using System.Text;
using Microsoft.Extensions.Logging;

namespace RdpManager.Infrastructure.Rdp;

/// <summary>
/// Writes the transient .rdp under a per-user directory with a random name, and cleans up.
/// The file never contains a secret (see RdpProfileBuilder), but it is still short-lived and
/// removed after launch. A startup sweep removes files orphaned by a crash.
/// </summary>
public sealed class TempRdpFileWriter
{
    private readonly ILogger<TempRdpFileWriter> _logger;
    private readonly string _dir;

    public TempRdpFileWriter(ILogger<TempRdpFileWriter> logger, string? overrideDir = null)
    {
        _logger = logger;
        _dir = overrideDir ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RdpManager", "temp");
    }

    public string Directory => _dir;

    public async Task<string> WriteAsync(string rdpText, CancellationToken ct)
    {
        System.IO.Directory.CreateDirectory(_dir);
        var path = Path.Combine(_dir, $"session-{Guid.NewGuid():N}.rdp");

        // BOM-less UTF-8 with the CRLF text the builder produced; mstsc is happy with UTF-8.
        await File.WriteAllTextAsync(path, rdpText, new UTF8Encoding(false), ct);

        if (OperatingSystem.IsWindows())
        {
            // LocalApplicationData is already per-user; nothing further needed. On *nix (tests)
            // tighten to owner-only so the guarantee holds there too.
        }
        else
        {
            try { File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite); }
            catch (Exception ex) { _logger.LogDebug(ex, "Could not set unix file mode on temp rdp."); }
        }

        return path;
    }

    public void DeleteQuietly(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to delete temp rdp {Path}", path);
        }
    }

    /// <summary>Removes leftover temp files older than <paramref name="maxAge"/> (crash recovery).</summary>
    public int SweepOrphans(TimeSpan maxAge, DateTimeOffset now)
    {
        if (!System.IO.Directory.Exists(_dir)) return 0;
        var removed = 0;
        foreach (var file in System.IO.Directory.EnumerateFiles(_dir, "session-*.rdp"))
        {
            try
            {
                if (now - File.GetLastWriteTimeUtc(file) > maxAge)
                {
                    File.Delete(file);
                    removed++;
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Sweep could not remove {File}", file);
            }
        }
        return removed;
    }
}
