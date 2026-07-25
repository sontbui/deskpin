using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security;
using Microsoft.Extensions.Logging;
using RdpManager.Application.Abstractions;
using RdpManager.Infrastructure.Interop;

namespace RdpManager.Infrastructure.Credentials;

/// <summary>
/// Delivers a secret to mstsc at launch by writing a <c>TERMSRV/&lt;host&gt;</c> generic credential
/// into Windows Credential Manager, then removing it after the session has consumed it. This is
/// why the generated .rdp never needs a password field. Persistence is SESSION so a crash can't
/// leave the secret behind past logoff; we also delete it explicitly in the launcher's finally.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class CredentialManagerInjector : ICredentialInjector
{
    private readonly ILogger<CredentialManagerInjector> _logger;

    public CredentialManagerInjector(ILogger<CredentialManagerInjector> logger) => _logger = logger;

    public Task InjectAsync(string host, string username, SecureString secret, CancellationToken ct)
    {
        EnsureWindows();
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        ArgumentNullException.ThrowIfNull(secret);

        var target = TargetFor(host);
        byte[]? blob = null;
        var handle = default(GCHandle);
        try
        {
            blob = SecureStringBytes.ToUtf16Bytes(secret);
            handle = GCHandle.Alloc(blob, GCHandleType.Pinned);

            var cred = new NativeMethods.CREDENTIAL
            {
                Type = NativeMethods.CRED_TYPE_GENERIC,
                TargetName = target,
                CredentialBlobSize = blob.Length,
                CredentialBlob = handle.AddrOfPinnedObject(),
                Persist = NativeMethods.CRED_PERSIST_SESSION,
                UserName = username,
            };

            if (!NativeMethods.CredWrite(ref cred, 0))
                throw new InvalidOperationException($"CredWrite failed for {target} (Win32 {Marshal.GetLastWin32Error()}).");
        }
        finally
        {
            if (handle.IsAllocated) handle.Free();
            SecureStringBytes.ZeroFill(blob);
        }

        return Task.CompletedTask;
    }

    public Task RemoveAsync(string host, CancellationToken ct)
    {
        EnsureWindows();
        var target = TargetFor(host);
        if (!NativeMethods.CredDelete(target, NativeMethods.CRED_TYPE_GENERIC, 0))
        {
            // Not fatal — log and move on; a stale SESSION credential clears at logoff anyway.
            _logger.LogWarning("CredDelete for {Target} returned Win32 {Error}", target, Marshal.GetLastWin32Error());
        }
        return Task.CompletedTask;
    }

    private static string TargetFor(string host) => $"TERMSRV/{host}";

    private static void EnsureWindows()
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Credential Manager is only available on Windows.");
    }
}
