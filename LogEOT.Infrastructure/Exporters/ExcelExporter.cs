using ClosedXML.Excel;
using LogEOT.Core.Models;

namespace LogEOT.Infrastructure.Exporters;

public class ExcelExporter
{
    // Same palette as the UMRUTI00T01Tool sheets.
    private const string HeaderFill = "#DDEBF7";
    private const string LimitFill = "#FFF2CC";
    private const string BadFill = "#FFC7CE";
    private const string MissingFill = "#F2F2F2";

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
                    SetNumeric(sheet.Cell(2, col), firstMetric.Upper);
                    SetNumeric(sheet.Cell(3, col), firstMetric.Lower);
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
                        SetNumeric(sheet.Cell(rowIdx, col), metric.Magnitude);
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

    // Write as a number so Cpk formulas (AVERAGE/STDEV) work; keep raw text if it is not numeric.
    // Returns the number written, or null when the value was not numeric.
    private static double? SetNumeric(IXLCell cell, string raw)
    {
        if (double.TryParse(raw, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var d))
        {
            cell.Value = d;
            cell.Style.NumberFormat.Format = FormatOf(raw);
            return d;
        }

        cell.Value = raw;
        return null;
    }

    // Show as many decimals as the log printed ("-65.0" stays "-65.0", "21" stays "21")
    // instead of rounding every cell to one fixed format.
    private static string FormatOf(string raw)
    {
        int dot = raw.IndexOf('.');
        if (dot < 0) return "0";

        int decimals = raw.Length - dot - 1;

        // Excel keeps 15 significant digits; asking for more only prints trailing zeros.
        int wholeDigits = raw.Substring(0, dot).TrimStart('-', '+', '0').Length;
        decimals = Math.Min(decimals, Math.Max(0, 15 - wholeDigits));

        return decimals <= 0 ? "0" : "0." + new string('0', decimals);
    }

    // Layout of the UMRUTI00T01Tool sheets: limit rows on top, one header row,
    // then one row per unit — values numeric, out-of-limit values in red.
    //   1 USL | 2 LSL | 3 header (MAC \ Item) | 4.. data
    private void WriteSheet(IXLWorksheet sheet,
        List<LogResult> data,
        List<(string Key, string AltKey, string ColumnName)> keys)
    {
        const int uslRow = 1, lslRow = 2, headerRow = 3, firstDataRow = 4;

        sheet.Cell(uslRow, 1).Value = "UPPER_SL";
        sheet.Cell(lslRow, 1).Value = "LOWER_SL";
        sheet.Cell(headerRow, 1).Value = "MAC \\ Item";

        var limits = new (double? Lower, double? Upper)[keys.Count];

        for (int i = 0; i < keys.Count; i++)
        {
            int col = 2 + i;
            string columnName = keys[i].ColumnName;

            var raw = data
                .Select(d => d.Limits.TryGetValue(columnName, out var l) ? l : default)
                .FirstOrDefault(l => l.Lower != null || l.Upper != null);

            limits[i] = (
                raw.Lower == null ? null : SetNumeric(sheet.Cell(lslRow, col), raw.Lower),
                raw.Upper == null ? null : SetNumeric(sheet.Cell(uslRow, col), raw.Upper));

            sheet.Cell(headerRow, col).Value = columnName;
        }

        for (int r = 0; r < data.Count; r++)
        {
            int row = firstDataRow + r;

            sheet.Cell(row, 1).Value = data[r].MAC;
            sheet.Cell(row, 1).Style.Font.FontName = "Consolas";

            for (int i = 0; i < keys.Count; i++)
            {
                var cell = sheet.Cell(row, 2 + i);
                data[r].Values.TryGetValue(keys[i].ColumnName, out var raw);

                if (string.IsNullOrWhiteSpace(raw))
                {
                    cell.Style.Fill.BackgroundColor = XLColor.FromHtml(MissingFill);
                    continue;
                }

                var value = SetNumeric(cell, raw);
                if (value == null) continue;

                var (lower, upper) = limits[i];
                if ((lower.HasValue && value < lower) || (upper.HasValue && value > upper))
                    cell.Style.Fill.BackgroundColor = XLColor.FromHtml(BadFill);
            }
        }

        int lastCol = 1 + keys.Count;
        int lastRow = firstDataRow + data.Count - 1;

        var limitRows = sheet.Range(uslRow, 1, lslRow, lastCol);
        limitRows.Style.Fill.BackgroundColor = XLColor.FromHtml(LimitFill);
        sheet.Range(uslRow, 1, lslRow, 1).Style.Font.Bold = true;

        var header = sheet.Range(headerRow, 1, headerRow, lastCol);
        header.Style.Fill.BackgroundColor = XLColor.FromHtml(HeaderFill);
        header.Style.Font.Bold = true;
        header.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        header.Style.Alignment.WrapText = true;

        sheet.SheetView.Freeze(headerRow, 1);
        sheet.Range(headerRow, 1, lastRow, 1).SetAutoFilter();

        sheet.Column(1).Width = 22;
        for (int i = 0; i < keys.Count; i++) sheet.Column(2 + i).Width = 13;
    }
}