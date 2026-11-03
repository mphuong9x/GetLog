using LogEOT.Core.Models;
using LogEOT.Infrastructure.Services;
using Xunit;

namespace LogEOT.Infrastructure.Tests;

public sealed class AnalyzeKeysConfigServiceTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "GetLog-config-" + Guid.NewGuid().ToString("N"));
    private string ConfigPath => Path.Combine(_folder, "Wifi Test.json");
    private readonly AnalyzeKeysConfigService _service = new();

    public AnalyzeKeysConfigServiceTests() => Directory.CreateDirectory(_folder);

    [Fact]
    public void RoundTripPreservesRowOrderDuplicatesExactTextAndIncompleteRows()
    {
        var rows = new List<KeyConfig>
        {
            new() { Key = "got rssi value", AltKey = "  signal:  ", ColumnName = "RSSI 2G" },
            new() { Key = "got rssi value", ColumnName = "RSSI 5G" },
            new() { Key = " Nhiệt độ: ", ColumnName = "" },
            new() { AltKey = "unfinished", ColumnName = "Draft" },
            new(),
            new() { Key = " ", AltKey = "\t", ColumnName = " " }
        };

        _service.Save(ConfigPath, rows);
        var config = _service.Load(ConfigPath);

        Assert.Equal(1, config.Version);
        Assert.Equal("Wifi Test", config.Name);
        Assert.Equal(rows.Take(4).Select(Row), config.Keys.Select(Row));
        Assert.Equal(6, rows.Count);
        Assert.Equal("", rows[2].ColumnName);
    }

    [Fact]
    public void ReloadedDuplicateKeysStillExtractTwoDifferentRssiValues()
    {
        _service.Save(ConfigPath,
        [
            new() { Key = "got rssi value, value =", ColumnName = "RSSI 2G" },
            new() { Key = "got rssi value, value =", ColumnName = "RSSI 5G" }
        ]);
        string logPath = Path.Combine(_folder, "PASS_AABBCCDDEEFF_wifi.log");
        File.WriteAllText(logPath, """
            --- [SL-RF-ETH] Wifi Test
            [PASS] connect UAP-AC-HD-Test-2G227
            [PASS] got rssi value, value = "-67" (-79 <= x <= -65)
            [PASS] 2g tx iperf, value = "52.0" (10 <= x)
            [PASS] 2g rx iperf, value = "24.5" (10 <= x)
            [PASS] connect UAP-AC-HD-Test-5G227
            [PASS] got rssi value, value = "-70" (-79 <= x <= -65)
            [PASS] 5g tx iperf, value = "74.1" (20 <= x)
            [PASS] 5g rx iperf, value = "65.2" (20 <= x)
            [PASS] wifi
            """);

        var loaded = _service.Load(ConfigPath);
        var keys = loaded.Keys.Select(Row).ToList();
        var result = new LogParser().Parse(logPath, keys);

        Assert.Equal("-67", result.Values["RSSI 2G"]);
        Assert.Equal("-70", result.Values["RSSI 5G"]);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("{\"version\":2,\"name\":\"Future\",\"keys\":[]}")]
    [InlineData("{\"version\":1,\"name\":\"Invalid\",\"keys\":null}")]
    [InlineData("{\"version\":1,\"name\":\"\",\"keys\":[]}")]
    [InlineData("{\"version\":1,\"name\":\"Invalid\",\"keys\":[null]}")]
    [InlineData("{\"version\":1,\"name\":\"Invalid\",\"keys\":[{\"key\":null}]}")]
    public void RejectsMalformedOrUnsupportedConfigs(string json)
    {
        File.WriteAllText(ConfigPath, json);

        Assert.ThrowsAny<Exception>(() => _service.Load(ConfigPath));
        Assert.Equal(json, File.ReadAllText(ConfigPath));
    }

    [Fact]
    public void SavingAgainReplacesTheConfigWithoutLeavingTemporaryFiles()
    {
        _service.Save(ConfigPath, [new() { Key = "Old", ColumnName = "Old column" }]);
        _service.Save(ConfigPath,
        [
            new() { Key = "New", ColumnName = "First" },
            new() { Key = "New", ColumnName = "Second" }
        ]);

        Assert.Equal(new[] { "First", "Second" }, _service.Load(ConfigPath).Keys.Select(key => key.ColumnName));
        Assert.Single(Directory.GetFiles(_folder));
    }

    private static (string Key, string AltKey, string ColumnName) Row(KeyConfig row) =>
        (row.Key, row.AltKey, row.ColumnName);

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
    }
}
