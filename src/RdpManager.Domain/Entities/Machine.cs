using RdpManager.Domain.Enums;
using RdpManager.Domain.ValueObjects;

namespace RdpManager.Domain.Entities;

/// <summary>
/// Aggregate root: a remote machine profile. Owns its display profile, credential
/// reference, tags and history. Enforces its own invariants; holds no secret.
/// </summary>
public sealed class Machine
{
    private readonly List<Tag> _tags = new();

    public Guid Id { get; private set; } = Guid.NewGuid();
    public string Name { get; private set; } = string.Empty;
    public HostAddress Host { get; private set; } = HostAddress.Create("localhost");
    public string? Username { get; private set; }
    public CredentialRef Credential { get; private set; } = CredentialRef.None;
    public DisplayMode DisplayMode { get; private set; } = DisplayMode.NotConfigured;
    public DisplayProfile? DisplayProfile { get; private set; }
    public string? Gateway { get; private set; }
    public RedirectionFlags Redirection { get; private set; } = RedirectionFlags.Clipboard;
    public string? Notes { get; private set; }
    public bool IsFavorite { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? LastConnectedAt { get; private set; }

    public IReadOnlyList<Tag> Tags => _tags;
    public bool IsDisplayConfigured => DisplayProfile is not null && DisplayMode != DisplayMode.NotConfigured;

    private Machine() { } // EF

    public Machine(string name, HostAddress host, DateTimeOffset createdAt, string? username = null)
    {
        Rename(name);
        Host = host ?? throw new ArgumentNullException(nameof(host));
        Username = string.IsNullOrWhiteSpace(username) ? null : username.Trim();
        CreatedAt = createdAt;
    }

    public void Rename(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Machine name must not be empty.", nameof(name));
        Name = name.Trim();
    }

    public void ChangeHost(HostAddress host) => Host = host ?? throw new ArgumentNullException(nameof(host));
    public void SetUsername(string? username) => Username = string.IsNullOrWhiteSpace(username) ? null : username.Trim();
    public void SetCredential(CredentialRef credential) => Credential = credential ?? CredentialRef.None;
    public void SetGateway(string? gateway) => Gateway = string.IsNullOrWhiteSpace(gateway) ? null : gateway.Trim();
    public void SetNotes(string? notes) => Notes = notes;
    public void SetRedirection(RedirectionFlags flags) => Redirection = flags;
    public void ToggleFavorite(bool value) => IsFavorite = value;
    public void MarkConnected(DateTimeOffset when) => LastConnectedAt = when;

    public void ConfigureDisplay(DisplayProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (profile.MachineId != Id)
            throw new InvalidOperationException("Display profile belongs to a different machine.");
        DisplayProfile = profile;
        DisplayMode = profile.IsMultiMonitor ? DisplayMode.MultiMonitor : DisplayMode.SingleMonitor;
    }

    public void ClearDisplay()
    {
        DisplayProfile = null;
        DisplayMode = DisplayMode.NotConfigured;
    }

    public void AddTag(Tag tag)
    {
        ArgumentNullException.ThrowIfNull(tag);
        if (!_tags.Exists(t => string.Equals(t.Name, tag.Name, StringComparison.OrdinalIgnoreCase)))
            _tags.Add(tag);
    }

    public void RemoveTag(string name) =>
        _tags.RemoveAll(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));
}
