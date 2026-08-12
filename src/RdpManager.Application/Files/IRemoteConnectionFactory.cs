using System.Security;

namespace RdpManager.Application.Files;

/// <summary>A machine's SFTP connection details plus its transiently-revealed secret.</summary>
public sealed record RemoteConnectionInfo(RemoteConnection Connection, SecureString? Secret);

/// <summary>
/// Builds the SFTP connection for a machine and reveals its stored secret (DPAPI) only long
/// enough to hand to the transport. Infrastructure owns the credential store; the ViewModel just
/// asks for a ready-to-use connection.
/// </summary>
public interface IRemoteConnectionFactory
{
    Task<RemoteConnectionInfo?> CreateAsync(Guid machineId, CancellationToken ct);
}
