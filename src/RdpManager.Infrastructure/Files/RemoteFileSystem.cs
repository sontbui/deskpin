using Microsoft.Extensions.Logging;
using RdpManager.Application.Files;

namespace RdpManager.Infrastructure.Files;

/// <summary>
/// <see cref="IRemoteFileSystem"/> over SMB. A remote drive path like <c>C:\Users\svc</c> maps to
/// the target host's administrative share — <c>\\host\C$\Users\svc</c> — which is what is actually
/// reachable from the local machine. (<c>\\tsclient</c> only exists *inside* the remote session:
/// it is how the remote sees the local drives, so it can never be browsed from here.)
/// UNC paths (Samba homes like <c>\\host\svc</c>) pass through untouched.
/// Copies stream in 256 KiB chunks, report cumulative bytes through <see cref="IProgress{T}"/>,
/// honor the <see cref="CancellationToken"/> between chunks, and never leave a partial file behind.
/// </summary>
public sealed class RemoteFileSystem : IRemoteFileSystem
{
    private const int CopyBufferSize = 256 * 1024;

    private readonly ILogger<RemoteFileSystem> _logger;
    private volatile string? _host;

    public RemoteFileSystem(ILogger<RemoteFileSystem> logger) => _logger = logger;

    public void SetTarget(string? host)
    {
        _host = string.IsNullOrWhiteSpace(host) ? null : host.Trim();
        _logger.LogInformation("Remote file bridge target: {Host}", _host ?? "(none)");
    }

    /// <summary>Maps a remote path onto what this machine can reach. UNC input passes through.</summary>
    internal string MapToBridge(string remotePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(remotePath);
        var path = remotePath.Trim();
        if (path.StartsWith(@"\\", StringComparison.Ordinal)) return path;

        if (path.Length >= 2 && char.IsAsciiLetter(path[0]) && path[1] == ':')
        {
            var host = _host ?? throw new InvalidOperationException("Select a machine first.");
            var rest = path.Length > 2 ? path[2..].TrimStart('\\') : string.Empty;
            var root = $@"\\{host}\{char.ToUpperInvariant(path[0])}$";
            return rest.Length == 0 ? root : $@"{root}\{rest}";
        }
        throw new ArgumentException($"'{remotePath}' is not an absolute remote path.", nameof(remotePath));
    }

    public Task<IReadOnlyList<FileSystemEntry>> ListDirectoryAsync(string path, CancellationToken ct) =>
        Task.Run<IReadOnlyList<FileSystemEntry>>(() =>
        {
            var mapped = MapToBridge(path);
            var entries = new List<FileSystemEntry>();
            try
            {
                // No Exists() pre-check: it swallows the real SMB error (wrong password, no
                // admin rights, port blocked all look like "false"). Enumerate directly and
                // diagnose on failure instead. UnauthorizedAccessException flows through
                // untouched — that is the pane's Access-denied state.
                foreach (var info in new DirectoryInfo(mapped).EnumerateFileSystemInfos("*", LocalFileSystem.ListingOptions))
                {
                    ct.ThrowIfCancellationRequested();
                    // Report the *remote-shaped* path back to the UI, not the bridge path.
                    var entry = LocalFileSystem.ToEntry(info);
                    entries.Add(entry with { FullPath = UnmapFromBridge(entry.FullPath, path) });
                }
            }
            catch (Exception ex) when (ex is DirectoryNotFoundException or IOException)
            {
                throw new DirectoryNotFoundException(DescribeUnreachable(path, mapped), ex);
            }
            return LocalFileSystem.SortListing(entries);
        }, ct);

    /// <summary>
    /// Figures out WHY a bridge path failed: probes the share root so the message carries the
    /// actual Windows error (logon failure, port unreachable, …) instead of a generic not-found.
    /// </summary>
    private string DescribeUnreachable(string remotePath, string mappedPath)
    {
        var root = ShareRootOf(mappedPath);
        try
        {
            _ = Directory.EnumerateFileSystemEntries(root).Any(); // forces a real SMB round-trip
            // The share itself answers — the specific folder just isn't there.
            return $"{remotePath} doesn't exist on the remote ({root} is reachable). " +
                   "Note: the profile folder name can differ from the login name.";
        }
        catch (Exception rootEx)
        {
            _logger.LogWarning(rootEx, "Share root {Root} is unreachable", root);
            return $"Can't open {root}: {rootEx.Message} " +
                   "Checklist: save the machine's password in Edit (SMB sign-in), the account needs admin rights " +
                   "on the remote for C$, SMB port 445 must be reachable, and workgroup machines need " +
                   "LocalAccountTokenFilterPolicy=1 for admin shares.";
        }
    }

    /// <summary>"\\host\C$\Users\x" → "\\host\C$".</summary>
    private static string ShareRootOf(string uncPath)
    {
        var parts = uncPath.TrimStart('\\').Split('\\');
        return parts.Length >= 2 ? $@"\\{parts[0]}\{parts[1]}" : uncPath;
    }

    public Task<FileSystemEntry?> StatAsync(string path, CancellationToken ct) =>
        Task.Run<FileSystemEntry?>(() =>
        {
            var mapped = MapToBridge(path);
            var file = new FileInfo(mapped);
            if (file.Exists) return LocalFileSystem.ToEntry(file) with { FullPath = path };
            var dir = new DirectoryInfo(mapped);
            return dir.Exists ? LocalFileSystem.ToEntry(dir) with { FullPath = path } : null;
        }, ct);

    public Task CreateDirectoryAsync(string path, CancellationToken ct) =>
        Task.Run(() => Directory.CreateDirectory(MapToBridge(path)), ct);

    public Task DeleteAsync(string path, CancellationToken ct) =>
        Task.Run(() =>
        {
            var mapped = MapToBridge(path);
            if (Directory.Exists(mapped)) Directory.Delete(mapped, recursive: true);
            else if (File.Exists(mapped)) File.Delete(mapped);
            else throw new FileNotFoundException($"Nothing to delete at {path}", path);
        }, ct);

    public Task RenameAsync(string path, string newName, CancellationToken ct) =>
        Task.Run(() =>
        {
            var mapped = MapToBridge(path);
            var parent = Path.GetDirectoryName(mapped)
                ?? throw new IOException($"Cannot rename a root: {path}");
            var target = Path.Combine(parent, newName);
            if (Directory.Exists(mapped)) Directory.Move(mapped, target);
            else if (File.Exists(mapped)) File.Move(mapped, target);
            else throw new FileNotFoundException($"Nothing to rename at {path}", path);
        }, ct);

    public async Task CopyInAsync(Stream source, string remotePath, IProgress<long>? progress, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(source);
        var mapped = MapToBridge(remotePath);
        var destination = new FileStream(
            mapped, FileMode.Create, FileAccess.Write, FileShare.None,
            CopyBufferSize, FileOptions.Asynchronous);
        try
        {
            await using (destination.ConfigureAwait(false))
            {
                await PumpAsync(source, destination, progress, ct).ConfigureAwait(false);
            }
        }
        catch
        {
            DeletePartial(mapped);
            throw;
        }
    }

    public async Task CopyOutAsync(string remotePath, Stream destination, IProgress<long>? progress, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(destination);
        var mapped = MapToBridge(remotePath);
        var source = new FileStream(
            mapped, FileMode.Open, FileAccess.Read, FileShare.Read,
            CopyBufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using (source.ConfigureAwait(false))
        {
            await PumpAsync(source, destination, progress, ct).ConfigureAwait(false);
        }
    }

    /// <summary>The chunked copy loop: every buffer-full is written, counted and reported.</summary>
    private static async Task PumpAsync(Stream source, Stream destination, IProgress<long>? progress, CancellationToken ct)
    {
        var buffer = new byte[CopyBufferSize];
        long total = 0;
        int read;
        while ((read = await source.ReadAsync(buffer.AsMemory(), ct).ConfigureAwait(false)) > 0)
        {
            await destination.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
            total += read;
            progress?.Report(total);
        }
        await destination.FlushAsync(ct).ConfigureAwait(false);
    }

    private void DeletePartial(string mappedPath)
    {
        try
        {
            if (File.Exists(mappedPath)) File.Delete(mappedPath);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not remove partial file {Path}", mappedPath);
        }
    }

    /// <summary>Best-effort inverse mapping so listings show remote-style paths in the UI.</summary>
    private string UnmapFromBridge(string bridgePath, string requestedRemoteDirectory)
    {
        var mappedDir = MapToBridge(requestedRemoteDirectory);
        if (bridgePath.StartsWith(mappedDir, StringComparison.OrdinalIgnoreCase))
        {
            var suffix = bridgePath[mappedDir.Length..].TrimStart('\\');
            return suffix.Length == 0 ? requestedRemoteDirectory : TransferPath.Join(requestedRemoteDirectory, suffix);
        }
        return bridgePath;
    }
}
