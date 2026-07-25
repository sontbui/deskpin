using System;
using System.Diagnostics;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace RdpManager.Presentation.Services;

/// <summary>
/// On startup, asks GitHub for the latest release and offers to download it if newer than the
/// running build. Fully best-effort: any network/parse failure is swallowed so it never blocks
/// the app. Silent installs are out of scope here (see release notes for the Velopack option).
/// </summary>
public sealed class UpdateChecker
{
    // TODO: point this at the real repository, e.g. "OPSWAT/deskpin".
    private const string Repository = "OPSWAT/deskpin";

    private readonly IDialogService _dialogs;
    public UpdateChecker(IDialogService dialogs) => _dialogs = dialogs;

    public async Task CheckAsync()
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(6) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("Deskpin-Updater");

            var json = await http.GetStringAsync($"https://api.github.com/repos/{Repository}/releases/latest");
            using var doc = JsonDocument.Parse(json);
            var tag = doc.RootElement.GetProperty("tag_name").GetString() ?? string.Empty;
            var url = doc.RootElement.GetProperty("html_url").GetString() ?? $"https://github.com/{Repository}/releases";

            var latest = ParseVersion(tag);
            var current = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);
            if (latest is null || latest <= current) return;

            var download = await _dialogs.ConfirmAsync(
                "Update available",
                $"Deskpin {tag} is available (you have v{current.ToString(3)}). Download it now?",
                "Download", "Later");

            if (download)
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch
        {
            // Offline, rate-limited, or repo not published yet — ignore.
        }
    }

    private static Version? ParseVersion(string tag)
    {
        var m = Regex.Match(tag ?? string.Empty, @"\d+(\.\d+){1,3}");
        return m.Success && Version.TryParse(m.Value, out var v) ? v : null;
    }
}
