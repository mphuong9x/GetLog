using System.Globalization;
using LogEOT.Core.Models;

namespace LogEOT.Infrastructure.Services;

public sealed class LocalLogDownloadService
{
    public int LastScanWarningCount { get; private set; }
    public int LastDownloadFailureCount { get; private set; }
    public bool LastRunHadErrors => LastScanWarningCount > 0 || LastDownloadFailureCount > 0;

    public Task<int> DownloadLogsAsync(string sourceRoot, string outputRoot, ScanOptions options,
        Action<string> logMessage, Action<int, int> progressUpdate) => Task.Run(() =>
    {
        LastScanWarningCount = 0;
        LastDownloadFailureCount = 0;
        string source = Path.TrimEndingDirectorySeparator(Path.GetFullPath(sourceRoot));
        string output = Path.TrimEndingDirectorySeparator(Path.GetFullPath(outputRoot));
        if (!Directory.Exists(source)) throw new DirectoryNotFoundException(source);
        if (source.Equals(output, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Output directory must be different from the source folder.");
        bool outputInsideSource = IsWithin(output, source);

        var rules = new LogDownloadRules(options);
        var files = new List<(string Source, string Directory)>();
        var pending = new Stack<string>();
        pending.Push(source);
        logMessage($"Scanning on local folder {source} (including all subfolders)...");

        // Snapshot every matching source before copying, and exclude an output folder
        // nested inside the source so existing downloads are never scanned again.
        while (pending.TryPop(out string? directory))
        {
            try
            {
                var entries = Directory.GetFileSystemEntries(directory);
                Array.Sort(entries, StringComparer.OrdinalIgnoreCase);
                foreach (string entry in entries)
                {
                    if (outputInsideSource && IsWithin(entry, output)) continue;
                    try
                    {
                        var attributes = File.GetAttributes(entry);
                        if ((attributes & FileAttributes.ReparsePoint) != 0)
                        {
                            Warn(entry, "Symbolic links and junctions are skipped to avoid cycles.");
                            continue;
                        }
                        if ((attributes & FileAttributes.Directory) != 0)
                            pending.Push(entry);
                        else if (rules.MatchFile(Path.GetFileName(entry)) && MatchDirectory(directory, options))
                            files.Add((entry, DestinationDirectory(directory, source, output, options)));
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        Warn(entry, ex.Message);
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Warn(directory, ex.Message);
            }
        }

        logMessage($"Found {files.Count} files in local folder.");
        logMessage("Downloading from local folder...");
        progressUpdate(0, files.Count);
        int done = 0;
        long lastProgress = Environment.TickCount64;
        foreach (var file in files)
        {
            try
            {
                string target = Path.Combine(file.Directory, Path.GetFileName(file.Source));
                if (target.Equals(file.Source, StringComparison.OrdinalIgnoreCase))
                {
                    // Keeping the root folder with its parent as output can map a
                    // log to itself. It is already at the requested destination.
                    logMessage($"Already at destination: {target}");
                }
                else
                {
                    if (!outputInsideSource && IsWithin(target, source) && File.Exists(target))
                        throw new IOException($"Cannot overwrite a file in the source folder: {target}");
                    Directory.CreateDirectory(file.Directory);
                    File.Copy(file.Source, target, overwrite: true);
                }
                done++;
            }
            catch (Exception ex)
            {
                LastDownloadFailureCount++;
                logMessage($"Error copying {file.Source}: {ex.Message}");
            }
            long now = Environment.TickCount64;
            if (now - lastProgress >= 100)
            {
                progressUpdate(done, files.Count);
                lastProgress = now;
            }
        }
        progressUpdate(done, files.Count);
        logMessage($"DONE on local folder. Downloaded {done}/{files.Count} files." +
            (LastDownloadFailureCount > 0 ? $" ({LastDownloadFailureCount} failed)" : ""));
        return done;

        void Warn(string path, string message)
        {
            LastScanWarningCount++;
            logMessage($"Warning: Could not scan {path}: {message}");
        }
    });

    private static bool IsWithin(string path, string root)
    {
        string relative = Path.GetRelativePath(root, path);
        return relative == "." || (!Path.IsPathRooted(relative) && relative != ".."
            && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal));
    }

    private static string[] Parts(string path) => path.Split(
        [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries);

    private static bool MatchDirectory(string directory, ScanOptions options)
    {
        // Local folders may start at any level of a server archive, or contain extra
        // wrapper folders. Match folder names rather than fixed absolute depths.
        string[] parts = Parts(directory);
        if (!string.IsNullOrWhiteSpace(options.Model)
            && !parts.Contains(options.Model, StringComparer.OrdinalIgnoreCase)) return false;
        if (!string.IsNullOrEmpty(options.MoFilter)
            && !parts.Contains(options.MoFilter, StringComparer.OrdinalIgnoreCase)) return false;
        foreach (string part in parts)
        {
            if (!DateTime.TryParseExact(part, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var date)) continue;
            if (options.StartDate.HasValue && date.Date < options.StartDate.Value.Date) return false;
            if (options.EndDate.HasValue && date.Date > options.EndDate.Value.Date) return false;
        }
        return true;
    }

    private static string DestinationDirectory(string directory, string source, string output, ScanOptions options)
    {
        string relative = Path.GetRelativePath(source, directory);
        if (options.KeepRootFolder)
        {
            string rootName = Path.GetFileName(source);
            return relative == "." ? Path.Combine(output, rootName) : Path.Combine(output, rootName, relative);
        }

        string[] parts = Parts(directory);
        if (!string.IsNullOrWhiteSpace(options.Model))
        {
            int modelIndex = Array.FindLastIndex(parts,
                part => part.Equals(options.Model, StringComparison.OrdinalIgnoreCase));
            string station = modelIndex >= 0 && modelIndex + 1 < parts.Length ? parts[modelIndex + 1] : "UNKNOWN";
            return Path.Combine(output, options.Model, station);
        }

        // With all models selected, the source root plays the same role as the
        // remote logs root: its first two child folders identify model/station.
        string[] relativeParts = relative == "." ? [] : Parts(relative);
        return Path.Combine(output, relativeParts.Length > 0 ? relativeParts[0] : "UNKNOWN_MODEL",
            relativeParts.Length > 1 ? relativeParts[1] : "UNKNOWN");
    }
}
