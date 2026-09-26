using LogEOT.Core.Models;

namespace LogEOT.Infrastructure.Services;

internal sealed class LogDownloadRules
{
    private readonly ScanOptions _options;
    private readonly HashSet<ulong> _macs = [];
    private readonly List<string> _otherPatterns = [];

    public LogDownloadRules(ScanOptions options)
    {
        _options = options;
        foreach (var mac in options.MacList ?? [])
        {
            if (mac.Length == 12 && ulong.TryParse(mac,
                    System.Globalization.NumberStyles.AllowHexSpecifier,
                    System.Globalization.CultureInfo.InvariantCulture, out ulong value))
                _macs.Add(value);
            else
                _otherPatterns.Add(mac);
        }
    }

    public bool MatchFile(string name)
    {
        if (!(name.EndsWith(".log", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))) return false;

        if (!((_options.Pass && name.StartsWith("PASS", StringComparison.OrdinalIgnoreCase))
            || (_options.Fail && name.StartsWith("FAIL", StringComparison.OrdinalIgnoreCase)))) return false;

        if (_options.MacList is not { Count: > 0 }) return true;

        // Rolling 48-bit windows preserve legacy substring matching, even when
        // a full MAC is embedded in a longer token. No substring allocations.
        if (_macs.Count > 0)
        {
            ulong window = 0;
            int digits = 0;
            foreach (char c in name)
            {
                int digit = c is >= '0' and <= '9' ? c - '0'
                    : c is >= 'a' and <= 'f' ? c - 'a' + 10
                    : c is >= 'A' and <= 'F' ? c - 'A' + 10 : -1;
                if (digit < 0) { window = 0; digits = 0; continue; }
                window = ((window << 4) | (uint)digit) & 0xFFFFFFFFFFFFUL;
                if (++digits >= 12 && _macs.Contains(window)) return true;
            }
        }

        foreach (string pattern in _otherPatterns)
            if (name.IndexOf(pattern, StringComparison.OrdinalIgnoreCase) >= 0) return true;
        return false;
    }

    public bool VisitDirectory(string parent, string name)
    {
        // Preserve the existing layout/filter contract, including non-date folders.
        int depth = parent.Split('/', StringSplitOptions.RemoveEmptyEntries).Length;
        if (depth == 3 && !string.IsNullOrEmpty(_options.MoFilter)
            && !name.Equals(_options.MoFilter, StringComparison.OrdinalIgnoreCase)) return false;
        if (depth == 5 && (_options.StartDate.HasValue || _options.EndDate.HasValue)
            && DateTime.TryParseExact(name, "yyyy-MM-dd", null,
                System.Globalization.DateTimeStyles.None, out var date))
        {
            if (_options.StartDate.HasValue && date.Date < _options.StartDate.Value.Date) return false;
            if (_options.EndDate.HasValue && date.Date > _options.EndDate.Value.Date) return false;
        }
        return true;
    }

    public string LocalDirectory(string localRoot, string remote)
    {
        if (_options.KeepRootFolder)
        {
            string relative = (Path.GetDirectoryName(remote) ?? "").Replace("\\", "/").TrimStart('/');
            return Path.Combine(localRoot, relative);
        }
        var parts = remote.Split('/', StringSplitOptions.RemoveEmptyEntries);
        string model = string.IsNullOrWhiteSpace(_options.Model)
            ? (parts.Length >= 2 ? parts[1] : "UNKNOWN_MODEL") : _options.Model;
        string station = parts.Length >= 3 ? parts[2] : "UNKNOWN";
        return Path.Combine(localRoot, model, station);
    }
}
