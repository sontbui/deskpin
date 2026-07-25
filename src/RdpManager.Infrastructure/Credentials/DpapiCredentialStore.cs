using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using RdpManager.Application.Abstractions;
using RdpManager.Domain.Abstractions;
using RdpManager.Domain.Enums;
using RdpManager.Domain.ValueObjects;
using RdpManager.Infrastructure.Interop;
using RdpManager.Infrastructure.Persistence;

namespace RdpManager.Infrastructure.Credentials;

/// <summary>
/// Protects a secret at rest with Windows DPAPI (CurrentUser scope + per-record entropy) and
/// stores only the encrypted blob in SQLite. The plaintext exists in managed memory for the
/// shortest possible window and every intermediate buffer is zeroed.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class DpapiCredentialStore : ICredentialStore
{
    private readonly IDbContextFactory<AppDbContext> _factory;
    private readonly IClock _clock;

    public DpapiCredentialStore(IDbContextFactory<AppDbContext> factory, IClock clock)
    {
        _factory = factory;
        _clock = clock;
    }

    public async Task<CredentialRef> ProtectAsync(SecureString secret, CancellationToken ct)
    {
        EnsureWindows();
        ArgumentNullException.ThrowIfNull(secret);

        var entropy = RandomNumberGenerator.GetBytes(16);
        byte[]? plaintext = null;
        byte[] cipher;
        try
        {
            plaintext = SecureStringBytes.ToUtf16Bytes(secret);
            cipher = Protect(plaintext, entropy);
        }
        finally
        {
            SecureStringBytes.ZeroFill(plaintext);
        }

        var record = new ProtectedSecret
        {
            Reference = Guid.NewGuid().ToString("N"),
            ProtectedBlob = cipher,
            Entropy = entropy,
            CreatedAt = _clock.UtcNow,
        };

        await using var db = await _factory.CreateDbContextAsync(ct);
        db.Secrets.Add(record);
        await db.SaveChangesAsync(ct);

        return CredentialRef.Dpapi(record.Reference);
    }

    public async Task<SecureString> RevealAsync(CredentialRef reference, CancellationToken ct)
    {
        EnsureWindows();
        if (reference.Kind != CredentialStoreKind.Dpapi || reference.Reference is null)
            throw new InvalidOperationException("Not a DPAPI credential reference.");

        await using var db = await _factory.CreateDbContextAsync(ct);
        var record = await db.Secrets.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Reference == reference.Reference, ct)
            ?? throw new InvalidOperationException("Stored secret not found.");

        byte[]? plaintext = null;
        try
        {
            plaintext = Unprotect(record.ProtectedBlob, record.Entropy);
            return SecureStringBytes.FromUtf16Bytes(plaintext);
        }
        finally
        {
            SecureStringBytes.ZeroFill(plaintext);
        }
    }

    public async Task RemoveAsync(CredentialRef reference, CancellationToken ct)
    {
        if (reference.Kind != CredentialStoreKind.Dpapi || reference.Reference is null) return;
        await using var db = await _factory.CreateDbContextAsync(ct);
        var record = await db.Secrets.FirstOrDefaultAsync(s => s.Reference == reference.Reference, ct);
        if (record is null) return;
        db.Secrets.Remove(record);
        await db.SaveChangesAsync(ct);
    }

    private static void EnsureWindows()
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("DPAPI is only available on Windows.");
    }

    private static byte[] Protect(byte[] data, byte[] entropy) => RunDpapi(data, entropy, protect: true);
    private static byte[] Unprotect(byte[] data, byte[] entropy) => RunDpapi(data, entropy, protect: false);

    private static unsafe byte[] RunDpapi(byte[] data, byte[] entropy, bool protect)
    {
        fixed (byte* pData = data)
        fixed (byte* pEntropy = entropy)
        {
            var inBlob = new NativeMethods.DATA_BLOB { cbData = data.Length, pbData = (IntPtr)pData };
            var entBlob = new NativeMethods.DATA_BLOB { cbData = entropy.Length, pbData = (IntPtr)pEntropy };
            var outBlob = default(NativeMethods.DATA_BLOB);

            var ok = protect
                ? NativeMethods.CryptProtectData(ref inBlob, "RdpManager", ref entBlob, IntPtr.Zero, IntPtr.Zero, NativeMethods.CRYPTPROTECT_UI_FORBIDDEN, ref outBlob)
                : NativeMethods.CryptUnprotectData(ref inBlob, IntPtr.Zero, ref entBlob, IntPtr.Zero, IntPtr.Zero, NativeMethods.CRYPTPROTECT_UI_FORBIDDEN, ref outBlob);

            if (!ok) throw new CryptographicException(Marshal.GetLastWin32Error());

            try
            {
                var result = new byte[outBlob.cbData];
                Marshal.Copy(outBlob.pbData, result, 0, outBlob.cbData);
                return result;
            }
            finally
            {
                NativeMethods.LocalFree(outBlob.pbData);
            }
        }
    }
}
