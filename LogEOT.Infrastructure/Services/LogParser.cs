using LogEOT.Core.Models;

using System.Text.RegularExpressions;

namespace LogEOT.Infrastructure.Services;

public class LogParser
{
    private static readonly Regex FreqRegex = new(@"FREQ:\s*(\d+)\s*Hz", RegexOptions.Compiled);
    private static readonly Regex MagnitudeRegex = new(@"MAGNITUDE:\s*([-\d.]+)\s*\(\s*([-\d.]+)\s*<\s*x\s*<\s*([-\d.]+)\s*\)", RegexOptions.Compiled);
    private static readonly Regex MacRegex = new(@"MAC address\s*:\s*([A-F0-9]{12})", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex GroupNameRegex = new(@"Run \[.*?\]\s*(.*?)\s*test\.\.\.", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public LogResult Parse(string filePath, List<(string Key, string AltKey, string ColumnName)> keys)
    {
        var result = new LogResult();

        foreach (var key in keys)
        {
            result.Values[key.ColumnName] = null;
        }

        // Build a list of all search patterns: each maps to a ColumnName
        // For keys with AltKey, we create two entries pointing to the same column
        var searchEntries = new List<(string SearchKey, string ColumnName)>();
        foreach (var key in keys)
        {
            searchEntries.Add((key.Key, key.ColumnName));
            if (!string.IsNullOrWhiteSpace(key.AltKey))
            {
                searchEntries.Add((key.AltKey, key.ColumnName));
            }
        }

        var groupedEntries = searchEntries
            .GroupBy(e => e.SearchKey)
            .ToDictionary(g => g.Key, g => g.ToList());

        var occurrences = new Dictionary<string, int>();
        // Track how many values already filled per column (for multi-occurrence keys)
        var columnFilled = new Dictionary<string, int>();

        foreach (var line in File.ReadLines(filePath))
        {
            if (string.IsNullOrEmpty(result.MAC))
            {
                var macMatch = MacRegex.Match(line);
                if (macMatch.Success)
                {
                    result.MAC = macMatch.Groups[1].Value;
                }
            }

            foreach (var kvp in groupedEntries)
            {
                string searchKey = kvp.Key;
                int startIndex = 0;
                
                while ((startIndex = line.IndexOf(searchKey, startIndex, StringComparison.OrdinalIgnoreCase)) >= 0)
                {
                    int count = occurrences.GetValueOrDefault(searchKey, 0);
                    if (count >= kvp.Value.Count)
                        break;
                        
                    string partAfterKey = line.Substring(startIndex + searchKey.Length);
                    var value = ExtractValue(partAfterKey);
                    
                    var entry = kvp.Value[count];

                    // Only set if column hasn't been filled yet (first match wins)
                    if (result.Values[entry.ColumnName] == null)
                    {
                        result.Values[entry.ColumnName] = value;
                    }
                    
                    occurrences[searchKey] = count + 1;
                    
                    startIndex += searchKey.Length;
                }
            }
        }

        return result;
    }

    public LogResult ParseAudio(string filePath)
    {
        var result = new LogResult { AudioMetricGroups = new List<AudioMetricGroup>() };
        var currentGroup = new List<AudioMetric>();
        string currentGroupName = "Unknown";
        string? currentFreq = null;

        foreach (var line in File.ReadLines(filePath))
        {
            if (line.Contains("Run [SL-AUD-ETH] HF Mic seal Test", StringComparison.OrdinalIgnoreCase))
                break;

            var groupMatch = GroupNameRegex.Match(line);
            if (groupMatch.Success)
            {
                currentGroupName = groupMatch.Groups[1].Value.Trim();
            }

            if (string.IsNullOrEmpty(result.MAC))
            {
                var macMatch = MacRegex.Match(line);
                if (macMatch.Success)
                {
                    result.MAC = macMatch.Groups[1].Value;
                }
            }

            var freqMatch = FreqRegex.Match(line);
            if (freqMatch.Success)
            {
                currentFreq = freqMatch.Groups[1].Value + " Hz";
                continue;
            }

            if (currentFreq != null)
            {
                var magMatch = MagnitudeRegex.Match(line);
                if (magMatch.Success)
                {
                    currentGroup.Add(new AudioMetric
                    {
                        Frequency = currentFreq,
                        Magnitude = magMatch.Groups[1].Value,
                        Lower = magMatch.Groups[2].Value,
                        Upper = magMatch.Groups[3].Value
                    });
                    currentFreq = null; 
                }
            }

            if (line.Contains("Test Result: PASS", StringComparison.OrdinalIgnoreCase))
            {
                if (currentGroup.Count > 0)
                {
                    result.AudioMetricGroups.Add(new AudioMetricGroup 
                    {
                        GroupName = currentGroupName,
                        Metrics = currentGroup
                    });
                    currentGroup = new List<AudioMetric>();
                }
                currentFreq = null;
            }
        }

        if (currentGroup.Count > 0)
        {
            result.AudioMetricGroups.Add(new AudioMetricGroup 
            {
                GroupName = currentGroupName,
                Metrics = currentGroup
            });
        }

        return result;
    }

    private string? ExtractValue(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "";

        var value = text.TrimStart(' ', '=', ':');

        var extracted = value.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
        
        return extracted.Trim('"');
    }
}