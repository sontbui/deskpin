using System.Security;
using RdpManager.Application.Display;
using RdpManager.Domain.ValueObjects;

namespace RdpManager.Application.Abstractions;

/// <summary>Enumerates the live monitor topology (Infrastructure: CCD / Win32 P/Invoke).</summary>
public interface IDisplayTopologyProvider
{
    Task<DisplayTopology> GetCurrentAsync(CancellationToken ct);
}

/// <summary>Protects/reveals a secret at rest (Infrastructure: DPAPI). Never exposes plaintext to logs.</summary>
public interface ICredentialStore
{
    Task<CredentialRef> ProtectAsync(SecureString secret, CancellationToken ct);
    Task<SecureString> RevealAsync(CredentialRef reference, CancellationToken ct);
    Task RemoveAsync(CredentialRef reference, CancellationToken ct);
}

/// <summary>Delivers a secret to mstsc at launch via Credential Manager (TERMSRV/&lt;host&gt;).</summary>
public interface ICredentialInjector
{
    Task InjectAsync(string host, string username, SecureString secret, CancellationToken ct);
    Task RemoveAsync(string host, CancellationToken ct);
}

public sealed record ReachabilityResult(bool IsReachable, int? RoundtripMs, string? Detail);

public interface IReachabilityProbe
{
    Task<ReachabilityResult> CheckAsync(HostAddress host, CancellationToken ct);
}

public sealed record LaunchHandle(int ProcessId);

/// <summary>Writes the temp .rdp, starts mstsc, and guarantees cleanup of the temp file.</summary>
public interface IRemoteLauncher
{
    Task<LaunchHandle> LaunchAsync(string rdpFileText, CancellationToken ct);
}

/// <summary>Removes everything the app created (cert, DB, secrets, registry, temp) at uninstall.</summary>
public interface IUninstallCleanup
{
    Task RunAsync(CancellationToken ct);
}
