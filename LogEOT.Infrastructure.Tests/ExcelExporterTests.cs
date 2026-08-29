using ClosedXML.Excel;
using LogEOT.Core.Models;
using LogEOT.Infrastructure.Exporters;
using Xunit;

namespace LogEOT.Infrastructure.Tests;

public class ExcelExporterTests
{
    [Fact]
    public void Export_WritesResultColumnAndGraysMissingFailValue()
    {
        var output = Path.Combine(Path.GetTempPath(), $"LogEOT-{Guid.NewGuid():N}.xlsx");
        var keys = new List<(string Key, string AltKey, string ColumnName)>
        {
            ("Voltage", "", "Voltage")
        };
        var results = new List<LogResult>
        {
            new()
            {
                MAC = "112233445566",
                Result = "FAIL",
                Values = new Dictionary<string, string?> { ["Voltage"] = null }
            }
        };

        try
        {
            new ExcelExporter().Export(output, results, keys);

            using var workbook = new XLWorkbook(output);
            var sheet = workbook.Worksheet("Logs");

            Assert.Equal("Result", sheet.Cell(3, 2).GetString());
            Assert.Equal("Voltage", sheet.Cell(3, 3).GetString());
            Assert.Equal("FAIL", sheet.Cell(4, 2).GetString());
            Assert.True(sheet.Cell(4, 3).IsEmpty());
            Assert.Equal(XLColor.FromHtml("#F2F2F2"), sheet.Cell(4, 3).Style.Fill.BackgroundColor);
        }
        finally
        {
            if (File.Exists(output))
                File.Delete(output);
        }
    }
}
