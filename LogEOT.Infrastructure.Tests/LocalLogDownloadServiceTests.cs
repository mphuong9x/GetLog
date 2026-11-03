using LogEOT.Core.Models;
using LogEOT.Infrastructure.Services;
using Xunit;

namespace LogEOT.Infrastructure.Tests;

public sealed class LocalLogDownloadServiceTests : IDisposable
{
    private readonly string _workspace = Path.Combine(Path.GetTempPath(), "GetLog-local-" + Guid.NewGuid().ToString("N"));
    private string Source => Path.Combine(_workspace, "archive");
    private string Output => Path.Combine(_workspace, "output");

    [Fact]
    public async Task SearchesRootAndEveryNestedLevelWithoutStoppingAtFirstLog()
    {
        Write("PASS_ROOT.log");
        Write("a/PASS_A.txt");
        Write("a/b/c/d/PASS_DEEP.xml");
        Write("other/FAIL_B.log");
        Write("other/PASS_BAD.zip");
        Write("other/notes.txt");
        var service = new LocalLogDownloadService();
        var progress = new List<(int, int)>();

        int count = await service.DownloadLogsAsync(Source, Output,
            new ScanOptions { Pass = true, Fail = true, KeepRootFolder = true }, _ => { },
            (done, total) => progress.Add((done, total)));

        Assert.Equal(4, count);
        Assert.False(service.LastRunHadErrors);
        Assert.Equal((4, 4), progress.Last());
        foreach (string relative in new[] { "PASS_ROOT.log", "a/PASS_A.txt", "a/b/c/d/PASS_DEEP.xml", "other/FAIL_B.log" })
            Assert.Equal(relative, File.ReadAllText(Path.Combine(Output, "archive", relative)));
        Assert.Equal(6, Directory.GetFiles(Source, "*", SearchOption.AllDirectories).Length);
    }

    [Fact]
    public async Task AppliesModelMoDatePassAndMacFiltersInsideWrapperFolders()
    {
        const string wanted = "backup/logs/M/S1/MO1/Sfis/2026-10-28/extra/PASS_AABBCCDDEEFF.log";
        Write(wanted);
        Write("backup/logs/M/S1/MO1/Sfis/2026-10-27/PASS_AABBCCDDEEFF.log");
        Write("backup/logs/M/S1/MO1/Sfis/2026-10-29/PASS_AABBCCDDEEFF.log");
        Write("backup/logs/M/S1/MO2/Sfis/2026-10-28/PASS_AABBCCDDEEFF.log");
        Write("backup/logs/OTHER/S1/MO1/Sfis/2026-10-28/PASS_AABBCCDDEEFF.log");
        Write("backup/logs/M/S1/MO1/Sfis/2026-10-28/FAIL_AABBCCDDEEFF.log");
        Write("backup/logs/M/S1/MO1/Sfis/2026-10-28/PASS_112233445566.log");
        var options = new ScanOptions
        {
            Model = "m", MoFilter = "mo1", Pass = true,
            StartDate = new DateTime(2026, 10, 28), EndDate = new DateTime(2026, 10, 28),
            MacList = new HashSet<string> { "aabbccddeeff" }
        };
        var service = new LocalLogDownloadService();

        Assert.Equal(1, await Download(service, options));
        Assert.Equal(wanted, File.ReadAllText(Path.Combine(Output, "m", "S1", "PASS_AABBCCDDEEFF.log")));
        Assert.False(service.LastRunHadErrors);
    }

    [Fact]
    public async Task FiltersAlsoWorkWhenSelectedFolderIsBelowModelAndMo()
    {
        const string relative = "M/S1/MO1/Sfis/2026-10-28/PASS_A.log";
        Write(relative);
        var options = new ScanOptions { Model = "M", MoFilter = "MO1", Pass = true };
        var service = new LocalLogDownloadService();

        Assert.Equal(1, await service.DownloadLogsAsync(Path.GetDirectoryName(Path.Combine(Source, relative))!,
            Output, options, _ => { }, (_, _) => { }));
        Assert.Equal(relative, File.ReadAllText(Path.Combine(Output, "M", "S1", "PASS_A.log")));
    }

    [Fact]
    public async Task NestedOutputIsExcludedOnRepeatedRuns()
    {
        Write("PASS_A.log");
        string output = Path.Combine(Source, "downloads");
        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, "PASS_OLD.log"), "old download");
        var options = new ScanOptions { Pass = true, KeepRootFolder = true };
        var service = new LocalLogDownloadService();

        for (int run = 0; run < 2; run++)
            Assert.Equal(1, await service.DownloadLogsAsync(Source, output, options, _ => { }, (_, _) => { }));

        Assert.Equal(2, Directory.GetFiles(output, "*", SearchOption.AllDirectories).Length);
        Assert.Equal("PASS_A.log", File.ReadAllText(Path.Combine(Source, "PASS_A.log")));
    }

    [Fact]
    public async Task RejectsOutputEqualToSourceWithoutChangingFiles()
    {
        Write("PASS_A.log");
        var service = new LocalLogDownloadService();

        await Assert.ThrowsAsync<ArgumentException>(() => service.DownloadLogsAsync(Source,
            Source, new ScanOptions { Pass = true }, _ => { }, (_, _) => { }));

        Assert.Equal("PASS_A.log", File.ReadAllText(Path.Combine(Source, "PASS_A.log")));
        Assert.Single(Directory.GetFiles(_workspace, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task AllowsParentAsOutputAndCopiesOutsideTheSourceFolder()
    {
        const string relative = "M/S1/MO1/Sfis/2026-10-28/PASS_A.log";
        Write(relative);
        var service = new LocalLogDownloadService();

        Assert.Equal(1, await service.DownloadLogsAsync(Source, _workspace,
            new ScanOptions { Model = "M", Pass = true }, _ => { }, (_, _) => { }));

        Assert.False(service.LastRunHadErrors);
        Assert.Equal(relative, File.ReadAllText(Path.Combine(_workspace, "M", "S1", "PASS_A.log")));
        Assert.Equal(relative, File.ReadAllText(Path.Combine(Source, relative)));
    }

    [Fact]
    public async Task KeepingRootWithParentOutputRecognizesFilesAlreadyAtDestination()
    {
        Write("M/S1/PASS_A.log");
        var service = new LocalLogDownloadService();
        var messages = new List<string>();

        Assert.Equal(1, await service.DownloadLogsAsync(Source, _workspace,
            new ScanOptions { Pass = true, KeepRootFolder = true }, messages.Add, (_, _) => { }));

        Assert.False(service.LastRunHadErrors);
        Assert.Contains(messages, message => message.StartsWith("Already at destination:"));
        Assert.Equal("M/S1/PASS_A.log", File.ReadAllText(Path.Combine(Source, "M", "S1", "PASS_A.log")));
        Assert.Single(Directory.GetFiles(_workspace, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task ParentOutputCannotOverwriteAnotherSourceFile()
    {
        Write("S1/PASS_A.log");
        Write("S1/nested/PASS_A.log");
        var service = new LocalLogDownloadService();

        Assert.Equal(1, await service.DownloadLogsAsync(Source, _workspace,
            new ScanOptions { Model = "archive", Pass = true }, _ => { }, (_, _) => { }));

        Assert.Equal(1, service.LastDownloadFailureCount);
        Assert.Equal("S1/PASS_A.log", File.ReadAllText(Path.Combine(Source, "S1", "PASS_A.log")));
        Assert.Equal("S1/nested/PASS_A.log", File.ReadAllText(Path.Combine(Source, "S1", "nested", "PASS_A.log")));
    }

    [Fact]
    public async Task ReportsCopyFailuresAndContinuesWithOtherFiles()
    {
        Write("PASS_A.log");
        Write("PASS_B.log");
        Directory.CreateDirectory(Path.Combine(Output, "UNKNOWN_MODEL", "UNKNOWN", "PASS_A.log"));
        var service = new LocalLogDownloadService();
        var progress = new List<(int, int)>();

        int count = await service.DownloadLogsAsync(Source, Output, new ScanOptions { Pass = true },
            _ => { }, (done, total) => progress.Add((done, total)));

        Assert.Equal(1, count);
        Assert.True(service.LastRunHadErrors);
        Assert.Equal(1, service.LastDownloadFailureCount);
        Assert.Equal((1, 2), progress.Last());
        Assert.Equal("PASS_B.log", File.ReadAllText(Path.Combine(Output, "UNKNOWN_MODEL", "UNKNOWN", "PASS_B.log")));
    }

    [Fact]
    public async Task NoMatchesReturnsZeroWithCompleteProgress()
    {
        Write("FAIL_A.log");
        var service = new LocalLogDownloadService();
        var progress = new List<(int, int)>();

        Assert.Equal(0, await service.DownloadLogsAsync(Source, Output, new ScanOptions { Pass = true },
            _ => { }, (done, total) => progress.Add((done, total))));
        Assert.Equal((0, 0), progress.Last());
        Assert.False(service.LastRunHadErrors);
    }

    private Task<int> Download(LocalLogDownloadService service, ScanOptions options) =>
        service.DownloadLogsAsync(Source, Output, options, _ => { }, (_, _) => { });

    private void Write(string relative)
    {
        string path = Path.Combine(Source, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, relative);
    }

    public void Dispose()
    {
        if (Directory.Exists(_workspace)) Directory.Delete(_workspace, recursive: true);
    }
}
