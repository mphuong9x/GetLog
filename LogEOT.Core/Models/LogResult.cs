namespace LogEOT.Core.Models;

public class LogResult
{
    public string MAC { get; set; } = "";

    public Dictionary<string, string?> Values { get; set; } = new();
    public List<AudioMetricGroup>? AudioMetricGroups { get; set; }
}