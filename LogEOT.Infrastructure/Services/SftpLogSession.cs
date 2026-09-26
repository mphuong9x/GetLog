using LogEOT.Core.Models;
using WinSCP;

namespace LogEOT.Infrastructure.Services;

internal record RemoteLogEntry(string Name, bool IsDirectory);

// One owner per session; neither scan nor transfer calls share a session concurrently.
internal interface ISftpLogSession : IDisposable
{
    void Open(SftpServer server, SftpConfig config);
    IReadOnlyList<RemoteLogEntry> ListDirectory(string path);
    void Download(string remote, string localDirectory);
}

internal sealed class SftpLogSession : ISftpLogSession
{
    private readonly Session _session = new() { ExecutablePath = WinScpRuntime.ExecutablePath };

    public void Open(SftpServer server, SftpConfig config) => _session.Open(new SessionOptions
    {
        Protocol = Protocol.Sftp,
        HostName = server.Host,
        PortNumber = config.Port,
        UserName = string.IsNullOrEmpty(server.UserName) ? config.UserName : server.UserName,
        Password = string.IsNullOrEmpty(server.Password) ? config.Password : server.Password,
        SshHostKeyPolicy = SshHostKeyPolicy.GiveUpSecurityAndAcceptAny
    });

    public IReadOnlyList<RemoteLogEntry> ListDirectory(string path) => _session.ListDirectory(path)
        .Files.Select(item => new RemoteLogEntry(item.Name, item.IsDirectory)).ToArray();

    public void Download(string remote, string localDirectory) => _session.GetFileToDirectory(remote, localDirectory);
    public void Dispose() => _session.Dispose();
}
