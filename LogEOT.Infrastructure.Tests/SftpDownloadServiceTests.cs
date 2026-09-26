using System.Collections.Concurrent;
using LogEOT.Core.Models;
using LogEOT.Infrastructure.Services;
using Xunit;

namespace LogEOT.Infrastructure.Tests;

public class SftpDownloadServiceTests : IDisposable
{
    private readonly string _output = Path.Combine(Path.GetTempPath(), "GetLog-tests-" + Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData(false, "M", "M/S1")]
    [InlineData(false, "", "M/S1")]
    [InlineData(false, "SelectedModel", "SelectedModel/S1")]
    [InlineData(true, "M", "logs/M/S1/MO1/Sfis/2026-09-26")]
    public void OutputLayoutIsUnchanged(bool keepRoot, string model, string expected)
    {
        var rules = new LogDownloadRules(new ScanOptions { Model = model, KeepRootFolder = keepRoot });
        string actual = rules.LocalDirectory(_output, "/logs/M/S1/MO1/Sfis/2026-09-26/PASS_ABC.log");
        Assert.Equal(Path.GetFullPath(Path.Combine(_output, expected)), Path.GetFullPath(actual));
    }

    [Fact]
    public void OptimizedMacMatchingMatchesLegacySubstringSearch()
    {
        var patterns = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "AABBCCDDEEFF", "000000000001", "123", "aa:bb:cc", "special" };
        var rules = new LogDownloadRules(new ScanOptions { Pass = true, Fail = true, MacList = patterns });
        var names = new List<string>
        {
            "PASS_0aabbccddeeff0.log", "FAIL_000000000001.txt", "PASS_123.xml",
            "PASS_AA:BB:CC.txt", "FAIL_SPECIAL.xml", "PASS_fffffffffffe.log", "PASS_OTHER.log"
        };
        var random = new Random(42);
        for (int i = 0; i < 1000; i++)
        {
            string token = string.Concat(Enumerable.Range(0, 32).Select(_ => "0123456789abcdef_"[random.Next(17)]));
            if (i % 3 == 0) token += "aabbccddeeff";
            names.Add("PASS_" + token + ".log");
        }
        foreach (string name in names)
            Assert.Equal(patterns.Any(pattern => name.IndexOf(pattern, StringComparison.OrdinalIgnoreCase) >= 0),
                rules.MatchFile(name));
        Assert.False(rules.MatchFile("PASS_AABBCCDDEEFF.zip"));
        Assert.False(rules.MatchFile("OTHER_AABBCCDDEEFF.log"));
        var passOnly = new LogDownloadRules(new ScanOptions { Pass = true });
        Assert.False(passOnly.MatchFile("FAIL_AABBCCDDEEFF.log"));
    }

    [Fact]
    public void DirectoryFiltersPreserveMoDateAndLegacyFolders()
    {
        var rules = new LogDownloadRules(new ScanOptions
        {
            MoFilter = "MO1", StartDate = new DateTime(2026, 9, 1), EndDate = new DateTime(2026, 9, 26)
        });
        Assert.True(rules.VisitDirectory("/logs/M/S1", "mo1"));
        Assert.False(rules.VisitDirectory("/logs/M/S1", "MO2"));
        Assert.True(rules.VisitDirectory("/logs/M/S1/MO1/Sfis", "2026-09-26"));
        Assert.False(rules.VisitDirectory("/logs/M/S1/MO1/Sfis", "2026-08-31"));
        Assert.False(rules.VisitDirectory("/logs/M/S1/MO1/Sfis", "2026-09-27"));
        Assert.True(rules.VisitDirectory("/logs/M/S1/MO1/Sfis", "Legacy"));
    }

    [Fact]
    public async Task ScanBranchesRunConcurrentlyAndListEachDirectoryOnce()
    {
        var fake = new FakeServer();
        fake.Tree["/logs/M"] = [new("S1", true), new("S2", true)];
        fake.Tree["/logs/M/S1"] = [new("PASS_A.log", false)];
        fake.Tree["/logs/M/S2"] = [new("PASS_B.log", false)];
        using var reached = new CountdownEvent(2);
        fake.BeforeList = path =>
        {
            if (path == "/logs/M") return;
            reached.Signal();
            if (!reached.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException("Scan branches did not overlap");
        };
        var service = CreateService(fake, ["/logs"]);
        int count = await Download(service);
        Assert.Equal(2, count);
        Assert.False(service.LastRunHadErrors);
        Assert.All(fake.ListCounts.Values, calls => Assert.Equal(1, calls));
        Assert.Equal(0, fake.SessionRaces);
        Assert.Equal(fake.Created, fake.Disposed);
    }

    [Fact]
    public async Task DownloadsBalanceWorkWithoutRacingSameDestinationOrChangingServerOrder()
    {
        var fake = new FakeServer();
        foreach (string root in new[] { "/first", "/second" })
        {
            fake.Tree[root + "/M"] = [new("S1", true)];
            fake.Tree[root + "/M/S1"] = [new("PASS_A.log", false), new("PASS_B.log", false)];
        }
        using var reached = new CountdownEvent(2);
        int started = 0;
        fake.BeforeDownload = _ =>
        {
            if (Interlocked.Increment(ref started) > 2) return;
            reached.Signal();
            if (!reached.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException("Transfers did not overlap");
        };
        var service = CreateService(fake, ["/first", "/second"]);
        int count = await service.DownloadLogsAsync([new() { Host = "one" }, new() { Host = "two" }],
            _output, new ScanOptions { Model = "M", Pass = true }, _ => { }, (_, _) => { });
        Assert.Equal(8, count);
        Assert.False(service.LastRunHadErrors);
        Assert.Equal("two:/second/M/S1/PASS_A.log", File.ReadAllText(Path.Combine(_output, "M", "S1", "PASS_A.log")));
        Assert.Equal(new[] { "one:/first/M/S1/PASS_A.log", "one:/second/M/S1/PASS_A.log",
            "two:/first/M/S1/PASS_A.log", "two:/second/M/S1/PASS_A.log" },
            fake.Downloads.Where(item => item.EndsWith("PASS_A.log")).ToArray());
        Assert.Equal(0, fake.TargetRaces);
        Assert.Equal(0, fake.SessionRaces);
        Assert.Equal(fake.Created, fake.Disposed);
        Assert.Single(Directory.GetDirectories(_output)); // Only the existing model folder.
    }

    [Fact]
    public async Task ExtraConnectionFailureFallsBackToPrimaryAndDisposesFailedSessions()
    {
        var fake = new FakeServer { FailExtraConnections = true };
        fake.Tree["/logs/M"] = [new("S1", true), new("S2", true)];
        fake.Tree["/logs/M/S1"] = [new("PASS_A.log", false)];
        fake.Tree["/logs/M/S2"] = [new("PASS_B.log", false)];
        fake.BeforeList = path =>
        {
            if (path == "/logs/M") return;
            if (!SpinWait.SpinUntil(() => Volatile.Read(ref fake.FailedOpens) > 0, TimeSpan.FromSeconds(10)))
                throw new TimeoutException("Expected an extra connection attempt");
        };
        var service = CreateService(fake, ["/logs"]);
        Assert.Equal(2, await Download(service));
        Assert.False(service.LastRunHadErrors);
        Assert.True(fake.FailedOpens > 0);
        Assert.Equal(fake.Created, fake.Disposed);
    }

    [Fact]
    public async Task ScanAndDownloadErrorsAreReportedWithoutFalseCompleteProgress()
    {
        var fake = new FakeServer();
        fake.Tree["/logs/M"] = [new("Missing", true), new("PASS_OK.log", false), new("PASS_BAD.log", false)];
        fake.BeforeDownload = path => { if (path.EndsWith("PASS_BAD.log")) throw new IOException("Transfer failed"); };
        var progress = new ConcurrentQueue<(int Done, int Total)>();
        var service = CreateService(fake, ["/logs"]);
        int count = await service.DownloadLogsAsync([new() { Host = "one" }], _output,
            new ScanOptions { Model = "M", Pass = true }, _ => { }, (done, total) => progress.Enqueue((done, total)));
        Assert.Equal(1, count);
        Assert.True(service.LastRunHadErrors);
        Assert.Equal(1, service.LastScanWarningCount);
        Assert.Equal(1, service.LastDownloadFailureCount);
        Assert.Equal((1, 2), progress.Last());
        Assert.Equal(fake.Created, fake.Disposed);
    }

    private SftpDownloadService CreateService(FakeServer fake, string[] roots) =>
        new(new SftpConfig { Roots = roots, ScanWorkers = 2, DownloadWorkers = 2 }, fake.CreateSession);

    private Task<int> Download(SftpDownloadService service) => service.DownloadLogsAsync(
        [new() { Host = "one" }], _output, new ScanOptions { Model = "M", Pass = true }, _ => { }, (_, _) => { });

    public void Dispose()
    {
        if (Directory.Exists(_output)) Directory.Delete(_output, true);
    }

    private sealed class FakeServer
    {
        public Dictionary<string, IReadOnlyList<RemoteLogEntry>> Tree { get; } = [];
        public ConcurrentDictionary<string, int> ListCounts { get; } = new();
        public ConcurrentQueue<string> Downloads { get; } = new();
        private readonly ConcurrentDictionary<string, int> _targets = new(StringComparer.OrdinalIgnoreCase);
        public Action<string>? BeforeList { get; set; }
        public Action<string>? BeforeDownload { get; set; }
        public bool FailExtraConnections { get; set; }
        public int Created, Disposed, SessionRaces, TargetRaces, FailedOpens;
        public ISftpLogSession CreateSession() => new FakeSession(this, Interlocked.Increment(ref Created));

        private sealed class FakeSession(FakeServer owner, int id) : ISftpLogSession
        {
            private string _host = "";
            private int _busy;
            public void Open(SftpServer server, SftpConfig config)
            {
                if (owner.FailExtraConnections && id > 1)
                {
                    Interlocked.Increment(ref owner.FailedOpens);
                    throw new IOException("No connection slots");
                }
                _host = server.Host;
            }
            public IReadOnlyList<RemoteLogEntry> ListDirectory(string path)
            {
                Enter();
                try
                {
                    owner.ListCounts.AddOrUpdate(path, 1, (_, count) => count + 1);
                    owner.BeforeList?.Invoke(path);
                    return owner.Tree[path];
                }
                finally { Volatile.Write(ref _busy, 0); }
            }
            public void Download(string remote, string localDirectory)
            {
                Enter();
                string target = Path.GetFullPath(Path.Combine(localDirectory, Path.GetFileName(remote)));
                if (owner._targets.AddOrUpdate(target, 1, (_, count) => count + 1) != 1)
                    Interlocked.Increment(ref owner.TargetRaces);
                try
                {
                    owner.BeforeDownload?.Invoke(remote);
                    File.WriteAllText(target, _host + ":" + remote);
                    owner.Downloads.Enqueue(_host + ":" + remote);
                }
                finally
                {
                    owner._targets.AddOrUpdate(target, 0, (_, count) => count - 1);
                    Volatile.Write(ref _busy, 0);
                }
            }
            private void Enter()
            {
                if (Interlocked.Exchange(ref _busy, 1) != 0)
                {
                    Interlocked.Increment(ref owner.SessionRaces);
                    throw new InvalidOperationException("Session used concurrently");
                }
            }
            public void Dispose() => Interlocked.Increment(ref owner.Disposed);
        }
    }
}
