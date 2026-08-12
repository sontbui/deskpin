namespace RdpManager.Application.Files;

/// <summary>Outcome of trying to enable admin-share file access on a remote host.</summary>
public sealed record AdminSetupResult(bool Success, bool AlreadyEnabled, string Message, string Command);

/// <summary>
/// Turns on the policy that lets Deskpin browse a Windows host's <c>C$</c> admin share
/// (<c>LocalAccountTokenFilterPolicy=1</c>). Best-effort remotely: it works for domain accounts
/// and hosts with Remote Registry reachable, but a workgroup <b>local admin</b> is exactly the
/// case Windows filters — the same restriction that blocks C$ also blocks remote setup — so there
/// the result carries the one-line command to run in an elevated prompt on the box instead.
/// </summary>
public interface IRemoteAdminSetup
{
    Task<AdminSetupResult> EnableAdminShareAsync(Guid machineId, string host, CancellationToken ct);
}
