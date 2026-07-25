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
        return Read(Path.Combine(AppContext.BaseDirectory, "release-notes", $"{version}.json"));
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
