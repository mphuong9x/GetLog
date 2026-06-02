using LogEOT.Core.Interfaces;

namespace LogEOT.Infrastructure.Sources;

public class FolderLogSource : ILogSource
{
    private static readonly string[] SearchPatterns = { "*.log", "*.txt", "*.xml" };

    public IEnumerable<string> GetLogFiles(string path) => GetLogFiles(path, false);

    public List<string> GetLogFiles(string folderPath, bool includeSubFolders)
    {
        if (!Directory.Exists(folderPath))
            throw new DirectoryNotFoundException(folderPath);

        var option = includeSubFolders
            ? SearchOption.AllDirectories
            : SearchOption.TopDirectoryOnly;

        return SearchPatterns
            .SelectMany(pattern => Directory.GetFiles(folderPath, pattern, option))
            .ToList();
    }
}