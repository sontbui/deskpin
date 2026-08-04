using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using RdpManager.Presentation.Views;

namespace RdpManager.Presentation.Services;

/// <summary>
/// Checks GitHub for a newer release and, if the user agrees, downloads its installer (with a
/// progress dialog) and runs it to update in place — the installer closes the running app,
/// replaces files, and relaunches. <c>announceResult</c> = true also reports "up to date" /
/// "couldn't check" (used by the manual button in About).
/// </summary>
public sealed class UpdateChecker
{
    private readonly IDialogService _dialogs;
    private readonly IToastService _toasts;
    private readonly GitHubReleaseService _github;

    public UpdateChecker(IDialogService dialogs, IToastService toasts, GitHubReleaseService github)
    {
        _dialogs = dialogs;
        _toasts = toasts;
        _github = github;
    }

    public async Task CheckAsync(bool announceResult = false)
    {
#if DEBUG
        // Debug builds pretend to be an old version so any newer release looks like an update
        // — lets you test the whole download/install flow. Release builds use the real version.
        var current = new Version(1, 0, 0);
#else
        var current = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 0, 0);
#endif

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
        var message = $"You have v{current.ToString(3)}.\n\nWhat's new:\n{notes}\n\nInstall the update now?";
        if (!await _dialogs.ConfirmAsync($"Update available — {latest.Title}", message, "Update now", "Later"))
            return;

        // No installer attached → open the release page.
        if (string.IsNullOrEmpty(latest.InstallerUrl))
        {
            OpenInBrowser(latest.Url);
            return;
        }

        var setupPath = Path.Combine(Path.GetTempPath(), $"Deskpin-Setup-{latest.Version}.exe");
        var dialog = new DownloadProgressDialog { XamlRoot = _dialogs.XamlRoot };
        var showTask = dialog.ShowAsync(); // don't await — runs while we download

        try
        {
            await DownloadAsync(latest.InstallerUrl, setupPath, dialog.Report, dialog.Token);
            dialog.Hide();

            // /SILENT shows only a progress bar; the installer closes Deskpin, updates and relaunches it.
            Process.Start(new ProcessStartInfo(setupPath) { Arguments = "/SILENT", UseShellExecute = true });
            Microsoft.UI.Xaml.Application.Current.Exit();
        }
        catch (OperationCanceledException)
        {
            dialog.Hide();
            TryDelete(setupPath);
        }
        catch
        {
            dialog.Hide();
            _toasts.Show("Update download failed — opening the download page.");
            OpenInBrowser(latest.Url);
        }

        _ = showTask; // keep the compiler happy; dialog is closed via Hide()
    }

    private static async Task DownloadAsync(string url, string destPath, Action<double, string> report, CancellationToken ct)
    {
        using var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Deskpin-Updater");

        using var resp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();

        var total = resp.Content.Headers.ContentLength ?? -1L;
        await using var src = await resp.Content.ReadAsStreamAsync(ct);
        await using var dst = File.Create(destPath);

        var buffer = new byte[81920];
        long readTotal = 0;
        int lastPercent = -1;
        report(total > 0 ? 0 : -1, "Starting…");

        int n;
        while ((n = await src.ReadAsync(buffer, ct)) > 0)
        {
            await dst.WriteAsync(buffer.AsMemory(0, n), ct);
            readTotal += n;

            if (total > 0)
            {
                var percent = (int)(readTotal * 100 / total);
                if (percent != lastPercent)
                {
                    lastPercent = percent;
                    report(readTotal / (double)total, $"{readTotal / 1048576} / {total / 1048576} MB  ({percent}%)");
                }
            }
            else
            {
                report(-1, $"{readTotal / 1048576} MB");
            }
        }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { /* ignore */ }
    }

    private static void OpenInBrowser(string url) =>
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });

    // Turn the GitHub markdown body into plain bullet lines for a dialog.
    private static string FormatNotes(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return "•  See the release page for details.";
        var lines = body.Replace("\r\n", "\n").Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.Length > 0 && !l.StartsWith('#'))
            .Select(l => l.StartsWith("* ") || l.StartsWith("- ") ? "•  " + l[2..] : l);
        return string.Join("\n", lines);
    }
}
