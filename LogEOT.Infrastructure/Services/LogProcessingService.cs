using LogEOT.Core.Models;
using LogEOT.Infrastructure.Parsers;
using LogEOT.Infrastructure.Sources;

namespace LogEOT.Infrastructure.Services;

public class LogProcessingService
{
    public List<LogResult> ProcessFolder(
        string folderPath,
        List<(string Key, string AltKey, string ColumnName)> keys,
        bool isAudioLog = false,
        LogSelection? selection = null)
    {
        var results = new List<LogResult>();

        var source = new FolderLogSource();
        var parser = new LogParser();

        var files = source.GetLogFiles(folderPath, true);

        foreach (var file in files)
        {
            var fileName = Path.GetFileName(file);
            var result = GetResult(fileName);

            // Keep the legacy behavior for callers that do not select a type (for example,
            // Audio Logs): include PASS/unknown files and continue excluding FAIL retests.
            if (selection.HasValue
                ? !ShouldInclude(result, selection.Value)
                : result == "FAIL")
                continue;

            var mac = FileNameParser.Parse(fileName);

            LogResult logResult;
            if (isAudioLog)
            {
                logResult = parser.ParseAudio(file);
            }
            else
            {
                logResult = parser.Parse(file, keys);
            }

            if (string.IsNullOrEmpty(logResult.MAC))
            {
                logResult.MAC = mac;
            }

            logResult.Result = result;

            results.Add(logResult);
        }

        return results;
    }

    private static string GetResult(string fileName)
    {
        if (fileName.StartsWith("PASS_", StringComparison.OrdinalIgnoreCase))
            return "PASS";

        if (fileName.StartsWith("FAIL_", StringComparison.OrdinalIgnoreCase))
            return "FAIL";

        return "UNKNOWN";
    }

    private static bool ShouldInclude(string result, LogSelection selection) => selection switch
    {
        LogSelection.Pass => result == "PASS",
        LogSelection.Fail => result == "FAIL",
        LogSelection.All => true,
        _ => false
    };
}
