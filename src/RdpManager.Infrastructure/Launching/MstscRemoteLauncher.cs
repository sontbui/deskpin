using System.Diagnostics;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using RdpManager.Application.Abstractions;
using RdpManager.Infrastructure.Rdp;

namespace RdpManager.Infrastructure.Launching;

/// <summary>
/// Writes the temp .rdp, starts mstsc against it, and guarantees the temp file is removed even if
/// the launch fails. mstsc reads the file once at startup, so we delete after a short grace period.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class MstscRemoteLauncher : IRemoteLauncher
{
    private static readonly TimeSpan ReadGrace = TimeSpan.FromSeconds(3);

    private readonly TempRdpFileWriter _writer;
    private readonly ILogger<MstscRemoteLauncher> _logger;

    public MstscRemoteLauncher(TempRdpFileWriter writer, ILogger<MstscRemoteLauncher> logger)
    {
        _writer = writer;
        _logger = logger;
    }

    public async Task<LaunchHandle> LaunchAsync(string rdpFileText, CancellationToken ct)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("mstsc is only available on Windows.");

        var path = await _writer.WriteAsync(rdpFileText, ct);
        Process? process = null;
        try
        {
            process = Process.Start(new ProcessStartInfo
            {
                FileName = "mstsc.exe",
                Arguments = $"\"{path}\"",
                UseShellExecute = false,
            }) ?? throw new InvalidOperationException("Failed to start mstsc.");

            var pid = process.Id;
            _logger.LogInformation("Launched mstsc pid {Pid}", pid);

            // Let mstsc read the file, then remove it. Fire-and-forget so we don't block the UI.
            _ = ScheduleCleanupAsync(path);
            return new LaunchHandle(pid);
        }
        catch
        {
            _writer.DeleteQuietly(path); // never leave a temp file if the launch failed
            throw;
        }
        finally
        {
            process?.Dispose();
        }
    }

    private async Task ScheduleCleanupAsync(string path)
    {
        try { await Task.Delay(ReadGrace); }
        finally { _writer.DeleteQuietly(path); }
    }
}
