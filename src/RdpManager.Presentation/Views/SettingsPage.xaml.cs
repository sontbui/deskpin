using System;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Win32;
using RdpManager.Infrastructure.Launching;
using RdpManager.Presentation.Services;

namespace RdpManager.Presentation.Views;

public sealed partial class SettingsPage : Page
{
    private const string TsKey = @"SOFTWARE\Policies\Microsoft\Windows NT\Terminal Services";

    public SettingsPage()
    {
        InitializeComponent();

        var v = Assembly.GetExecutingAssembly().GetName().Version;
        VersionText.Text = $"Deskpin v{v?.ToString(3) ?? "1.0.0"}";

        var note = App.Host.Services.GetRequiredService<ReleaseNotesReader>().ForCurrentVersion();
        if (note is not null && note.Bullets.Count > 0)
        {
            WhatsNewTitle.Text = $"What's new — {note.Title}";
            WhatsNewText.Text = string.Join("\n", note.Bullets.Select(b => "•  " + b));
        }
        else
        {
            WhatsNewTitle.Visibility = Visibility.Collapsed;
            WhatsNewText.Visibility = Visibility.Collapsed;
        }

        ThumbprintBox.Text = App.Host.Services.GetRequiredService<RdpSigner>().EnsureThumbprint() ?? "(unavailable)";
        RefreshSeamlessStatus();
    }

    private void OnOpenLogs(object sender, RoutedEventArgs e)
    {
        try
        {
            System.IO.Directory.CreateDirectory(LogPaths.Directory);
            Process.Start(new ProcessStartInfo { FileName = LogPaths.Directory, UseShellExecute = true });
        }
        catch (Exception)
        {
            // Explorer refused — nothing actionable.
        }
    }

    private void OnReportBug(object sender, RoutedEventArgs e)
    {
        var title = BugTitleBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(title))
        {
            BugStatus.Text = "Give the bug a short title first.";
            return;
        }

        var bugs = App.Host.Services.GetRequiredService<BugReportService>();
        var url = bugs.BuildIssueUrl(title, BugDescBox.Text ?? string.Empty, BugDiagCheck.IsChecked == true);
        if (bugs.Open(url))
        {
            BugStatus.Text = "GitHub opened in your browser — review and press Submit.";
            App.Host.Services.GetRequiredService<IToastService>()
                .Show("Bug report drafted — finish submitting it on GitHub.");
            BugTitleBox.Text = string.Empty;
            BugDescBox.Text = string.Empty;
        }
        else
        {
            BugStatus.Text = $"Couldn't open the browser. Report manually at {BugReportService.IssuesUrl}";
        }
    }

    private void RefreshSeamlessStatus()
    {
        var on = IsTrusted();
        SeamlessStatus.Text = on ? "Status: ON — Deskpin is a trusted publisher; no prompt." : "Status: OFF — Windows will prompt on connect.";
        EnableSeamlessButton.IsEnabled = !on;
        DisableSeamlessButton.IsEnabled = on;
    }

    private bool IsTrusted()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(TsKey);
            // Our thumbprint is present in the trusted-publisher list.
            var tp = (key?.GetValue("TrustedCertThumbprints") as string) ?? string.Empty;
            var want = ThumbprintBox.Text.Trim();
            return want.Length > 0 && tp.Contains(want, StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    // Writes exactly what the "trusted .rdp publishers" Group Policy writes (verified against gpedit),
    // so no manual gpedit is needed — works on Windows Home too.
    private void OnEnableSeamless(object sender, RoutedEventArgs e)
    {
        var tp = ThumbprintBox.Text.Trim();
        if (string.IsNullOrEmpty(tp) || tp.StartsWith('(')) return;
        var q = $"\"HKLM\\{TsKey}\"";
        var args =
            $"/c reg add {q} /v AllowSignedFiles /t REG_DWORD /d 1 /f & " +
            $"reg add {q} /v AllowUnsignedFiles /t REG_DWORD /d 1 /f & " +
            $"reg add {q} /v TrustedCertModules /t REG_SZ /d {tp} /f & " +
            $"reg add {q} /v TrustedCertThumbprints /t REG_SZ /d {tp} /f & " +
            $"reg add \"HKLM\\{TsKey}\\TrustedCertModules\" /v 1 /t REG_SZ /d {tp} /f";
        RunElevated(args);
    }

    private void OnDisableSeamless(object sender, RoutedEventArgs e)
    {
        var q = $"\"HKLM\\{TsKey}\"";
        var args =
            $"/c reg delete \"HKLM\\{TsKey}\\TrustedCertModules\" /f & " +
            $"reg delete {q} /v TrustedCertModules /f & " +
            $"reg delete {q} /v TrustedCertThumbprints /f & " +
            $"reg delete {q} /v AllowSignedFiles /f & " +
            $"reg delete {q} /v AllowUnsignedFiles /f";
        RunElevated(args);
    }

    private void RunElevated(string cmdArgs)
    {
        try
        {
            var psi = new ProcessStartInfo("cmd.exe", cmdArgs)
            {
                UseShellExecute = true,
                Verb = "runas", // UAC prompt
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
            };
            using var p = Process.Start(psi);
            p?.WaitForExit(15_000);
        }
        catch (Exception)
        {
            // UAC declined or failed — leave state unchanged.
        }
        RefreshSeamlessStatus();
    }

    private async void OnCheckUpdates(object sender, RoutedEventArgs e)
    {
        CheckUpdatesButton.IsEnabled = false;
        try
        {
            await App.Host.Services.GetRequiredService<UpdateChecker>().CheckAsync(announceResult: true);
        }
        finally
        {
            CheckUpdatesButton.IsEnabled = true;
        }
    }
}
