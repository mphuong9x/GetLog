using System;
using System.IO;
using System.Reflection;

namespace LogEOT.Infrastructure.Services;

/// <summary>
/// Trích WinSCP.exe (nhúng trong assembly) ra thư mục temp một lần,
/// để WinSCP .NET tìm được executable khi chạy dạng single-file exe.
/// </summary>
public static class WinScpRuntime
{
    private static readonly object _lock = new();
    private static string? _executablePath;

    public static string ExecutablePath
    {
        get
        {
            if (_executablePath != null) return _executablePath;

            lock (_lock)
            {
                if (_executablePath != null) return _executablePath;

                var asm = typeof(WinScpRuntime).Assembly;
                var resourceName = asm.GetName().Name + ".Assets.WinSCP.exe";

                var dir = Path.Combine(Path.GetTempPath(), "LogEOT_WinSCP");
                Directory.CreateDirectory(dir);
                var target = Path.Combine(dir, "WinSCP.exe");

                using var stream = asm.GetManifestResourceStream(resourceName)
                    ?? throw new InvalidOperationException($"Không tìm thấy resource nhúng: {resourceName}");

                bool needWrite = true;
                if (File.Exists(target) && new FileInfo(target).Length == stream.Length)
                    needWrite = false;

                if (needWrite)
                {
                    using var fs = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None);
                    stream.CopyTo(fs);
                }

                _executablePath = target;
                return _executablePath;
            }
        }
    }
}
