namespace LogEOT.Core.Models;

public class SftpServer
{
    public string Host { get; set; } = "";

    // Null/empty => fall back to the default credentials in SftpConfig.
    public string? UserName { get; set; }
    public string? Password { get; set; }
}
