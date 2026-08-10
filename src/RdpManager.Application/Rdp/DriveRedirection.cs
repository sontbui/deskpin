namespace RdpManager.Application.Rdp;

/// <summary>
/// The parsed value of the .rdp <c>drivestoredirect:s:</c> field that <see cref="RdpProfileBuilder"/>
/// emits: <c>*</c> (all drives), empty (off), or a list like <c>C:;D:;DynamicDrives</c>.
/// Pure and deterministic — the Infrastructure reader/writer persists it, this type owns the mapping.
/// Drive redirection is the substrate of the Files console: when it's off, the console is off.
/// </summary>
public sealed record DriveRedirection
{
    /// <summary>The mstsc token that redirects drives plugged in after the session starts.</summary>
    public const string DynamicDrivesToken = "DynamicDrives";

    public static readonly DriveRedirection All = new(true, false, Array.Empty<string>());
    public static readonly DriveRedirection None = new(false, false, Array.Empty<string>());

    public bool RedirectAll { get; }
    public bool IncludeDynamicDrives { get; }
    /// <summary>Specific drive roots, normalized to "C:" form. Empty when RedirectAll or None.</summary>
    public IReadOnlyList<string> Drives { get; }

    /// <summary>True when the remote session can see at least something — i.e. the Files console can work.</summary>
    public bool IsEnabled => RedirectAll || IncludeDynamicDrives || Drives.Count > 0;

    private DriveRedirection(bool all, bool dynamic, IReadOnlyList<string> drives)
    {
        RedirectAll = all;
        IncludeDynamicDrives = dynamic;
        Drives = drives;
    }

    public static DriveRedirection Of(IEnumerable<string> drives, bool includeDynamicDrives = false)
    {
        ArgumentNullException.ThrowIfNull(drives);
        var normalized = drives
            .Select(NormalizeDrive)
            .Where(d => d is not null)
            .Select(d => d!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(d => d, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return new DriveRedirection(false, includeDynamicDrives, normalized);
    }

    /// <summary>Parses a raw <c>drivestoredirect:s:</c> value. Null/blank → None; "*" → All.</summary>
    public static DriveRedirection Parse(string? rdpValue)
    {
        if (string.IsNullOrWhiteSpace(rdpValue)) return None;
        var text = rdpValue.Trim();
        if (text == "*") return All;

        var dynamic = false;
        var drives = new List<string>();
        foreach (var raw in text.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (string.Equals(raw, DynamicDrivesToken, StringComparison.OrdinalIgnoreCase)) { dynamic = true; continue; }
            var drive = NormalizeDrive(raw);
            if (drive is not null && !drives.Contains(drive, StringComparer.OrdinalIgnoreCase))
                drives.Add(drive);
        }
        drives.Sort(StringComparer.OrdinalIgnoreCase);
        return new DriveRedirection(false, dynamic, drives);
    }

    /// <summary>Formats back to the exact field value the profile builder writes.</summary>
    public string ToRdpValue()
    {
        if (RedirectAll) return "*";
        if (!IsEnabled) return string.Empty;
        var parts = new List<string>(Drives);
        if (IncludeDynamicDrives) parts.Add(DynamicDrivesToken);
        return string.Join(';', parts) + ";";
    }

    /// <summary>"C", "c:", "C:\" → "C:". Anything else → null (ignored).</summary>
    private static string? NormalizeDrive(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var text = raw.Trim().TrimEnd('\\', '/');
        if (text.Length == 1 && char.IsAsciiLetter(text[0])) return char.ToUpperInvariant(text[0]) + ":";
        if (text.Length == 2 && char.IsAsciiLetter(text[0]) && text[1] == ':') return char.ToUpperInvariant(text[0]) + ":";
        return null;
    }

    public override string ToString() => RedirectAll ? "*" : ToRdpValue();
}

/// <summary>
/// Reads/writes a machine's drive-redirection choice (Infrastructure persists it and keeps the
/// machine's <c>RedirectionFlags.Drives</c> flag in sync so the generated .rdp matches).
/// </summary>
public interface IDriveRedirectionSettings
{
    Task<DriveRedirection> GetAsync(Guid machineId, CancellationToken ct);
    Task SetAsync(Guid machineId, DriveRedirection value, CancellationToken ct);
}
