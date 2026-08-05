using LogEOT.Core.Models;
using LogEOT.Infrastructure.Parsers;
using LogEOT.Infrastructure.Sources;

namespace LogEOT.Infrastructure.Services;

public class LogProcessingService
{
    public List<LogResult> ProcessFolder(string folderPath, List<(string Key, string AltKey, string ColumnName)> keys, bool isAudioLog = false)
    {
        var results = new List<LogResult>();

        var source = new FolderLogSource();
        var parser = new LogParser();

        var files = source.GetLogFiles(folderPath, true);

        foreach (var file in files)
        {
            // Retest logs of a failed unit stop at the failing item, so they carry only a
            // fraction of the measurements — keep the passing run of each unit.
            if (Path.GetFileName(file).StartsWith("FAIL_", StringComparison.OrdinalIgnoreCase))
                continue;

            var mac = FileNameParser.Parse(Path.GetFileName(file));

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

            results.Add(logResult);
        }

        return results;
    }
}