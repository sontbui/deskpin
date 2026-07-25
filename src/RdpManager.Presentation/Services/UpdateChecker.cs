using System;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace RdpManager.Presentation.Services;

/// <summary>
/// Checks GitHub for a newer release and, if found, shows its release notes with a Download button.
/// <paramref name="announceResult"/> = true makes it also report "you're up to date" / "couldn't
/// check" (used by the manual button in About); the startup check stays silent when there's nothing.
/// </summary>
public sealed class UpdateChecker
{
    private readonly IDialogService _dialogs;
    private readonly GitHubReleaseService _github;

    public UpdateChecker(IDialogService dialogs, GitHubReleaseService github)
    {
        _dialogs = dialogs;
        _github = github;
    }

    public async Task CheckAsync(bool announceResult = false)
    {
        var current = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 0, 0);
        var latest = await _github.GetLatestAsync();

        if (latest is null)
        {
            if (announceResult)
                await _dialogs.ErrorAsync("Check for updates", "Couldn't reach the update server right now.");
            return;
        }

        if (latest.Version <= current)
        {
            if (announceResult)
                await _dialogs.ErrorAsync("You're up to date", $"Deskpin v{current.ToString(3)} is the latest version.");
            return;
        }

        var notes = FormatNotes(latest.Body);
        var message = $"You have v{current.ToString(3)}.\n\nWhat's new:\n{notes}\n\nDownload it now?";
        if (await _dialogs.ConfirmAsync($"Update available — {latest.Title}", message, "Download", "Later"))
            Process.Start(new ProcessStartInfo(latest.Url) { UseShellExecute = true });
    }

    // Turn the GitHub markdown body into plain bullet lines for a ContentDialog.
    private static string FormatNotes(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return "•  See the release page for details.";
        var lines = body.Replace("\r\n", "\n").Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.Length > 0 && !l.StartsWith('#'))          // drop the markdown title line
            .Select(l => l.StartsWith("* ") || l.StartsWith("- ") ? "•  " + l[2..] : l);
        return string.Join("\n", lines);
    }
}
