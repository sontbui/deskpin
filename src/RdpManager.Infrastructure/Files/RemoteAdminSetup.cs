using RdpManager.Application.Files;

namespace RdpManager.Infrastructure.Files;

/// <summary>
/// "Enable file access" for the SFTP model = make sure the host runs an SSH/SFTP server. Linux and
/// macOS ship one; Windows needs the built-in OpenSSH Server enabled once. We can't run that
/// remotely (chicken-and-egg — no session yet), so this returns the exact command to run on the
/// box. Kept behind <see cref="IRemoteAdminSetup"/> so the UI flow is unchanged.
/// </summary>
public sealed class RemoteAdminSetup : IRemoteAdminSetup
{
    private static readonly string OpenSshCommand =
        "Add-WindowsCapability -Online -Name OpenSSH.Server~~~~0.0.1.0; " +
        "Start-Service sshd; Set-Service -Name sshd -StartupType Automatic; " +
        "New-NetFirewallRule -Name sshd -DisplayName 'OpenSSH Server (sshd)' -Enabled True " +
        "-Direction Inbound -Protocol TCP -Action Allow -LocalPort 22";

    public Task<AdminSetupResult> EnableAdminShareAsync(Guid machineId, string host, CancellationToken ct) =>
        Task.FromResult(new AdminSetupResult(
            Success: false,
            AlreadyEnabled: false,
            Message: $"To browse files on {host} over SFTP, its SSH server must be running. " +
                     "Linux/macOS already have one. On Windows, RDP into the machine and run this once " +
                     $"in an elevated PowerShell:\n\n{OpenSshCommand}",
            Command: OpenSshCommand));
}
