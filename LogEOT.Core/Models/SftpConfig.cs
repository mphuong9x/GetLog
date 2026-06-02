namespace LogEOT.Core.Models;

public class SftpConfig
{
    public int Port { get; set; } = 4422;
    public string UserName { get; set; } = "user";
    public string Password { get; set; } = "ubnt";
    public string[] Roots { get; set; } = { "/UBNT_Test_Logs", "/UBNT_Test_Logs_Download" };
}
