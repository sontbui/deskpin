using System;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace RdpManager.Presentation.Services;

public sealed record GitHubRelease(Version Version, string Title, string Body, string Url);

/// <summary>Reads the latest published release from GitHub. Best-effort; returns null on any failure.</summary>
public sealed class GitHubReleaseService
{
    // TODO: point this at the real repository, e.g. "sontbui/deskpin".
    public const string Repository = "sontbui/deskpin";

    public async Task<GitHubRelease?> GetLatestAsync()
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(6) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("Deskpin-Updater");

            var json = await http.GetStringAsync($"https://api.github.com/repos/{Repository}/releases/latest");
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var tag = root.GetProperty("tag_name").GetString() ?? string.Empty;
            var name = root.TryGetProperty("name", out var nm) ? nm.GetString() : null;
            var body = root.TryGetProperty("body", out var b) ? b.GetString() : null;
            var url = root.TryGetProperty("html_url", out var u) ? u.GetString() : $"https://github.com/{Repository}/releases";

            var version = ParseVersion(tag);
            if (version is null) return null;
            return new GitHubRelease(version, string.IsNullOrWhiteSpace(name) ? tag : name!, body ?? string.Empty, url!);
        }
        catch
        {
            return null; // offline, rate-limited, or repo not published yet
        }
    }

    private static Version? ParseVersion(string tag)
    {
        var m = Regex.Match(tag ?? string.Empty, @"\d+(\.\d+){1,3}");
        return m.Success && Version.TryParse(m.Value, out var v) ? v : null;
    }
}
