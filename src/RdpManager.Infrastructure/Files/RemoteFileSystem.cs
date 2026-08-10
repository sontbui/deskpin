using Microsoft.Extensions.Logging;
using RdpManager.Application.Files;

namespace RdpManager.Infrastructure.Files;

/// <summary>Where the drive-redirection bridge is mounted. Default is mstsc's <c>\\tsclient</c> share.</summary>
public sealed record RemoteFileSystemOptions
{
    public string UncRoot { get; init; } = @"\\tsclient";
}

/// <summary>
/// <see cref="IRemoteFileSystem"/> over the RDP redirected-drive UNC bridge: a remote path like
/// <c>C:\Users\svc</c> maps to <c>&lt;UncRoot&gt;\C\Users\svc</c> (the way mstsc exposes redirected
/// drives as <c>\\tsclient\C</c>). Copies stream in 256 KiB chunks and report cumulative bytes
/// through <see cref="IProgress{T}"/> so the transfer dock shows live progress, and honor the
/// <see cref="CancellationToken"/> between chunks. A cancelled or failed copy removes its partial
/// destination file — no half-written files are left behind.
/// </summary>
public sealed class RemoteFileSystem : IRemoteFileSystem
{
    private const int CopyBufferSize = 256 * 1024;

    private readonly RemoteFileSystemOptions _options;
    private readonly ILogger<RemoteFileSystem> _logger;

    public RemoteFileSystem(RemoteFileSystemOptions options, ILogger<RemoteFileSystem> logger)
    {
        _options = options;
        _logger = logger;
    }

    /// <summary>Maps a remote drive path onto the redirection bridge. UNC input passes through untouched.</summary>
    internal string MapToBridge(string remotePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(remotePath);
        var path = remotePath.Trim();
        if (path.StartsWith(@"\\", StringComparison.Ordinal)) return path;
        if (path.Length >= 2 && char.IsAsciiLetter(path[0]) && path[1] == ':')
        {
            var rest = path.Length > 2 ? path[2..].TrimStart('\\') : string.Empty;
            var root = $@"{_options.UncRoot}\{char.ToUpperInvariant(path[0])}";
            return rest.Length == 0 ? root : $@"{root}\{rest}";
        }
        throw new ArgumentException($"'{remotePath}' is not an absolute remote path.", nameof(remotePath));
    }

    public Task<IReadOnlyList<FileSystemEntry>> ListDirectoryAsync(string path, CancellationToken ct) =>
        Task.Run<IReadOnlyList<FileSystemEntry>>(() =>
        {
            var mapped = MapToBridge(path);
            var dir = new DirectoryInfo(mapped);
            if (!dir.Exists) throw new DirectoryNotFoundException($"Directory not found: {path}");

            var entries = new List<FileSystemEntry>();
            foreach (var info in dir.EnumerateFileSystemInfos("*", LocalFileSystem.ListingOptions))
            {
                ct.ThrowIfCancellationRequested();
                // Report the *remote-shaped* path back to the UI, not the bridge path.
                var entry = LocalFileSystem.ToEntry(info);
                entries.Add(entry with { FullPath = UnmapFromBridge(entry.FullPath, path) });
            }
            return LocalFileSystem.SortListing(entries);
        }, ct);

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
