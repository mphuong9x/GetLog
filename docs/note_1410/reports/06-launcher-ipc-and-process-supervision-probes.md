# Phụ lục review 06 — Mã tái hiện lỗi

Bốn fixture dưới đây đã chạy thành công để xác nhận **hành vi lỗi hiện tại**, không phải để chứng minh hệ thống đã được sửa. Xem [báo cáo chính](06-launcher-ipc-and-process-supervision-report.md) về kết quả, giới hạn môi trường và hướng khắc phục.

Để chạy lại trên Windows, lưu khối C# thành `MProjectAgent.Tests/LauncherReviewProbes.cs`, chạy `dotnet test MProjectAgent.Tests/MProjectAgent.Tests.csproj --no-restore --filter FullyQualifiedName~LauncherReviewProbes --verbosity minimal`, rồi bỏ file tạm khỏi project khi hoàn tất.

```csharp
using System.Diagnostics;
using System.Reflection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MProjectAgent.Configuration;
using MProjectAgent.Models;
using MProjectAgent.Services;
using MProjectAgent.Storage;

namespace MProjectAgent.Tests;

// Review evidence: these assertions confirm CURRENT defective behavior, not desired behavior.
// To rerun, temporarily include this file in MProjectAgent.Tests, then filter LauncherReviewProbes.
public sealed class LauncherReviewProbes
{
    private static T Field<T>(object instance, string name) =>
        (T)instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(instance)!;

    [Fact]
    public async Task CancelDuringWatchAttach_LeavesLivePidWithoutWatcherOrDurablePid()
    {
        var dir = NewDirectory();
        var store = Store(dir);
        var supervisor = Supervisor(dir, store);
        var wrapper = Path.Combine(dir, "wait.cmd");
        await File.WriteAllTextAsync(wrapper, "@echo off\r\nping 127.0.0.1 -n 20 >nul\r\n");
        try
        {
            using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(1));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => supervisor.LaunchAndSuperviseAsync(
                new LaunchRequest { PackageId = Guid.NewGuid(), ExePath = wrapper,
                    WatchProcessPath = Path.Combine(dir, "absent-watch.exe") }, cancel.Token));
            var pid = supervisor.GetSnapshot().Pid;
            Assert.NotNull(pid);
            using var live = Process.GetProcessById(pid.Value);
            Assert.False(live.HasExited);
            Assert.True(Field<Task>(supervisor, "_watchTask").IsCompleted);
            Assert.Null((await store.LoadAsync(CancellationToken.None))!.Pid);
        }
        finally
        {
            await StopFixture(supervisor);
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public async Task HttpTimeout_EndsProbeAfterOnlyOneAttempt()
    {
        var dir = NewDirectory();
        using var handler = new BlackHoleHandler();
        using var http = new HttpClient(handler);
        var supervisor = Supervisor(dir, Store(dir), new Factory(http));
        using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(6));
        using var process = Process.GetCurrentProcess();
        try
        {
            var probe = (Task)typeof(ProcessSupervisor).GetMethod("ProbeAsync",
                BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(supervisor,
                new object[] { process, "http://fixture.invalid/health", cancel.Token })!;
            await probe.WaitAsync(TimeSpan.FromSeconds(4));
            Assert.False(cancel.IsCancellationRequested);
            Assert.Equal(1, handler.Calls);
        }
        finally
        {
            cancel.Cancel();
            await supervisor.DisposeAsync();
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public async Task WatchAttach_SelectsExistingUnrelatedProcessBeforeNewLaunch()
    {
        var dir = NewDirectory();
        var watchPath = Path.Combine(dir, "review-watch-" + Guid.NewGuid().ToString("N") + ".exe");
        File.Copy(Path.Combine(Environment.SystemDirectory, "ping.exe"), watchPath);
        using var unrelated = Process.Start(new ProcessStartInfo(watchPath, "127.0.0.1 -n 20")
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true })!;
        var wrapper = Path.Combine(dir, "empty.cmd");
        await File.WriteAllTextAsync(wrapper, "@exit /b 0\r\n");
        var supervisor = Supervisor(dir, Store(dir));
        try
        {
            await supervisor.LaunchAndSuperviseAsync(new LaunchRequest {
                PackageId = Guid.NewGuid(), ExePath = wrapper, WatchProcessPath = watchPath
            }, CancellationToken.None);
            Assert.Equal(unrelated.Id, supervisor.GetSnapshot().Pid);
            Assert.True(await supervisor.RequestStopAsync(CancellationToken.None));
            await unrelated.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(3));
        }
        finally
        {
            if (!unrelated.HasExited) { unrelated.Kill(true); await unrelated.WaitForExitAsync(); }
            await StopFixture(supervisor);
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public async Task ConcurrentLaunches_BothStartSecondRequestsEntryPoint()
    {
        var dir = NewDirectory();
        var watchPath = Path.Combine(dir, "review-watch-" + Guid.NewGuid().ToString("N") + ".exe");
        File.Copy(Path.Combine(Environment.SystemDirectory, "ping.exe"), watchPath);
        using var watchProcess = Process.Start(new ProcessStartInfo(watchPath, "127.0.0.1 -n 20")
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true })!;
        var firstPath = Path.Combine(dir, "first.cmd");
        var secondPath = Path.Combine(dir, "second.cmd");
        await File.WriteAllTextAsync(firstPath, "@echo first>>first.txt\r\n");
        await File.WriteAllTextAsync(secondPath, "@echo second>>second.txt\r\n");
        var store = Store(dir);
        var supervisor = Supervisor(dir, store);
        var gate = Field<SemaphoreSlim>(store, "_lock");
        try
        {
            await gate.WaitAsync();
            var first = supervisor.LaunchAndSuperviseAsync(new LaunchRequest {
                PackageId = Guid.NewGuid(), ExePath = firstPath, WatchProcessPath = watchPath
            }, CancellationToken.None);
            var second = supervisor.LaunchAndSuperviseAsync(new LaunchRequest {
                PackageId = Guid.NewGuid(), ExePath = secondPath, WatchProcessPath = watchPath
            }, CancellationToken.None);
            gate.Release();
            await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(5));
            await Task.Delay(500);
            Assert.False(File.Exists(Path.Combine(dir, "first.txt")));
            Assert.Equal(2, File.ReadAllLines(Path.Combine(dir, "second.txt")).Length);
        }
        finally
        {
            if (!watchProcess.HasExited) { watchProcess.Kill(true); await watchProcess.WaitForExitAsync(); }
            await StopFixture(supervisor);
            Directory.Delete(dir, true);
        }
    }

    private static string NewDirectory()
    {
        var dir = Path.Combine(Path.GetTempPath(), "launcher-review-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }
    private static RuntimeStateStore Store(string dir) => new(
        Options.Create(new AgentOptions { StateDirectory = dir }), NullLogger<RuntimeStateStore>.Instance);
    private static ProcessSupervisor Supervisor(string dir, RuntimeStateStore store, IHttpClientFactory? factory = null) => new(
        store, Options.Create(new SupervisorOptions { Enabled = true, WatchAttachTimeoutSeconds = 30,
            HealthProbeIntervalSeconds = 1, HealthProbeTimeoutSeconds = 1, HealthProbeFailureThreshold = 3,
            MaintenanceFlagPath = Path.Combine(dir, "maintenance.flag") }),
        NullLogger<ProcessSupervisor>.Instance, factory);
    private static async Task StopFixture(ProcessSupervisor supervisor)
    {
        await supervisor.RequestStopAsync(CancellationToken.None);
        var watch = Field<Task>(supervisor, "_watchTask");
        await watch.WaitAsync(TimeSpan.FromSeconds(3));
        await supervisor.DisposeAsync();
    }
    private sealed class Factory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }
    private sealed class BlackHoleHandler : HttpMessageHandler
    {
        public int Calls;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Interlocked.Increment(ref Calls);
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            throw new InvalidOperationException("Unreachable");
        }
    }
}
```
