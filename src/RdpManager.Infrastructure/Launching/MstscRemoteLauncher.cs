using System.Diagnostics;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
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
    private readonly RdpSigner _signer;
    private readonly ILogger<MstscRemoteLauncher> _logger;

    public MstscRemoteLauncher(TempRdpFileWriter writer, RdpSigner signer, ILogger<MstscRemoteLauncher> logger)
    {
        _writer = writer;
        _signer = signer;
        _logger = logger;
    }

    public async Task<LaunchHandle> LaunchAsync(string rdpFileText, CancellationToken ct)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("mstsc is only available on Windows.");

        var path = await _writer.WriteAsync(rdpFileText, ct);
        WriteDiagnosticCopy(rdpFileText); // persistent, non-secret copy for troubleshooting
        PreAuthorizeRedirection(rdpFileText); // pre-checks the resource boxes
        _signer.SignInPlace(path);            // sign so the machine policy can fully trust it (no prompt)
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

    // Pre-approves local resource redirection for this host so mstsc stops showing the
    // "Allow the remote computer to access the following resources" consent every launch.
    // Writes HKCU\Software\Microsoft\Terminal Server Client\LocalDevices\<full address> = allow mask.
    private void PreAuthorizeRedirection(string rdpFileText)
    {
        if (!OperatingSystem.IsWindows()) return;
        var address = ExtractFullAddress(rdpFileText);
        if (string.IsNullOrEmpty(address)) return;
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(
                @"Software\Microsoft\Terminal Server Client\LocalDevices");
            key.SetValue(address, 0x4C, RegistryValueKind.DWord); // clipboard/drives/ports consent
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not pre-authorize redirection for the host.");
        }
    }

    private static string? ExtractFullAddress(string rdp)
    {
        foreach (var line in rdp.Split('\n'))
            if (line.StartsWith("full address:s:", StringComparison.OrdinalIgnoreCase))
                return line["full address:s:".Length..].Trim();
        return null;
    }

    // Keeps the exact .rdp of the most recent launch (no password inside) at
    // %LOCALAPPDATA%\Deskpin\last-launch.rdp so multi-monitor issues can be inspected.
    private void WriteDiagnosticCopy(string rdpFileText)
    {
        try
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Deskpin");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "last-launch.rdp"), rdpFileText);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not write diagnostic rdp copy.");
        }
    }
}
