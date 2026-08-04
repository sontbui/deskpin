using System.Diagnostics;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace RdpManager.Infrastructure.Launching;

/// <summary>
/// Signs the temporary .rdp with a self-signed "Deskpin" certificate that is trusted for the
/// current user, so mstsc shows a verified publisher instead of the scary "Unknown publisher"
/// caution. The certificate is created once and reused. Adding it to the current-user Root store
/// triggers a one-time Windows trust prompt on first use; after that, launches are prompt-free.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class RdpSigner
{
    private const string Subject = "CN=Deskpin";
    private const string BypassKey = @"Software\Microsoft\Terminal Server Client\PublisherBypassList";
    private readonly ILogger<RdpSigner> _logger;

    public RdpSigner(ILogger<RdpSigner> logger) => _logger = logger;

    public void SignInPlace(string rdpPath)
    {
        if (!OperatingSystem.IsWindows()) return;
        try
        {
            var thumbprint = GetOrCreateCertThumbprint();
            if (thumbprint is null) return;

            RememberPublisher(thumbprint); // pre-check "Remember my choices" so mstsc stops prompting

            var psi = new ProcessStartInfo("rdpsign.exe", $"/sha256 {thumbprint} \"{rdpPath}\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            using var p = Process.Start(psi);
            p?.WaitForExit(10_000);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not sign the temporary .rdp; the publisher warning may appear.");
        }
    }

    // Marks our publisher as trusted so mstsc's "Verify the publisher" prompt (with the
    // "Remember my choices" checkbox) doesn't appear. The value name is the cert thumbprint.
    private void RememberPublisher(string thumbprint)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(BypassKey);
            key.SetValue(thumbprint, 1, RegistryValueKind.DWord);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not pre-trust the Deskpin publisher.");
        }
    }

    /// <summary>Creates the Deskpin signing cert if needed and returns its SHA1 thumbprint (for the trust policy).</summary>
    public string? EnsureThumbprint()
    {
        if (!OperatingSystem.IsWindows()) return null;
        try { return GetOrCreateCertThumbprint(); } catch { return null; }
    }

    /// <summary>Removes the Deskpin certificate + publisher trust from all current-user stores (uninstall).</summary>
    public void RemoveCertificate()
    {
        if (!OperatingSystem.IsWindows()) return;

        // Forget the publisher trust entries for our certs.
        try
        {
            using var my = new X509Store(StoreName.My, StoreLocation.CurrentUser);
            my.Open(OpenFlags.ReadOnly);
            using var bypass = Registry.CurrentUser.OpenSubKey(BypassKey, writable: true);
            if (bypass is not null)
                foreach (var c in my.Certificates.Find(X509FindType.FindBySubjectDistinguishedName, Subject, false))
                    try { bypass.DeleteValue(c.Thumbprint, throwOnMissingValue: false); } catch { /* ignore */ }
        }
        catch (Exception ex) { _logger.LogDebug(ex, "Could not remove publisher trust."); }

        foreach (var name in new[] { StoreName.My, StoreName.Root, StoreName.TrustedPublisher })
        {
            try
            {
                using var store = new X509Store(name, StoreLocation.CurrentUser);
                store.Open(OpenFlags.ReadWrite);
                var matches = store.Certificates.Find(X509FindType.FindBySubjectDistinguishedName, Subject, false);
                foreach (var c in matches) store.Remove(c);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Could not remove Deskpin cert from {Store}.", name);
            }
        }
    }

    private static string? GetOrCreateCertThumbprint()
    {
        using var my = new X509Store(StoreName.My, StoreLocation.CurrentUser);
        my.Open(OpenFlags.ReadWrite);

        var found = my.Certificates.Find(X509FindType.FindBySubjectDistinguishedName, Subject, false);
        if (found.Count > 0) return found[0].Thumbprint;

        using var rsa = RSA.Create(2048);
        var req = new CertificateRequest(Subject, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        req.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
            new OidCollection { new Oid("1.3.6.1.5.5.7.3.3") }, critical: true)); // Code Signing
        req.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, critical: false));

        using var created = req.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(10));
        var pfx = created.Export(X509ContentType.Pfx);
        var persisted = X509CertificateLoader.LoadPkcs12(
            pfx, null, X509KeyStorageFlags.PersistKeySet | X509KeyStorageFlags.Exportable | X509KeyStorageFlags.UserKeySet);

        my.Add(persisted);
        AddPublic(StoreName.Root, persisted);            // needed so the self-signed chain is trusted
        AddPublic(StoreName.TrustedPublisher, persisted); // needed so the .rdp signature is trusted
        return persisted.Thumbprint;
    }

    private static void AddPublic(StoreName name, X509Certificate2 cert)
    {
        using var store = new X509Store(name, StoreLocation.CurrentUser);
        store.Open(OpenFlags.ReadWrite);
        if (store.Certificates.Find(X509FindType.FindByThumbprint, cert.Thumbprint, false).Count == 0)
        {
            var pub = X509CertificateLoader.LoadCertificate(cert.Export(X509ContentType.Cert));
            store.Add(pub);
        }
    }
}
