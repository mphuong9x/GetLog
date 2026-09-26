using System.Collections.Concurrent;
using System.Diagnostics;
using LogEOT.Core.Models;

namespace LogEOT.Infrastructure.Services;

public class SftpDownloadService
{
    private readonly SftpConfig _config;
    private readonly Func<ISftpLogSession> _createSession;
    private int _hadErrors;
    private int _scanWarningCount;
    private int _downloadFailureCount;
    public bool LastRunHadErrors => Volatile.Read(ref _hadErrors) != 0;
    public int LastScanWarningCount => Volatile.Read(ref _scanWarningCount);
    public int LastDownloadFailureCount => Volatile.Read(ref _downloadFailureCount);

    public SftpDownloadService(SftpConfig? config = null)
        : this(config ?? new SftpConfig(), () => new SftpLogSession()) { }

    internal SftpDownloadService(SftpConfig config, Func<ISftpLogSession> createSession)
    {
        _config = config;
        _createSession = createSession;
    }

    public async Task<int> DownloadLogsAsync(List<SftpServer> servers, string localRoot,
        ScanOptions options, Action<string> logMessage, Action<int, int> progressUpdate)
    {
        Interlocked.Exchange(ref _hadErrors, 0);
        Interlocked.Exchange(ref _scanWarningCount, 0);
        Interlocked.Exchange(ref _downloadFailureCount, 0);
        return await Task.Run(async () =>
        {
            Directory.CreateDirectory(localRoot);
            var rules = new LogDownloadRules(options);
            int grandTotal = 0;

            // Servers retain their selected order: their output paths can overlap.
            foreach (var server in servers)
            {
                var elapsed = Stopwatch.StartNew();
                logMessage($"--- Processing Server {server.Host} ---");
                logMessage($"Scanning on {server.Host}...");
                using var primary = _createSession();
                try { primary.Open(server, _config); }
                catch (Exception ex)
                {
                    MarkScanWarning();
                    logMessage($"Error connecting to {server.Host}: {ex.Message}");
                    continue;
                }

                var scanClock = Stopwatch.StartNew();
                var branches = new List<ScanBranch>();
                foreach (var root in _config.Roots)
                {
                    string path = string.IsNullOrWhiteSpace(options.Model) ? root : $"{root}/{options.Model}";
                    try
                    {
                        // List each root once. Keep branch slots in listing order,
                        // then scan them independently without changing merge order.
                        foreach (var item in primary.ListDirectory(path))
                        {
                            if (item.Name is "." or "..") continue;
                            if (item.IsDirectory)
                            {
                                if (rules.VisitDirectory(path, item.Name))
                                    branches.Add(new ScanBranch(path + "/" + item.Name));
                            }
                            else if (rules.MatchFile(item.Name))
                            {
                                var branch = new ScanBranch(null);
                                branch.Files.Add(path + "/" + item.Name);
                                branches.Add(branch);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        MarkScanWarning();
                        logMessage($"Warning: Could not access {server.Host}:{path}: {ex.Message}");
                    }
                }

                var scanQueue = new ConcurrentQueue<ScanBranch>(branches.Where(branch => branch.Path != null));
                await RunWorkers(primary, server, Math.Min(scanQueue.Count, Math.Clamp(_config.ScanWorkers, 1, 8)),
                    () => scanQueue.IsEmpty, session =>
                    {
                        while (scanQueue.TryDequeue(out var branch))
                            Walk(session, branch.Path!, branch.Files, rules, logMessage);
                    }, logMessage);

                var allFiles = branches.SelectMany(branch => branch.Files).ToList();
                scanClock.Stop();
                logMessage($"Found {allFiles.Count} files on {server.Host} (scan {scanClock.Elapsed.TotalSeconds:F1}s)");
                if (allFiles.Count == 0) continue;

                int done = 0, errors = 0;
                var failedFiles = new ConcurrentQueue<string>();
                var groups = new Dictionary<string, DownloadGroup>(StringComparer.OrdinalIgnoreCase);
                foreach (string remote in allFiles)
                {
                    try
                    {
                        string directory = rules.LocalDirectory(localRoot, remote);
                        string target = Path.GetFullPath(Path.Combine(directory, Path.GetFileName(remote)));
                        if (!groups.TryGetValue(target, out var group))
                            groups.Add(target, group = new DownloadGroup(directory));
                        group.Files.Add(remote);
                    }
                    catch (Exception ex) { RecordFailure(remote, ex); }
                }

                var downloadQueue = new ConcurrentQueue<DownloadGroup>(groups.Values);
                var progressGate = new object();
                long lastProgress = 0;
                logMessage($"Downloading from {server.Host}...");
                ReportProgress(true);

                await RunWorkers(primary, server,
                    Math.Min(downloadQueue.Count, Math.Clamp(_config.DownloadWorkers, 1, 10)),
                    () => downloadQueue.IsEmpty, session =>
                    {
                        var createdDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        while (downloadQueue.TryDequeue(out var group))
                        {
                            // Colliding filenames are serialized in scan order while
                            // independent destinations are balanced across workers.
                            foreach (string remote in group.Files)
                            {
                                try
                                {
                                    if (!createdDirectories.Contains(group.Directory))
                                    {
                                        Directory.CreateDirectory(group.Directory);
                                        createdDirectories.Add(group.Directory);
                                    }
                                    session.Download(remote, group.Directory);
                                    Interlocked.Increment(ref done);
                                }
                                catch (Exception ex) { RecordFailure(remote, ex); }
                                ReportProgress(false);
                            }
                        }
                    }, logMessage);

                ReportProgress(true);
                grandTotal += done;
                logMessage($"DONE on {server.Host}. Downloaded {done}/{allFiles.Count} files." +
                    (errors > 0 ? $" ({errors} failed)" : "") + $" Total {elapsed.Elapsed.TotalSeconds:F1}s.");
                foreach (var failure in failedFiles) logMessage($"    x {failure}");
                if (errors > 10) logMessage($"    ... and {errors - 10} more.");

                void RecordFailure(string remote, Exception exception)
                {
                    MarkDownloadFailure();
                    if (Interlocked.Increment(ref errors) <= 10)
                        failedFiles.Enqueue($"{remote} — {exception.Message}");
                }

                void ReportProgress(bool force)
                {
                    lock (progressGate)
                    {
                        long now = Environment.TickCount64;
                        if (!force && now - lastProgress < 100) return;
                        lastProgress = now;
                        progressUpdate(Volatile.Read(ref done), allFiles.Count);
                    }
                }
            }
            return grandTotal;
        });
    }

    private async Task RunWorkers(ISftpLogSession primary, SftpServer server, int count,
        Func<bool> isEmpty, Action<ISftpLogSession> consume, Action<string> logMessage)
    {
        if (count == 0) return;
        var workers = Enumerable.Range(1, count - 1).Select(_ => Task.Run(() =>
        {
            if (isEmpty()) return;
            // Dispose even if opening fails; primary can drain any remaining work.
            using var session = _createSession();
            try { session.Open(server, _config); }
            catch (Exception ex)
            {
                logMessage($"Warning: Could not open extra connection on {server.Host}: {ex.Message}");
                return;
            }
            consume(session);
        })).ToList();
        // Reuse the open scan connection as worker 1, avoiding another handshake.
        workers.Add(Task.Run(() => consume(primary)));
        await Task.WhenAll(workers);
    }

    private void Walk(ISftpLogSession session, string path, List<string> files,
        LogDownloadRules rules, Action<string> logMessage)
    {
        try
        {
            foreach (var item in session.ListDirectory(path))
            {
                if (item.Name is "." or "..") continue;
                string full = path + "/" + item.Name;
                if (item.IsDirectory)
                {
                    if (rules.VisitDirectory(path, item.Name)) Walk(session, full, files, rules, logMessage);
                }
                else if (rules.MatchFile(item.Name)) files.Add(full);
            }
        }
        catch (Exception ex)
        {
            MarkScanWarning();
            logMessage($"Warning: Could not access {path}: {ex.Message}");
        }
    }

    private void MarkError() => Interlocked.Exchange(ref _hadErrors, 1);
    private void MarkScanWarning()
    {
        Interlocked.Increment(ref _scanWarningCount);
        MarkError();
    }
    private void MarkDownloadFailure()
    {
        Interlocked.Increment(ref _downloadFailureCount);
        MarkError();
    }
    private sealed class ScanBranch(string? path)
    {
        public string? Path { get; } = path;
        public List<string> Files { get; } = [];
    }
    private sealed class DownloadGroup(string directory)
    {
        public string Directory { get; } = directory;
        public List<string> Files { get; } = [];
    }
}
