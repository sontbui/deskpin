using RdpManager.Application.Common;

namespace RdpManager.Application.Files;

/// <summary>A breadcrumb segment: the label shown and the full path navigating to it.</summary>
public sealed record BreadcrumbSegment(string Label, string Path);

/// <summary>
/// Pure Windows-path parsing/validation for the "go to directory" address bar. Accepts
/// <c>\</c> or <c>/</c> separators, resolves <c>.</c>/<c>..</c>, and validates before any
/// navigation happens — deliberately free of System.IO.Path so behavior is identical on
/// every OS the tests run on.
/// </summary>
public static class TransferPath
{
    public const char Separator = '\\';

    // Windows-invalid name characters (separators are handled before this check).
    private static readonly char[] InvalidNameChars = { '<', '>', ':', '"', '|', '?', '*' };

    /// <summary>
    /// Parses and validates user input into a normalized absolute Windows path.
    /// Relative input resolves against <paramref name="basePath"/> when one is given.
    /// </summary>
    public static Result<string> Normalize(string? input, string? basePath = null)
    {
        if (string.IsNullOrWhiteSpace(input))
            return Error.Validation("Type a directory path first.");

        var text = input.Trim().Replace('/', Separator);

        bool isUnc = text.StartsWith(@"\\", StringComparison.Ordinal);
        string root;
        string rest;

        if (isUnc)
        {
            var body = text.TrimStart(Separator);
            var parts = body.Split(Separator, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
                return Error.Validation(@"A network path needs a server and a share, like \\server\share.");
            var serverError = ValidateSegment(parts[0]) ?? ValidateSegment(parts[1]);
            if (serverError is not null) return serverError;
            root = @"\\" + parts[0] + Separator + parts[1];
            rest = string.Join(Separator, parts.Skip(2));
        }
        else if (text.Length >= 2 && IsDriveLetter(text[0]) && text[1] == ':')
        {
            root = char.ToUpperInvariant(text[0]) + ":";
            rest = text.Length > 2 ? text[2..] : string.Empty;
            if (rest.Length > 0 && rest[0] != Separator)
                return Error.Validation($"'{input.Trim()}' is not a valid path — expected something like {root}\\folder.");
        }
        else
        {
            // Relative input — resolve against the pane's current directory when we have one.
            if (string.IsNullOrWhiteSpace(basePath))
                return Error.Validation($"'{input.Trim()}' is not an absolute path. Start with a drive (C:\\…) or \\\\server\\share.");
            var baseResult = Normalize(basePath);
            if (!baseResult.IsSuccess) return baseResult.Error!;
            return Normalize(baseResult.Value + Separator + text);
        }

        var segments = new List<string>();
        foreach (var segment in rest.Split(Separator, StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment == ".") continue;
            if (segment == "..")
            {
                if (segments.Count == 0)
                    return Error.Validation("The path climbs above its root.");
                segments.RemoveAt(segments.Count - 1);
                continue;
            }
            var error = ValidateSegment(segment);
            if (error is not null) return error;
            segments.Add(segment);
        }

        if (segments.Count == 0)
            return isUnc ? root : root + Separator; // "C:\" for a drive root; "\\server\share" for UNC

        return root + Separator + string.Join(Separator, segments);
    }

    /// <summary>Joins a normalized directory with a bare file/folder name.</summary>
    public static string Join(string directory, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return directory.EndsWith(Separator) ? directory + name : directory + Separator + name;
    }

    /// <summary>Last segment of a path ("C:\a\b.txt" → "b.txt").</summary>
    public static string GetFileName(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var trimmed = path.TrimEnd(Separator);
        var idx = trimmed.LastIndexOf(Separator);
        return idx < 0 ? trimmed : trimmed[(idx + 1)..];
    }

    /// <summary>Parent directory, or null when <paramref name="path"/> is already a root.</summary>
    public static string? GetParent(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var normalized = Normalize(path);
        if (!normalized.IsSuccess) return null;
        var value = normalized.Value;

        if (value.StartsWith(@"\\", StringComparison.Ordinal))
        {
            var parts = value.TrimStart(Separator).Split(Separator);
            if (parts.Length <= 2) return null; // \\server\share is the UNC root
            return @"\\" + string.Join(Separator, parts[..^1]);
        }

        var trimmed = value.TrimEnd(Separator);
        var idx = trimmed.LastIndexOf(Separator);
        if (idx < 0) return null; // "C:\" trims to "C:" — already a root
        var parent = trimmed[..idx];
        return parent.Length == 2 && parent[1] == ':' ? parent + Separator : parent; // "C:" gets its slash back
    }

    /// <summary>Breadcrumb segments for a normalized path, each with the full path it navigates to.</summary>
    public static IReadOnlyList<BreadcrumbSegment> Breadcrumbs(string normalizedPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(normalizedPath);
        var crumbs = new List<BreadcrumbSegment>();

        if (normalizedPath.StartsWith(@"\\", StringComparison.Ordinal))
        {
            var parts = normalizedPath.TrimStart(Separator).Split(Separator, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2) return crumbs;
            var root = @"\\" + parts[0] + Separator + parts[1];
            crumbs.Add(new BreadcrumbSegment(parts[0] + Separator + parts[1], root));
            var current = root;
            foreach (var part in parts.Skip(2))
            {
                current = Join(current, part);
                crumbs.Add(new BreadcrumbSegment(part, current));
            }
            return crumbs;
        }

        var segments = normalizedPath.Split(Separator, StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0) return crumbs;
        var head = segments[0] + Separator; // "C:\"
        crumbs.Add(new BreadcrumbSegment(segments[0], head));
        var path = head;
        foreach (var segment in segments.Skip(1))
        {
            path = path.EndsWith(Separator) ? path + segment : path + Separator + segment;
            crumbs.Add(new BreadcrumbSegment(segment, path));
        }
        return crumbs;
    }

    /// <summary>"report.pdf" → "report (copy).pdf"; ordinal 2 → "report (copy 2).pdf". Used by Keep-both.</summary>
    public static string CopyVariant(string fileName, int ordinal = 1)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        var dot = fileName.LastIndexOf('.');
        var stem = dot > 0 ? fileName[..dot] : fileName;
        var ext = dot > 0 ? fileName[dot..] : string.Empty;
        var tag = ordinal <= 1 ? " (copy)" : $" (copy {ordinal})";
        return stem + tag + ext;
    }

    private static bool IsDriveLetter(char c) => c is >= 'a' and <= 'z' or >= 'A' and <= 'Z';

    private static Error? ValidateSegment(string segment)
    {
        if (segment.Length == 0)
            return Error.Validation("The path contains an empty segment.");
        foreach (var ch in segment)
        {
            if (ch < ' ' || Array.IndexOf(InvalidNameChars, ch) >= 0)
                return Error.Validation($"'{segment}' contains a character that isn't allowed in a folder name.");
        }
        if (segment[^1] is '.' or ' ')
            return Error.Validation($"'{segment}' can't end with a dot or a space.");
        return null;
    }
}
