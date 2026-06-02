namespace LogEOT.Core.Interfaces;

public interface ILogSource
{
    IEnumerable<string> GetLogFiles(string path);
}