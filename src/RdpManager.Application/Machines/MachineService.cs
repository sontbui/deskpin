using System.Security;
using RdpManager.Application.Abstractions;
using RdpManager.Application.Common;
using RdpManager.Domain.Abstractions;
using RdpManager.Domain.Entities;
using RdpManager.Domain.Enums;
using RdpManager.Domain.ValueObjects;

namespace RdpManager.Application.Machines;

public sealed record CreateMachineRequest(
    string Name, string Host, int Port, string? Username,
    IReadOnlyList<string> Tags, string? Gateway, string? Notes,
    RedirectionFlags Redirection = RedirectionFlags.Default);

/// <summary>
/// Application service for managing machine profiles. Owns credential lifecycle coordination so a
/// deleted machine never leaves an orphaned secret behind.
/// </summary>
public sealed class MachineService
{
    private readonly IMachineRepository _repo;
    private readonly ICredentialStore _credentials;
    private readonly IClock _clock;

    public MachineService(IMachineRepository repo, ICredentialStore credentials, IClock clock)
    {
        _repo = repo; _credentials = credentials; _clock = clock;
    }

    public Task<IReadOnlyList<Machine>> ListAsync(CancellationToken ct) => _repo.ListAsync(ct);
    public Task<Machine?> GetAsync(Guid id, CancellationToken ct) => _repo.GetAsync(id, ct);

    public async Task<Result<Machine>> CreateAsync(CreateMachineRequest req, CancellationToken ct)
    {
        HostAddress host;
        try { host = HostAddress.Create(req.Host, req.Port); }
        catch (ArgumentException ex) { return Error.Validation(ex.Message); }

        Machine machine;
        try { machine = new Machine(req.Name, host, _clock.UtcNow, req.Username); }
        catch (ArgumentException ex) { return Error.Validation(ex.Message); }

        machine.SetGateway(req.Gateway);
        machine.SetNotes(req.Notes);
        machine.SetRedirection(req.Redirection);
        foreach (var tag in req.Tags.Where(t => !string.IsNullOrWhiteSpace(t)))
            machine.AddTag(new Tag(tag));

        await _repo.AddAsync(machine, ct);
        return machine;
    }

    /// <summary>Protects a plaintext secret with the store and attaches a reference to the machine.</summary>
    public async Task<Result<Machine>> SetPasswordAsync(Guid machineId, SecureString secret, CancellationToken ct)
    {
        var machine = await _repo.GetAsync(machineId, ct);
        if (machine is null) return Error.NotFound("Machine");

        // Remove any previous secret first so we don't leak stored blobs.
        if (machine.Credential.Kind == CredentialStoreKind.Dpapi)
            await _credentials.RemoveAsync(machine.Credential, ct);

        var reference = await _credentials.ProtectAsync(secret, ct);
        machine.SetCredential(reference);
        await _repo.UpdateAsync(machine, ct);
        return machine;
    }

    /// <summary>Updates a machine's basic details (name / host / username). Tags and secret unchanged.</summary>
    public async Task<Result<Machine>> UpdateDetailsAsync(Guid machineId, CreateMachineRequest req, CancellationToken ct)
    {
        var machine = await _repo.GetAsync(machineId, ct);
        if (machine is null) return Error.NotFound("Machine");

        HostAddress host;
        try { host = HostAddress.Create(req.Host, req.Port); }
        catch (ArgumentException ex) { return Error.Validation(ex.Message); }

        try { machine.Rename(req.Name); }
        catch (ArgumentException ex) { return Error.Validation(ex.Message); }

        machine.ChangeHost(host);
        machine.SetUsername(req.Username);
        machine.SetRedirection(req.Redirection);
        await _repo.UpdateAsync(machine, ct);
        return machine;
    }

    public async Task<Result<Machine>> DuplicateAsync(Guid machineId, CancellationToken ct)
    {
        var src = await _repo.GetAsync(machineId, ct);
        if (src is null) return Error.NotFound("Machine");

        // A duplicate never copies the secret — the reference is per-machine and re-entered.
        var copy = new Machine($"{src.Name}-copy", src.Host, _clock.UtcNow, src.Username);
        copy.SetGateway(src.Gateway);
        copy.SetNotes(src.Notes);
        copy.SetRedirection(src.Redirection);
        foreach (var tag in src.Tags) copy.AddTag(new Tag(tag.Name));

        await _repo.AddAsync(copy, ct);
        return copy;
    }

    public async Task<Result<bool>> DeleteAsync(Guid machineId, CancellationToken ct)
    {
        var machine = await _repo.GetAsync(machineId, ct);
        if (machine is null) return Error.NotFound("Machine");

        if (machine.Credential.Kind == CredentialStoreKind.Dpapi)
            await _credentials.RemoveAsync(machine.Credential, ct); // don't orphan the secret

        await _repo.RemoveAsync(machineId, ct);
        return true;
    }

    /// <summary>Saves the monitors the user picked as the machine's display profile.</summary>
    public async Task<Result<Machine>> ConfigureDisplayAsync(
        Guid machineId, IReadOnlyList<MonitorFingerprint> selected, CancellationToken ct)
    {
        var machine = await _repo.GetAsync(machineId, ct);
        if (machine is null) return Error.NotFound("Machine");
        if (selected.Count == 0) return Error.Validation("Select at least one monitor.");

        await _repo.SaveDisplayProfileAsync(machineId, selected, _clock.UtcNow, ct);
        return (await _repo.GetAsync(machineId, ct))!;
    }
}
