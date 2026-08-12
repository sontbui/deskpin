using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using RdpManager.Application.Files;

namespace RdpManager.Infrastructure.Files;

/// <summary>
/// Enables the C$ admin-share access policy on a remote Windows host via the Remote Registry,
/// reusing the authenticated SMB session (so the machine's stored credential is used, never a
/// plaintext copy). Works for domain accounts / non-filtered hosts; a workgroup local admin is
/// filtered by Windows for remote registry too, so on failure we hand back the exact command to
/// run in an elevated prompt on the box.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class RemoteAdminSetup : IRemoteAdminSetup
{
    private const string PolicyKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System";
    private const string PolicyValue = "LocalAccountTokenFilterPolicy";

    private static readonly string FixCommand =
        "reg add \"HKLM\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Policies\\System\" " +
        "/v LocalAccountTokenFilterPolicy /t REG_DWORD /d 1 /f";

    private readonly IRemoteShareAuthenticator _shareAuth;
    private readonly ILogger<RemoteAdminSetup> _logger;

    public RemoteAdminSetup(IRemoteShareAuthenticator shareAuth, ILogger<RemoteAdminSetup> logger)
    {
        _shareAuth = shareAuth;
        _logger = logger;
    }

    public async Task<AdminSetupResult> EnableAdminShareAsync(Guid machineId, string host, CancellationToken ct)
    {
        if (!OperatingSystem.IsWindows())
            return new AdminSetupResult(false, false, "This only applies to Windows remotes.", FixCommand);

        host = host.Trim();

        // Reuse the machine's stored credential for the SMB/registry session.
        await _shareAuth.EnsureAsync(machineId, host, ct);

        try
        {
            return await Task.Run(() =>
            {
                using var baseKey = RegistryKey.OpenRemoteBaseKey(RegistryHive.LocalMachine, host, RegistryView.Registry64);
                using var key = baseKey.CreateSubKey(PolicyKey, writable: true)
                    ?? throw new IOException("The policy key is not available on the remote.");

                if (key.GetValue(PolicyValue) is int current && current == 1)
                    return new AdminSetupResult(true, true, $"File access is already enabled on {host}.", FixCommand);

                key.SetValue(PolicyValue, 1, RegistryValueKind.DWord);

                // Drop the cached (filtered) sessions so the next browse re-authenticates with a
                // full admin token and C$ opens.
                _ = WNetCancelConnection2W($@"\\{host}\IPC$", 0, true);
                _ = WNetCancelConnection2W($@"\\{host}\C$", 0, true);

                _logger.LogInformation("Enabled admin-share file access on {Host}", host);
                return new AdminSetupResult(true, false,
                    $"File access enabled on {host}. Browsing its drives should work now.", FixCommand);
            }, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Remote admin-share setup on {Host} failed", host);
            return new AdminSetupResult(false, false,
                $"Couldn't set it up remotely ({ex.Message.TrimEnd('.')}). " +
                $"This host is likely a workgroup local admin, which Windows blocks from remote setup. " +
                $"RDP into {host} and run this once in an elevated PowerShell/CMD:\n\n{FixCommand}",
                FixCommand);
        }
    }

    [DllImport("mpr.dll", CharSet = CharSet.Unicode)]
    private static extern int WNetCancelConnection2W(string name, int flags, [MarshalAs(UnmanagedType.Bool)] bool force);
}
