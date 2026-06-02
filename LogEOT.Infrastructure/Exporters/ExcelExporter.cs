using ClosedXML.Excel;
using LogEOT.Core.Models;

namespace LogEOT.Infrastructure.Exporters;

public class ExcelExporter
{
    public void Export(string filePath, List<LogResult> results, List<(string Key, string AltKey, string ColumnName)> keys)
    {
        using var workbook = new XLWorkbook();

        var sheet = workbook.Worksheets.Add("Logs");

        WriteSheet(sheet, results, keys);

        workbook.SaveAs(filePath);
    }

    public void ExportAudio(string filePath, List<LogResult> results)
    {
        using var workbook = new XLWorkbook();

        int maxGroups = results.Count > 0 ? results.Max(r => r.AudioMetricGroups?.Count ?? 0) : 0;
        
        if (maxGroups == 0)
        {
            workbook.Worksheets.Add("Audio Logs");
        }

        for (int groupIdx = 0; groupIdx < maxGroups; groupIdx++)
        {
            string sheetName = "Audio Log " + (groupIdx + 1);
            var firstGroupName = results
                .Where(r => r.AudioMetricGroups != null && r.AudioMetricGroups.Count > groupIdx)
                .Select(r => r.AudioMetricGroups![groupIdx].GroupName)
                .FirstOrDefault(n => !string.IsNullOrEmpty(n) && n != "Unknown");

            if (!string.IsNullOrEmpty(firstGroupName))
            {
                sheetName = firstGroupName.Replace("[SL-AUD-ETH]", "").Trim();
                string invalidChars = new string(Path.GetInvalidFileNameChars()) + new string(Path.GetInvalidPathChars()) + "[]*:?/";
                foreach (char c in invalidChars)
                {
                    sheetName = sheetName.Replace(c.ToString(), "");
                }
                if (sheetName.Length > 31) sheetName = sheetName.Substring(0, 31);
            }

            var sheet = workbook.Worksheets.Add(sheetName);

            var frequencies = results
                .Where(r => r.AudioMetricGroups != null && r.AudioMetricGroups.Count > groupIdx)
                .SelectMany(r => r.AudioMetricGroups![groupIdx].Metrics)
                .Select(m => m.Frequency)
                .Distinct()
                .OrderByDescending(f => {
                    var match = System.Text.RegularExpressions.Regex.Match(f, @"(\d+)");
                    return match.Success ? int.Parse(match.Groups[1].Value) : 0;
                })
                .ToList();

            if (frequencies.Count == 0) continue;

            for (int i = 0; i < frequencies.Count; i++)
            {
                int col = 2 + i; 
                sheet.Cell(1, col).Value = frequencies[i];
                
                var firstMetric = results
                    .Where(r => r.AudioMetricGroups != null && r.AudioMetricGroups.Count > groupIdx)
                    .SelectMany(r => r.AudioMetricGroups![groupIdx].Metrics)
                    .FirstOrDefault(m => m.Frequency == frequencies[i]);

                if (firstMetric != null)
                {
                    sheet.Cell(2, col).Value = firstMetric.Upper;
                    sheet.Cell(3, col).Value = firstMetric.Lower;
                }
            }

            sheet.Cell(1, 1).Value = "MAC / FREQ";
            sheet.Cell(2, 1).Value = "Upper";
            sheet.Cell(3, 1).Value = "Lower";

            int rowIdx = 4;
            foreach (var result in results)
            {
                sheet.Cell(rowIdx, 1).Value = result.MAC;

                for (int i = 0; i < frequencies.Count; i++)
                {
                    int col = 2 + i;
                    var group = result.AudioMetricGroups != null && result.AudioMetricGroups.Count > groupIdx ? result.AudioMetricGroups[groupIdx] : null;
                    var metric = group?.Metrics.FirstOrDefault(m => m.Frequency == frequencies[i]);
                    if (metric != null)
                    {
                        sheet.Cell(rowIdx, col).Value = metric.Magnitude;
                    }
                }
                rowIdx++;
            }

            sheet.Rows(1, 3).Style.Font.SetBold(true);

            sheet.Column(1).Style.Font.SetBold(true);

            sheet.Columns().AdjustToContents();
        }

        workbook.SaveAs(filePath);
    }

    private void WriteSheet(IXLWorksheet sheet,
        List<LogResult> data,
        List<(string Key, string AltKey, string ColumnName)> keys)
    {
        int col = 1;

        sheet.Cell(1, col++).Value = "MAC";

        foreach (var key in keys)
        {
            sheet.Cell(1, col++).Value = key.ColumnName;
        }

        int row = 2;

        foreach (var item in data)
        {
            col = 1;

            sheet.Cell(row, col++).Value = item.MAC;

            foreach (var key in keys)
            {
                if (item.Values.TryGetValue(key.ColumnName, out var value))
                    sheet.Cell(row, col++).Value = value;
                else
                    sheet.Cell(row, col++).Value = "";
            }

            row++;
        }

        sheet.Columns().AdjustToContents();
    }
}