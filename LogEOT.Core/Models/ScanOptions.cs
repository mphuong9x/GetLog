namespace LogEOT.Core.Models;

public class ScanOptions
{
    public string Model { get; set; } = "";
    public string? MoFilter { get; set; }
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public bool Pass { get; set; }
    public bool Fail { get; set; }
    public bool KeepRootFolder { get; set; }
    public HashSet<string>? MacList { get; set; }
}
