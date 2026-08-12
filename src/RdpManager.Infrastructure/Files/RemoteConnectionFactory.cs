using RdpManager.Application.Abstractions;
using RdpManager.Application.Files;
using RdpManager.Domain.Enums;

namespace RdpManager.Infrastructure.Files;

/// <summary>
/// Builds a machine's SFTP <see cref="RemoteConnection"/> and reveals its DPAPI-stored password
/// (only transiently — the caller disposes the SecureString). No plaintext is persisted.
/// </summary>
public sealed class RemoteConnectionFactory : IRemoteConnectionFactory
{
    private readonly IMachineRepository _machines;
    private readonly ICredentialStore _credentials;

    public RemoteConnectionFactory(IMachineRepository machines, ICredentialStore credentials)
    {
        _machines = machines;
        _credentials = credentials;
    }

    public async Task<RemoteConnectionInfo?> CreateAsync(Guid machineId, CancellationToken ct)
    {
        var machine = await _machines.GetAsync(machineId, ct);
        if (machine is null || string.IsNullOrWhiteSpace(machine.Username)) return null;

        var connection = new RemoteConnection(machine.Host.Host, machine.SshPort, machine.Username);

        System.Security.SecureString? secret = null;
        if (machine.Credential.Kind == CredentialStoreKind.Dpapi && machine.Credential.Reference is not null)
            secret = await _credentials.RevealAsync(machine.Credential, ct);

        return new RemoteConnectionInfo(connection, secret);
    }
}
