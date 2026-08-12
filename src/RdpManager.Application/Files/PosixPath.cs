using RdpManager.Application.Common;

namespace RdpManager.Application.Files;

/// <summary>
/// Pure POSIX path parsing/validation for SFTP hosts: <c>/</c> root and separators, resolves
/// <c>.</c>/<c>..</c>, and accepts <c>\</c> typed by Windows users (normalized to <c>/</c>).
/// Deliberately free of System.IO so it behaves identically on every OS the tests run on.
/// </summary>
public static class PosixPath
{
    public const char Separator = '/';

    public static Result<string> Normalize(string? input, string? basePath = null)
    {
        if (string.IsNullOrWhiteSpace(input))
            return Error.Validation("Type a directory path first.");

        var text = input.Trim().Replace('\\', Separator);

        // Relative input resolves against the pane's current directory when we have one.
        if (text[0] != Separator)
        {
            if (string.IsNullOrWhiteSpace(basePath))
                return Error.Validation($"'{input.Trim()}' is not an absolute path. Start at / .");
            var baseResult = Normalize(basePath);
            if (!baseResult.IsSuccess) return baseResult.Error!;
            var baseVal = baseResult.Value;
            return Normalize((baseVal.EndsWith(Separator) ? baseVal : baseVal + Separator) + text);
        }

        var segments = new List<string>();
        foreach (var segment in text.Split(Separator, StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment == ".") continue;
            if (segment == "..")
            {
                if (segments.Count == 0) return Error.Validation("The path climbs above the root.");
                segments.RemoveAt(segments.Count - 1);
                continue;
            }
            if (segment.Contains('\0', StringComparison.Ordinal))
                return Error.Validation("The path contains an invalid character.");
            segments.Add(segment);
        }

        return segments.Count == 0 ? "/" : "/" + string.Join(Separator, segments);
    }

    public static string Join(string directory, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return directory.EndsWith(Separator) ? directory + name : directory + Separator + name;
    }

    public static string GetFileName(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var trimmed = path.TrimEnd(Separator);
        var idx = trimmed.LastIndexOf(Separator);
        return idx < 0 ? trimmed : trimmed[(idx + 1)..];
    }

    /// <summary>Parent directory, or null when <paramref name="path"/> is the root.</summary>
    public static string? GetParent(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var trimmed = path.TrimEnd(Separator);
        if (trimmed.Length == 0) return null; // "/" → root
        var idx = trimmed.LastIndexOf(Separator);
        if (idx < 0) return null;
        return idx == 0 ? "/" : trimmed[..idx];
    }

    public static IReadOnlyList<BreadcrumbSegment> Breadcrumbs(string normalizedPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(normalizedPath);
        var crumbs = new List<BreadcrumbSegment> { new("/", "/") };
        var current = string.Empty;
        foreach (var segment in normalizedPath.Split(Separator, StringSplitOptions.RemoveEmptyEntries))
        {
            current += Separator + segment;
            crumbs.Add(new BreadcrumbSegment(segment, current));
        }
        return crumbs;
    }

    /// <summary>"report.pdf" → "report (copy).pdf"; ordinal 2 → "report (copy 2).pdf".</summary>
    public static string CopyVariant(string fileName, int ordinal = 1)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        var dot = fileName.LastIndexOf('.');
        var stem = dot > 0 ? fileName[..dot] : fileName;
        var ext = dot > 0 ? fileName[dot..] : string.Empty;
        var tag = ordinal <= 1 ? " (copy)" : $" (copy {ordinal})";
        return stem + tag + ext;
    }
}
