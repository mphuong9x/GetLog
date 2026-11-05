using ClosedXML.Excel;
using LogEOT.Core.Models;
using LogEOT.Infrastructure.Exporters;
using LogEOT.Infrastructure.Services;
using Xunit;

namespace LogEOT.Infrastructure.Tests;

public sealed class LogParserLimitTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "GetLog-limits-" + Guid.NewGuid().ToString("N"));

    public LogParserLimitTests() => Directory.CreateDirectory(_folder);

    [Theory]
    [InlineData("(-79 <= x <= -65)", "-79", "-65")]
    [InlineData("(-68 ~ -52)", "-68", "-52")]
    [InlineData("(-52 ~ -68)", "-68", "-52")]
    [InlineData("( -79.00 <= x <= -65.00 )", "-79.00", "-65.00")]
    [InlineData("(10 <= x)", "10", null)]
    [InlineData("(x <= 30)", null, "30")]
    [InlineData("(-12.5 <= x)", "-12.5", null)]
    [InlineData("( x <= -6.25 )", null, "-6.25")]
    public void LegacyLimitFormatsAndValuesAreUnchanged(string text, string? lower, string? upper)
    {
        var result = Parse("Measurement = 12.0 " + text, [("Measurement", "", "Value")]);

        Assert.Equal("12.0", result.Values["Value"]);
        Assert.Equal((lower, upper), result.Limits["Value"]);
    }

    [Theory]
    [InlineData("( >= -75, <= -65 )", "-75", "-65")]
    [InlineData("(>=-75,<=-65)", "-75", "-65")]
    [InlineData("( <= -65, >= -75 )", "-75", "-65")]
    [InlineData("( > -75, < -65 )", "-75", "-65")]
    [InlineData("( >= -75, < -65 )", "-75", "-65")]
    [InlineData("( < -65, > -75 )", "-75", "-65")]
    [InlineData("( >= -75.00, <= -65.50 )", "-75.00", "-65.50")]
    [InlineData("(>=+1.25,<=+4.50)", "+1.25", "+4.50")]
    [InlineData("(>=-.5,<=.5)", "-.5", ".5")]
    [InlineData("( > 40 )", "40", null)]
    [InlineData("( >= 40 )", "40", null)]
    [InlineData("(<30)", null, "30")]
    [InlineData("(<=30)", null, "30")]
    [InlineData("( > -10.50 )", "-10.50", null)]
    [InlineData("( <= -5.25 )", null, "-5.25")]
    public void RecognizesPrefixedLimitsWithoutChangingMeasurementText(string text, string? lower, string? upper)
    {
        var result = Parse("Measurement = -71.83 " + text, [("Measurement", "", "Value")]);

        Assert.Equal("-71.83", result.Values["Value"]);
        Assert.Equal((lower, upper), result.Limits["Value"]);
    }

    [Theory]
    [InlineData("no limits")]
    [InlineData("( >= invalid, <= -65 )")]
    [InlineData("( >= -75, >= -65 )")]
    [InlineData("( <= -75, <= -65 )")]
    [InlineData("( > 40 Mbits )")]
    public void UnrecognizedLimitsDoNotInventBoundsOrLoseTheValue(string text)
    {
        var result = Parse("Measurement = 12.0 " + text, [("Measurement", "", "Value")]);

        Assert.Equal("12.0", result.Values["Value"]);
        Assert.Empty(result.Limits);
    }

    [Fact]
    public void DuplicateKeysOnOneLineKeepTheirOwnValuesAndDifferentLimits()
    {
        var result = Parse("RSSI = -71.83 ( >= -75, <= -65 ), RSSI = -68.50 ( >= -70, <= -60 )",
            [("RSSI", "", "First"), ("RSSI", "", "Second")]);

        Assert.Equal("-71.83", result.Values["First"]);
        Assert.Equal("-68.50", result.Values["Second"]);
        Assert.Equal(("-75", "-65"), result.Limits["First"]);
        Assert.Equal(("-70", "-60"), result.Limits["Second"]);
    }

    [Theory]
    [InlineData("(>=-75,<=-65)", "(-80 <= x <= -70)", "-75", "-65", "-80", "-70")]
    [InlineData("(>40)", "(10 <= x)", "40", null, "10", null)]
    [InlineData("(-80 <= x <= -70)", "(>=-75,<=-65)", "-80", "-70", "-75", "-65")]
    public void MixedFormatsOnOneLineStayAttachedToTheCorrectMeasurement(string firstLimit, string secondLimit,
        string? firstLower, string? firstUpper, string? secondLower, string? secondUpper)
    {
        var result = Parse($"RSSI = -71.83 {firstLimit}, RSSI = -68.50 {secondLimit}",
            [("RSSI", "", "First"), ("RSSI", "", "Second")]);

        Assert.Equal((firstLower, firstUpper), result.Limits["First"]);
        Assert.Equal((secondLower, secondUpper), result.Limits["Second"]);
    }

    [Fact]
    public void LegacyFormatPrecedenceIsUnchangedWhenNoNewFormatPrecedesIt()
    {
        var result = Parse("Measurement = 12.0 (10 <= x), other = 20.0 (-68 ~ -52)",
            [("Measurement", "", "Value")]);

        Assert.Equal(("-68", "-52"), result.Limits["Value"]);
    }

    [Fact]
    public void AlternateKeysStillExtractTheValueAndNewLimits()
    {
        var result = Parse("Fallback RSSI = -68.50 ( >= -75, <= -65 )",
            [("Primary RSSI", "Fallback RSSI", "RSSI")]);

        Assert.Equal("-68.50", result.Values["RSSI"]);
        Assert.Equal(("-75", "-65"), result.Limits["RSSI"]);
    }

    [Fact]
    public void WifiValuesAndLimitsExportAsNumbersWithoutChangingExcelLayout()
    {
        var keys = new List<(string Key, string AltKey, string ColumnName)>
        {
            ("signal level average value 1 =", "", "RSSI 1"),
            ("signal level average value 2 =", "", "RSSI 2"),
            ("Speed =", "", "Speed")
        };
        var result = Parse("""
            'wifi_2g_rssi_rx': '[PASS] WiFi 2G RSSI rx signal level average value 1 = -71.83 ( >= -75, <= -65 ), signal level average value 2 = -68.50 ( >= -75, <= -65 )',
            'wifi_2g_iperf_rx': '[PASS] WiFi 2G rx (receiver) Speed = 119.0 Mbits ( > 40 )'
            """, keys);
        string output = Path.Combine(_folder, "Wifi.xlsx");

        new ExcelExporter().Export(output, [result], keys);
        using var workbook = new XLWorkbook(output);
        var sheet = workbook.Worksheet("Logs");
        Assert.Equal("UPPER_SL", sheet.Cell(1, 1).GetString());
        Assert.Equal("LOWER_SL", sheet.Cell(2, 1).GetString());
        Assert.Equal(-65d, sheet.Cell(1, 3).GetDouble());
        Assert.Equal(-75d, sheet.Cell(2, 3).GetDouble());
        Assert.Equal(-65d, sheet.Cell(1, 4).GetDouble());
        Assert.Equal(-75d, sheet.Cell(2, 4).GetDouble());
        Assert.True(sheet.Cell(1, 5).IsEmpty());
        Assert.Equal(40d, sheet.Cell(2, 5).GetDouble());
        Assert.Equal(-71.83, sheet.Cell(4, 3).GetDouble());
        Assert.Equal(-68.50, sheet.Cell(4, 4).GetDouble());
        Assert.Equal(119d, sheet.Cell(4, 5).GetDouble());
        Assert.Equal("0.00", sheet.Cell(4, 4).Style.NumberFormat.Format);
    }

    private LogResult Parse(string text, List<(string Key, string AltKey, string ColumnName)> keys)
    {
        string path = Path.Combine(_folder, "PASS_AABBCCDDEEFF.log");
        File.WriteAllText(path, text);
        return new LogParser().Parse(path, keys);
    }

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
    }
}
