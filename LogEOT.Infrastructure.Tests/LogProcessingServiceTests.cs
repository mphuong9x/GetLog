using LogEOT.Core.Models;
using LogEOT.Infrastructure.Services;
using Xunit;

namespace LogEOT.Infrastructure.Tests;

public class LogProcessingServiceTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"LogEOT-{Guid.NewGuid():N}");
    private readonly List<(string Key, string AltKey, string ColumnName)> _keys =
    [
        ("Voltage", "", "Voltage")
    ];

    public LogProcessingServiceTests()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(Path.Combine(_folder, "PASS_AABBCCDDEEFF_unit.log"), "Voltage: 12.0");
        File.WriteAllText(Path.Combine(_folder, "FAIL_112233445566_unit.log"), "Stopped before measurement");
        File.WriteAllText(Path.Combine(_folder, "other_unit.txt"), "Voltage: 11.5");
    }

    [Fact]
    public void ProcessFolder_CanSelectPassLogs()
    {
        var results = new LogProcessingService().ProcessFolder(
            _folder,
            _keys,
            selection: LogSelection.Pass);

        var result = Assert.Single(results);
        Assert.Equal("PASS", result.Result);
        Assert.Equal("AABBCCDDEEFF", result.MAC);
        Assert.Equal("12.0", result.Values["Voltage"]);
    }

    [Fact]
    public void ProcessFolder_CanSelectFailLogs()
    {
        var results = new LogProcessingService().ProcessFolder(
            _folder,
            _keys,
            selection: LogSelection.Fail);

        var result = Assert.Single(results);
        Assert.Equal("FAIL", result.Result);
        Assert.Equal("112233445566", result.MAC);
        Assert.Null(result.Values["Voltage"]);
    }

    [Fact]
    public void ProcessFolder_AllIncludesEverySupportedInputFile()
    {
        var results = new LogProcessingService().ProcessFolder(
            _folder,
            _keys,
            selection: LogSelection.All);

        Assert.Equal(3, results.Count);
        Assert.Contains(results, result => result.Result == "PASS");
        Assert.Contains(results, result => result.Result == "FAIL");
        Assert.Contains(results, result => result.Result == "UNKNOWN");
    }

    public void Dispose()
    {
        if (Directory.Exists(_folder))
            Directory.Delete(_folder, recursive: true);
    }
}
