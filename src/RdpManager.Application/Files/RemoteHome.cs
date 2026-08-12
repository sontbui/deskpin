using RdpManager.Domain.Enums;

namespace RdpManager.Application.Files;

/// <summary>
/// Where the remote pane should open for a machine: straight into the user's home, per OS.
/// Pure and deterministic (unit-tested). Windows homes ride the C$ admin share; Linux/macOS
/// use the Samba convention of a share named after the user (the [homes] section).
/// </summary>
public static class RemoteHome
{
    /// <summary>The default directory for a machine's remote pane.</summary>
    public static string PathFor(MachineOs os, string? username, string host)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        var leaf = UserLeaf(username);

        return os switch
        {
            MachineOs.Windows => leaf is null ? @"C:\" : $@"C:\Users\{leaf}",
            // Samba/macOS file sharing expose the home directory as a share named after the user.
            _ => leaf is null ? $@"\\{host}\home" : $@"\\{host}\{leaf}",
        };
    }

    /// <summary>"DOMAIN\user" → "user"; "user@corp.local" → "user"; "  " → null.</summary>
    public static string? UserLeaf(string? username)
    {
        if (string.IsNullOrWhiteSpace(username)) return null;
        var name = username.Trim();

        var slash = name.LastIndexOfAny(new[] { '\\', '/' });
        if (slash >= 0) name = name[(slash + 1)..];

        var at = name.IndexOf('@', StringComparison.Ordinal);
        if (at > 0) name = name[..at];

        return string.IsNullOrWhiteSpace(name) ? null : name;
    }
}
