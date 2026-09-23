using RdpManager.Application.Common;
using RdpManager.Application.Files;
using RdpManager.Domain.Abstractions;

namespace RdpManager.Application.Tests.Files;

/// <summary>A clock the tests move by hand (throughput/ETA need elapsed time).</summary>
internal sealed class MutableClock : IClock
{
    public DateTimeOffset Now = DateTimeOffset.UnixEpoch;
    /// <summary>When set, every read advances the clock — simulates time passing during a copy.</summary>
    public TimeSpan AdvancePerRead = TimeSpan.Zero;

    public DateTimeOffset UtcNow
    {
        get
        {
            var value = Now;
            Now = Now.Add(AdvancePerRead);
            return value;
        }
    }
}

/// <summary>Shared in-memory "disk" behavior for both fake endpoints. Paths are opaque strings.</summary>
internal abstract class FakeFileSystemBase : IFileSystemBrowser
{
    public IPathModel PathModel { get; set; } = WindowsPathModel.Instance;

    public readonly Dictionary<string, byte[]> Files = new(StringComparer.OrdinalIgnoreCase);
    public readonly HashSet<string> Directories = new(StringComparer.OrdinalIgnoreCase);
    public readonly HashSet<string> DeniedPaths = new(StringComparer.OrdinalIgnoreCase);

    public void AddFile(string path, byte[] content)
    {
        Files[path] = content;
        var parent = TransferPath.GetParent(path);
        if (parent is not null) Directories.Add(parent);
    }

    public void AddFile(string path, int size) => AddFile(path, CreatePattern(size));

    public static byte[] CreatePattern(int size)
    {
        var bytes = new byte[size];
        for (var i = 0; i < size; i++) bytes[i] = (byte)(i % 251);
        return bytes;
    }

    protected void ThrowIfDenied(string path)
    {
        if (DeniedPaths.Contains(path)) throw new UnauthorizedAccessException($"Access to {path} denied.");
    }

    public Task<IReadOnlyList<FileSystemEntry>> ListDirectoryAsync(string path, CancellationToken ct)
    {
        ThrowIfDenied(path);
        if (!Directories.Contains(path)) throw new DirectoryNotFoundException(path);
        var entries = Files.Keys
            .Where(f => string.Equals(TransferPath.GetParent(f), path, StringComparison.OrdinalIgnoreCase))
            .Select(f => Entry(f))
            .ToList();
        return Task.FromResult<IReadOnlyList<FileSystemEntry>>(entries);
    }

    public Task<FileSystemEntry?> StatAsync(string path, CancellationToken ct)
    {
        ThrowIfDenied(path);
        if (Files.ContainsKey(path)) return Task.FromResult<FileSystemEntry?>(Entry(path));
        if (Directories.Contains(path))
            return Task.FromResult<FileSystemEntry?>(new FileSystemEntry(TransferPath.GetFileName(path), path, true, 0, null));
        return Task.FromResult<FileSystemEntry?>(null);
    }

    public Task CreateDirectoryAsync(string path, CancellationToken ct)
    {
        ThrowIfDenied(path);
        Directories.Add(path);
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string path, CancellationToken ct)
    {
        ThrowIfDenied(path);
        if (!Files.Remove(path) && !Directories.Remove(path)) throw new FileNotFoundException(path);
        return Task.CompletedTask;
    }

    public Task RenameAsync(string path, string newName, CancellationToken ct)
    {
        ThrowIfDenied(path);
        if (!Files.Remove(path, out var content)) throw new FileNotFoundException(path);
        var parent = TransferPath.GetParent(path) ?? throw new IOException("no parent");
        Files[TransferPath.Join(parent, newName)] = content;
        return Task.CompletedTask;
    }

    private FileSystemEntry Entry(string path) =>
        new(TransferPath.GetFileName(path), path, false, Files[path].Length, null);
}

internal sealed class FakeLocalFileSystem : FakeFileSystemBase, ILocalFileSystem
{
    public string DefaultDirectory => @"C:\Users\me";

    public Task<IReadOnlyList<FileSystemEntry>> ListRootsAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<FileSystemEntry>>(new[]
        {
            new FileSystemEntry("C:", @"C:\", true, 0, null),
            new FileSystemEntry("D:", @"D:\", true, 0, null),
        });

    public Task<Stream> OpenReadAsync(string path, CancellationToken ct)
    {
        ThrowIfDenied(path);
        if (!Files.TryGetValue(path, out var content)) throw new FileNotFoundException(path);
        return Task.FromResult<Stream>(new MemoryStream(content, writable: false));
    }

    public Task<Stream> OpenWriteAsync(string path, CancellationToken ct)
    {
        ThrowIfDenied(path);
        return Task.FromResult<Stream>(new CommitOnDisposeStream(bytes => AddFile(path, bytes)));
    }

    /// <summary>MemoryStream that hands its bytes to the fake "disk" when the writer disposes it.</summary>
    private sealed class CommitOnDisposeStream : MemoryStream
    {
        private readonly Action<byte[]> _commit;
        private bool _committed;
        public CommitOnDisposeStream(Action<byte[]> commit) => _commit = commit;

        protected override void Dispose(bool disposing)
        {
            if (disposing && !_committed) { _committed = true; _commit(ToArray()); }
            base.Dispose(disposing);
        }
    }
}

internal sealed class FakeRemoteFileSystem : FakeFileSystemBase, IRemoteFileSystem
{
    public RemoteConnection? Target;
    public string Home = @"C:\Users\me";
    public void SetTarget(RemoteConnection? connection, System.Security.SecureString? secret) => Target = connection;
    public Task<string?> GetHomeDirectoryAsync(CancellationToken ct) => Task.FromResult<string?>(Home);

    /// <summary>When set, TryConnectAsync reports this instead of connecting (sign-in/reachability tests).</summary>
    public Error? ConnectError;
    public Task<Result<string?>> TryConnectAsync(CancellationToken ct) => Task.FromResult(
        ConnectError is null ? Result<string?>.Success(Home) : Result<string?>.Failure(ConnectError));

    public int ChunkSize = 4;
    /// <summary>Copies that should fail with "Connection reset" before one succeeds (retry tests).</summary>
    public int FailuresBeforeSuccess;
    /// <summary>When set, copies pause after the first chunk until released (cancel tests).</summary>
    public TaskCompletionSource? Hold;
    public readonly List<string> CopyLog = new();

    public async Task CopyInAsync(Stream source, string remotePath, IProgress<long>? progress, CancellationToken ct)
    {
        CopyLog.Add(remotePath);
        ThrowIfDenied(remotePath);
        if (FailuresBeforeSuccess > 0)
        {
            FailuresBeforeSuccess--;
            throw new IOException("Connection reset");
        }

        using var collected = new MemoryStream();
        var buffer = new byte[ChunkSize];
        long total = 0;
        var first = true;
        int read;
        while ((read = await source.ReadAsync(buffer.AsMemory(), ct)) > 0)
        {
            collected.Write(buffer, 0, read);
            total += read;
            progress?.Report(total);
            if (first && Hold is not null) { first = false; await Hold.Task.WaitAsync(ct); }
        }
        AddFile(remotePath, collected.ToArray());
    }

    public async Task CopyOutAsync(string remotePath, Stream destination, IProgress<long>? progress, CancellationToken ct)
    {
        CopyLog.Add(remotePath);
        ThrowIfDenied(remotePath);
        if (FailuresBeforeSuccess > 0)
        {
            FailuresBeforeSuccess--;
            throw new IOException("Connection reset");
        }
        if (!Files.TryGetValue(remotePath, out var content)) throw new FileNotFoundException(remotePath);

        long total = 0;
        var first = true;
        for (var offset = 0; offset < content.Length; offset += ChunkSize)
        {
            ct.ThrowIfCancellationRequested();
            var slice = Math.Min(ChunkSize, content.Length - offset);
            await destination.WriteAsync(content.AsMemory(offset, slice), ct);
            total += slice;
            progress?.Report(total);
            if (first && Hold is not null) { first = false; await Hold.Task.WaitAsync(ct); }
        }
    }
}
