using RdpManager.Application.Abstractions;
using RdpManager.Application.Rdp;
using RdpManager.Domain.Enums;

namespace RdpManager.Infrastructure.Files;

/// <summary>
/// Persists each machine's drive-redirection choice and keeps the machine's
/// <see cref="RedirectionFlags.Drives"/> flag in sync, so the <c>drivestoredirect:s:</c> field the
/// profile builder emits always matches what the redirection dialog shows. Turning redirection off
/// here is what disables the Files console for that machine.
/// </summary>
public sealed class DriveRedirectionSettingsStore : IDriveRedirectionSettings
{
    private const string KeyPrefix = "files.drivestoredirect.";

    private readonly ISettingsStore _settings;
    private readonly IMachineRepository _machines;

    public DriveRedirectionSettingsStore(ISettingsStore settings, IMachineRepository machines)
    {
        _settings = settings;
        _machines = machines;
    }

    public async Task<DriveRedirection> GetAsync(Guid machineId, CancellationToken ct)
    {
        var raw = await _settings.GetAsync(KeyPrefix + machineId, ct);
        if (raw is not null) return DriveRedirection.Parse(raw);

        // No explicit choice yet — fall back to the machine's coarse Drives flag.
        var machine = await _machines.GetAsync(machineId, ct);
        return machine is not null && machine.Redirection.HasFlag(RedirectionFlags.Drives)
            ? DriveRedirection.All
            : DriveRedirection.None;
    }

    public async Task SetAsync(Guid machineId, DriveRedirection value, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(value);
        await _settings.SetAsync(KeyPrefix + machineId, value.ToRdpValue(), ct);

        var machine = await _machines.GetAsync(machineId, ct);
        if (machine is null) return;

        var flags = value.IsEnabled
            ? machine.Redirection | RedirectionFlags.Drives
            : machine.Redirection & ~RedirectionFlags.Drives;
        if (flags != machine.Redirection)
        {
            machine.SetRedirection(flags);
            await _machines.UpdateAsync(machine, ct);
        }
    }
}
