using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace RdpManager.Presentation.Services;

/// <summary>
/// Files a bug as a GitHub issue on the app repository — by opening the browser at a
/// pre-filled "new issue" URL. Deliberately token-free: the user submits under their own
/// GitHub account, so the app never stores any credential (the DPAPI/Credential Manager
/// model stays untouched).
/// </summary>
public sealed class BugReportService
{
    private const int MaxLogChars = 1200;   // keep the URL well under GitHub's length limit

    public static string IssuesUrl => $"https://github.com/{GitHubReleaseService.Repository}/issues/new";

    /// <summary>Builds the pre-filled new-issue URL (title, labeled "bug", body with optional diagnostics).</summary>
    public string BuildIssueUrl(string title, string description, bool includeDiagnostics)
    {
        var body = new StringBuilder();
        body.AppendLine("**What happened**");
        body.AppendLine(string.IsNullOrWhiteSpace(description) ? "_describe the bug here_" : description.Trim());
        body.AppendLine();
        body.AppendLine("**Steps to reproduce**");
        body.AppendLine("1. ");
        body.AppendLine();
        body.AppendLine("**Expected**");
        body.AppendLine("- ");

        if (includeDiagnostics)
        {
            body.AppendLine();
            body.AppendLine("---");
            body.AppendLine("<details><summary>Diagnostics</summary>");
            body.AppendLine();
            body.AppendLine($"- Deskpin: v{Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "?"}");
            body.AppendLine($"- OS: {Environment.OSVersion.VersionString} ({RuntimeInformation.OSArchitecture})");
            body.AppendLine($"- .NET: {RuntimeInformation.FrameworkDescription}");

            var log = ReadRecentErrorLog();
            if (log.Length > 0)
            {
                body.AppendLine();
                body.AppendLine("Recent error log:");
                body.AppendLine("```");
                body.AppendLine(log);
                body.AppendLine("```");
            }
            body.AppendLine("</details>");
        }

        return IssuesUrl
               + "?labels=bug"
               + "&title=" + Uri.EscapeDataString(title.Trim())
               + "&body=" + Uri.EscapeDataString(body.ToString());
    }

    /// <summary>Opens the URL in the default browser. Returns false when the shell launch fails.</summary>
    public bool Open(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>Tail of %LOCALAPPDATA%\RdpManager\startup-error.log — the file App.xaml.cs appends to.</summary>
    private static string ReadRecentErrorLog()
    {
        try
        {
            var path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "RdpManager", "startup-error.log");
            if (!File.Exists(path)) return string.Empty;

            var lines = File.ReadAllLines(path);
            var tail = string.Join('\n', lines.TakeLast(25));
            return tail.Length <= MaxLogChars ? tail : tail[^MaxLogChars..];
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }
}
