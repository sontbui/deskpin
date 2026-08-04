using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;

namespace RdpManager.Presentation.Services;

public sealed record ReleaseNote(string Title, IReadOnlyList<string> Bullets);

/// <summary>
/// Loads the release note that shipped with this build (release-notes/&lt;version&gt;.json,
/// copied next to the exe). Used by the About page to show "What's new" without any network.
/// </summary>
public sealed class ReleaseNotesReader
{
    public ReleaseNote? ForCurrentVersion()
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";
        var dir = Path.Combine(AppContext.BaseDirectory, "release-notes");

        // Exact match first.
        var exact = Read(Path.Combine(dir, $"{version}.json"));
        if (exact is not null) return exact;

        // Fall back to the most recent notes shipped, retitled to this version.
        if (!Directory.Exists(dir)) return null;
        var newest = Directory.GetFiles(dir, "*.json")
            .Where(f => Version.TryParse(Path.GetFileNameWithoutExtension(f), out _))
            .OrderByDescending(f => Version.Parse(Path.GetFileNameWithoutExtension(f)))
            .FirstOrDefault();

        var fallback = newest is null ? null : Read(newest);
        return fallback is null ? null : fallback with { Title = $"Deskpin {version}" };
    }

    public static ReleaseNote? Read(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var title = doc.RootElement.GetProperty("title").GetString() ?? "Release notes";
            var bullets = doc.RootElement.GetProperty("description").EnumerateArray()
                .Select(e => e.TryGetProperty("content", out var c) ? c.GetString() : null)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => s!)
                .ToList();
            return new ReleaseNote(title, bullets);
        }
        catch
        {
            return null;
        }
    }
}
