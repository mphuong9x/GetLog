namespace LogEOT.Infrastructure.Parsers;

public static class FileNameParser
{
    public static string Parse(string filePath)
    {
        var fileName = Path.GetFileNameWithoutExtension(filePath);

        var parts = fileName.Split('_');

        if (parts.Length < 2)
        {
            return fileName;
        }

        return parts[1];
    }
}