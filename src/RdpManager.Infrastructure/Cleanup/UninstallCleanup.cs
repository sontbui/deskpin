using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using RdpManager.Application.Abstractions;
using RdpManager.Infrastructure.Launching;

namespace RdpManager.Infrastructure.Cleanup;

/// <summary>
/// Removes every trace the app leaves on the machine, for a clean uninstall:
/// the Deskpin signing cert, Credential Manager entries, LocalDevices registry values,
/// and the local data folders (DB + DPAPI secrets + temp + logs).
/// </summary>
public sealed class UninstallCleanup : IUninstallCleanup
{
    private const string LocalDevicesKey = @"Software\Microsoft\Terminal Server Client\LocalDevices";

    private readonly IMachineRepository _machines;
    private readonly ICredentialInjector _credentials;
    private readonly RdpSigner _signer;
    private readonly ILogger<UninstallCleanup> _logger;

    public UninstallCleanup(
        IMachineRepository machines, ICredentialInjector credentials,
        RdpSigner signer, ILogger<UninstallCleanup> logger)
    {
        _machines = machines;
        _credentials = credentials;
        _signer = signer;
        _logger = logger;
    }

    public async Task RunAsync(CancellationToken ct)
    {
        // 1. Read hosts BEFORE deleting the DB so we can clean their per-host traces.
        var hosts = Array.Empty<string>();
        try
        {
            var list = await _machines.ListAsync(ct);
            hosts = list.Select(m => m.Host.Host).Distinct().ToArray();
        }
        catch (Exception ex) { _logger.LogDebug(ex, "Cleanup: could not read machines."); }

        // 2. Remove Credential Manager entries + LocalDevices consent for each host.
        foreach (var host in hosts)
        {
            try { await _credentials.RemoveAsync(host, ct); } catch { /* ignore */ }
        }
        RemoveLocalDevices(hosts);

        // 3. Remove the signing certificate.
        _signer.RemoveCertificate();

        // 4. Release SQLite file handles, then delete the data folders.
        try { SqliteConnection.ClearAllPools(); } catch { /* ignore */ }
        DeleteDataFolder("Deskpin");
        DeleteDataFolder("RdpManager");
    }

    private void RemoveLocalDevices(string[] hosts)
    {
        if (!OperatingSystem.IsWindows()) return;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(LocalDevicesKey, writable: true);
            if (key is null) return;
            foreach (var host in hosts)
            {
                try { key.DeleteValue(host, throwOnMissingValue: false); }
                catch { /* ignore */ }
            }
        }
        catch (Exception ex) { _logger.LogDebug(ex, "Cleanup: LocalDevices removal failed."); }
    }

    private void DeleteDataFolder(string name)
    {
        try
        {
            var path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), name);
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        }
        catch (Exception ex) { _logger.LogDebug(ex, "Cleanup: could not delete data folder {Name}.", name); }
    }
}
