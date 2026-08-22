using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using LogEOT.Core.Models;
using WinSCP;

namespace LogEOT.Infrastructure.Services;

public class SftpDownloadService
{
    private readonly SftpConfig _config;

    public SftpDownloadService(SftpConfig? config = null)
    {
        _config = config ?? new SftpConfig();
    }

    public async Task<int> DownloadLogsAsync(
        List<SftpServer> servers,
        string localRoot,
        ScanOptions options,
        Action<string> logMessage,
        Action<int, int> progressUpdate)
    {
        return await Task.Run(() =>
        {
            int grandTotal = 0;
            Directory.CreateDirectory(localRoot);

            foreach (var server in servers)
            {
                var host = server.Host;
                logMessage($"--- Processing Server {host} ---");

                var sessionOptions = new SessionOptions
                {
                    Protocol = Protocol.Sftp,
                    HostName = host,
                    PortNumber = _config.Port,
                    UserName = string.IsNullOrEmpty(server.UserName) ? _config.UserName : server.UserName,
                    Password = string.IsNullOrEmpty(server.Password) ? _config.Password : server.Password,
                    SshHostKeyPolicy = SshHostKeyPolicy.GiveUpSecurityAndAcceptAny
                };

                var allFiles = new List<string>();

                logMessage($"Scanning on {host}...");

                try
                {
                    using (var session = new Session { ExecutablePath = WinScpRuntime.ExecutablePath })
                    {
                        session.Open(sessionOptions);

                        foreach (var root in _config.Roots)
                        {
                            string scanPath = string.IsNullOrWhiteSpace(options.Model)
                                ? root
                                : $"{root}/{options.Model}";
                            if (!DirExists(session, scanPath))
                                continue;

                            Walk(session, scanPath, options, allFiles, logMessage);
                        }
                    }
                }
                catch (Exception ex)
                {
                    logMessage($"Error scanning {host}: {ex.Message}");
                    continue;
                }

                logMessage($"Found {allFiles.Count} files on {host}");
                if (allFiles.Count == 0) continue;

                var sessions = new List<Session>();
                int workerCount = Math.Min(Environment.ProcessorCount, 10);
                //int workerCount = 7;

                for (int i = 0; i < workerCount; i++)
                {
                    try
                    {
                        var s = new Session { ExecutablePath = WinScpRuntime.ExecutablePath };
                        s.Open(sessionOptions);
                        sessions.Add(s);
                    }
                    catch (Exception ex)
                    {
                        logMessage($"Error opening worker session on {host}: {ex.Message}");
                    }
                }

                if (sessions.Count == 0)
                {
                    logMessage($"Could not open any sessions for {host}. Skipping download.");
                    continue;
                }

                int done = 0;
                int total = allFiles.Count;
                int errors = 0;
                var failedFiles = new System.Collections.Concurrent.ConcurrentBag<string>();

                logMessage($"Downloading from {host}...");
                progressUpdate(0, total);

                int currentWorkers = sessions.Count;

                var tasks = new Task[currentWorkers];
                for (int w = 0; w < currentWorkers; w++)
                {
                    int workerIndex = w;
                    tasks[w] = Task.Run(() =>
                    {
                        var session = sessions[workerIndex];
                        for (int i = workerIndex; i < allFiles.Count; i += currentWorkers)
                        {
                            try
                            {
                                string remote = allFiles[i];
                                string localDir;

                                if (options.KeepRootFolder)
                                {
                                    string? remoteDirName = Path.GetDirectoryName(remote);
                                    string relativeDir = (remoteDirName ?? "").Replace("\\", "/").TrimStart('/');
                                    localDir = Path.Combine(localRoot, relativeDir);
                                }
                                else
                                {
                                    string model = string.IsNullOrWhiteSpace(options.Model)
                                        ? ExtractModel(remote)
                                        : options.Model;
                                    string station = ExtractStation(remote);
                                    localDir = Path.Combine(localRoot, model, station);
                                }

                                Directory.CreateDirectory(localDir);
                                session.GetFileToDirectory(remote, localDir);

                                int count = Interlocked.Increment(ref done);
                                progressUpdate(count, total);
                            }
                            catch (Exception ex)
                            {
                                Interlocked.Increment(ref errors);
                                failedFiles.Add($"{allFiles[i]} — {ex.Message}");
                            }
                        }
                    });
                }

                Task.WaitAll(tasks);
                grandTotal += done;

                foreach (var s in sessions)
                    s.Dispose();

                logMessage($"DONE on {host}. Downloaded {done}/{total} files." + (errors > 0 ? $" ({errors} failed)" : ""));

                if (errors > 0)
                {
                    logMessage($"  {errors} file(s) failed on {host} (showing up to 10):");
                    int shown = 0;
                    foreach (var f in failedFiles)
                    {
                        logMessage($"    x {f}");
                        if (++shown >= 10) break;
                    }
                    if (errors > shown)
                        logMessage($"    ... and {errors - shown} more.");
                }

                progressUpdate(total, total);
            }

            return grandTotal;
        });
    }

    static void Walk(Session session, string path, ScanOptions options, List<string> files, Action<string>? logMessage = null)
    {
        try
        {
            var list = session.ListDirectory(path);
            var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
            int depth = parts.Length;

            foreach (var item in list.Files)
            {
                if (item.Name == "." || item.Name == "..")
                    continue;

                string full = path + "/" + item.Name;

                if (item.IsDirectory)
                {
                    if (depth == 3)
                    {
                        if (!string.IsNullOrEmpty(options.MoFilter) && !item.Name.Equals(options.MoFilter, StringComparison.OrdinalIgnoreCase))
                            continue;
                    }
                    else if (depth == 5)
                    {
                        if (options.StartDate.HasValue || options.EndDate.HasValue)
                        {
                            if (DateTime.TryParseExact(item.Name, "yyyy-MM-dd", null, System.Globalization.DateTimeStyles.None, out DateTime folderDate))
                            {
                                if (options.StartDate.HasValue && folderDate.Date < options.StartDate.Value.Date) continue;
                                if (options.EndDate.HasValue && folderDate.Date > options.EndDate.Value.Date) continue;
                            }
                        }
                    }

                    Walk(session, full, options, files, logMessage);
                }
                else
                {
                    if (IsValidExt(item.Name))
                    {
                        bool isPass = item.Name.StartsWith("PASS", StringComparison.OrdinalIgnoreCase);
                        bool isFail = item.Name.StartsWith("FAIL", StringComparison.OrdinalIgnoreCase);

                        if (!isPass && !isFail)
                            continue;

                        if ((options.Pass && isPass) || (options.Fail && isFail))
                        {
                            if (options.MacList is { Count: > 0 })
                            {
                                bool hasMac = false;
                                foreach (var mac in options.MacList)
                                {
                                    if (item.Name.IndexOf(mac, StringComparison.OrdinalIgnoreCase) >= 0)
                                    {
                                        hasMac = true;
                                        break;
                                    }
                                }
                                if (!hasMac) continue;
                            }

                            files.Add(full);
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            logMessage?.Invoke($"Warning: Could not access {path}: {ex.Message}");
        }
    }

    static bool IsValidExt(string name)
    {
        return name.EndsWith(".log", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".xml", StringComparison.OrdinalIgnoreCase);
    }

    static bool DirExists(Session session, string path)
    {
        try
        {
            session.ListDirectory(path);
            return true;
        }
        catch
        {
            return false;
        }
    }

    static string ExtractStation(string remote)
    {
        var parts = remote.Split('/', StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length >= 3)
            return parts[2];

        return "UNKNOWN";
    }

    static string ExtractModel(string remote)
    {
        var parts = remote.Split('/', StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length >= 2)
            return parts[1];

        return "UNKNOWN_MODEL";
    }
}
