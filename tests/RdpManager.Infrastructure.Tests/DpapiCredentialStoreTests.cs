using System.Runtime.InteropServices;
using System.Security;
using RdpManager.Domain.Abstractions;
using RdpManager.Domain.Enums;
using RdpManager.Infrastructure.Credentials;
using Xunit;

namespace RdpManager.Infrastructure.Tests;

/// <summary>
/// DPAPI is Windows-only, so these facts are skipped elsewhere. On Windows they prove the secret
/// round-trips through the encrypted-at-rest store and that only a reference is exposed.
/// </summary>
public sealed class DpapiCredentialStoreTests : IDisposable
{
    private readonly TestDbContextFactory _factory = new();

    private static SecureString Secret(string s)
    {
        var ss = new SecureString();
        foreach (var c in s) ss.AppendChar(c);
        ss.MakeReadOnly();
        return ss;
    }

    [Fact]
    public async Task Protect_then_reveal_round_trips_on_windows()
    {
        if (!OperatingSystem.IsWindows()) return; // skipped off-Windows

        var store = new DpapiCredentialStore(_factory, new SystemClock());
        var reference = await store.ProtectAsync(Secret("hunter2!"), CancellationToken.None);

        Assert.Equal(CredentialStoreKind.Dpapi, reference.Kind);
        Assert.NotNull(reference.Reference);

        using var revealed = await store.RevealAsync(reference, CancellationToken.None);
        Assert.Equal("hunter2!", Marshal.PtrToStringUni(Marshal.SecureStringToBSTR(revealed)));

        await store.RemoveAsync(reference, CancellationToken.None);
    }

    [Fact]
    public async Task Stored_blob_is_not_the_plaintext()
    {
        if (!OperatingSystem.IsWindows()) return;

        var store = new DpapiCredentialStore(_factory, new SystemClock());
        var reference = await store.ProtectAsync(Secret("plaintextpw"), CancellationToken.None);

        using var db = _factory.CreateDbContext();
        var row = db.Secrets.Single(s => s.Reference == reference.Reference);
        var asText = System.Text.Encoding.UTF8.GetString(row.ProtectedBlob);
        Assert.DoesNotContain("plaintextpw", asText, StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose() => _factory.Dispose();
}
