using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using RdpManager.Application.Abstractions;
using RdpManager.Application.Files;
using RdpManager.Domain.Enums;

namespace RdpManager.Infrastructure.Files;

/// <summary>
/// Signs the local machine in to a remote host's SMB shares using the machine's stored
/// credential, so the <c>\\host\C$</c> admin-share bridge works without the user ever running
/// <c>net use</c> by hand. The secret comes out of DPAPI only for the duration of the
/// WNetAddConnection2 call (unmanaged unicode buffer, zeroed immediately after) — nothing is
/// persisted in plaintext, matching the app's credential model.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class SmbShareAuthenticator : IRemoteShareAuthenticator
{
    private const int NoError = 0;
    private const int ErrorAccessDenied = 5;
    private const int ErrorBadNetPath = 53;
    private const int ErrorBadNetName = 67;
    private const int ErrorInvalidPassword = 86;
    private const int ErrorSessionCredentialConflict = 1219;
    private const int ErrorLogonFailure = 1326;

    private readonly ICredentialStore _credentials;
    private readonly IMachineRepository _machines;
    private readonly ILogger<SmbShareAuthenticator> _logger;

    public SmbShareAuthenticator(
        ICredentialStore credentials, IMachineRepository machines, ILogger<SmbShareAuthenticator> logger)
    {
        _credentials = credentials;
        _machines = machines;
        _logger = logger;
    }

    public async Task<string?> EnsureAsync(Guid machineId, string host, CancellationToken ct)
    {
        if (!OperatingSystem.IsWindows()) return null;

        var machine = await _machines.GetAsync(machineId, ct);
        if (machine is null || string.IsNullOrWhiteSpace(machine.Username)) return null;
        if (machine.Credential.Kind != CredentialStoreKind.Dpapi || machine.Credential.Reference is null)
        {
            // No stored secret — browsing rides the current Windows session's own access.
            _logger.LogInformation("No stored credential for {Host}; SMB uses the current Windows session", host);
            return null;
        }

        try
        {
            using var secret = await _credentials.RevealAsync(machine.Credential, ct);
            var username = machine.Username;
            var share = $@"\\{host.Trim()}\IPC$"; // authenticates the whole SMB session for the host

            var result = await Task.Run(() =>
            {
                var code = Connect(share, username, secret);
                if (code == ErrorSessionCredentialConflict)
                {
                    // A stale session with other credentials exists — drop it and try once more.
                    _ = WNetCancelConnection2W(share, 0, true);
                    code = Connect(share, username, secret);
                }
                return code;
            }, ct);

            if (result is NoError or ErrorSessionCredentialConflict)
            {
                _logger.LogInformation("SMB session to {Host} established as {User}", host, username);
                return null;
            }

            _logger.LogWarning("SMB sign-in to {Host} as {User} failed (WNet error {Code})", host, username, result);
            return result switch
            {
                ErrorAccessDenied => $"SMB sign-in to {host}: access denied for {username}.",
                ErrorBadNetPath => $"{host} is unreachable over SMB — check the network and port 445.",
                ErrorBadNetName => $"{host} rejected the connection: share not found.",
                ErrorInvalidPassword or ErrorLogonFailure =>
                    $"{host} rejected the saved username/password for {username} — update it in Edit.",
                _ => $"SMB sign-in to {host} failed (error {result}).",
            };
        }
        catch (Exception ex)
        {
            // Best-effort by contract: browsing will surface its own, friendlier error.
            _logger.LogWarning(ex, "SMB sign-in to {Host} failed", host);
            return null;
        }
    }

    private static int Connect(string share, string username, System.Security.SecureString secret)
    {
        var resource = new NetResource { Type = 0 /* RESOURCETYPE_ANY */, RemoteName = share };
        var password = IntPtr.Zero;
        try
        {
            password = Marshal.SecureStringToGlobalAllocUnicode(secret);
            return WNetAddConnection2W(ref resource, password, username, 0);
        }
        finally
        {
            if (password != IntPtr.Zero) Marshal.ZeroFreeGlobalAllocUnicode(password);
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NetResource
    {
        public int Scope;
        public int Type;
        public int DisplayType;
        public int Usage;
        [MarshalAs(UnmanagedType.LPWStr)] public string? LocalName;
        [MarshalAs(UnmanagedType.LPWStr)] public string RemoteName;
        [MarshalAs(UnmanagedType.LPWStr)] public string? Comment;
        [MarshalAs(UnmanagedType.LPWStr)] public string? Provider;
    }

    [DllImport("mpr.dll", CharSet = CharSet.Unicode)]
    private static extern int WNetAddConnection2W(ref NetResource netResource, IntPtr password, string? userName, int flags);

    [DllImport("mpr.dll", CharSet = CharSet.Unicode)]
    private static extern int WNetCancelConnection2W(string name, int flags, [MarshalAs(UnmanagedType.Bool)] bool force);
}
