using System.Text.Json;
using LogEOT.Core.Models;

namespace LogEOT.Infrastructure.Services;

public sealed class AnalyzeKeysConfigService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public void Save(string path, IEnumerable<KeyConfig> keys)
    {
        var config = new AnalyzeKeysConfig
        {
            Version = 1,
            Name = Path.GetFileNameWithoutExtension(path),
            // Keep exact text, duplicates and order. Only omit entirely blank rows.
            Keys = keys.Where(key => !string.IsNullOrWhiteSpace(key.Key)
                    || !string.IsNullOrWhiteSpace(key.AltKey) || !string.IsNullOrWhiteSpace(key.ColumnName))
                .Select(key => new KeyConfig { Key = key.Key, AltKey = key.AltKey, ColumnName = key.ColumnName })
                .ToList()
        };
        Validate(config);
        string json = JsonSerializer.Serialize(config, JsonOptions);
        string temporaryPath = Path.GetFullPath(path) + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporaryPath, json);
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    public AnalyzeKeysConfig Load(string path)
    {
        var config = JsonSerializer.Deserialize<AnalyzeKeysConfig>(File.ReadAllText(path), JsonOptions)
            ?? throw new InvalidDataException("The selected file does not contain a key config.");
        Validate(config);
        return config;
    }

    private static void Validate(AnalyzeKeysConfig config)
    {
        if (config.Version != 1)
            throw new InvalidDataException($"Unsupported key config version: {config.Version}.");
        if (string.IsNullOrWhiteSpace(config.Name) || config.Keys == null)
            throw new InvalidDataException("Key config must include a name and a keys array.");
        if (config.Keys.Any(key => key == null || key.Key == null || key.AltKey == null || key.ColumnName == null))
            throw new InvalidDataException("Each config row must contain text values for Key, AltKey and ColumnName.");
    }
}
