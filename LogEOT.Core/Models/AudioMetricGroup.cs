namespace LogEOT.Core.Models;

public class AudioMetricGroup
{
    public string GroupName { get; set; } = "";
    public List<AudioMetric> Metrics { get; set; } = new();
}
