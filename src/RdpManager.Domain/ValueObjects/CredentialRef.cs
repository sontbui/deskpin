using RdpManager.Domain.Enums;

namespace RdpManager.Domain.ValueObjects;

/// <summary>
/// A pointer to where a machine's secret lives — NEVER the secret itself.
/// The domain deliberately cannot represent a plaintext password.
/// </summary>
public sealed record CredentialRef(CredentialStoreKind Kind, string? Reference)
{
    public static readonly CredentialRef None = new(CredentialStoreKind.None, null);

    public static CredentialRef Prompt() => new(CredentialStoreKind.PromptEachTime, null);

    public static CredentialRef Dpapi(string reference) =>
        new(CredentialStoreKind.Dpapi, NotBlank(reference));

    public static CredentialRef CredentialManager(string targetName) =>
        new(CredentialStoreKind.CredentialManager, NotBlank(targetName));

    public bool RequiresStoredSecret => Kind is CredentialStoreKind.Dpapi or CredentialStoreKind.CredentialManager;

    private static string NotBlank(string s) =>
        string.IsNullOrWhiteSpace(s) ? throw new ArgumentException("Reference must not be blank.") : s;
}
