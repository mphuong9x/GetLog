namespace LogEOT.Core.Models;

public class LogResult
{
    public string MAC { get; set; } = "";

    public string Result { get; set; } = "";

    public Dictionary<string, string?> Values { get; set; } = new();

    // Limits printed next to the value on the matched line, keyed like Values (by column name).
    // Kept as written in the log so the export can show them with the same precision.
    public Dictionary<string, (string? Lower, string? Upper)> Limits { get; set; } = new();

    public List<AudioMetricGroup>? AudioMetricGroups { get; set; }
}
