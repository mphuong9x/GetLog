namespace LogEOT.Core.Models;

public sealed class AnalyzeKeysConfig
{
    public required int Version { get; set; }
    public required string Name { get; set; }
    public required List<KeyConfig> Keys { get; set; }
}
