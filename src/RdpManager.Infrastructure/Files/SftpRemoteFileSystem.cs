using Microsoft.Extensions.Logging;
using RdpManager.Application.Common;
using RdpManager.Application.Files;
using Renci.SshNet;
using Renci.SshNet.Common;
using Renci.SshNet.Sftp;

namespace RdpManager.Infrastructure.Files;

/// <summary>
/// <see cref="IRemoteFileSystem"/> over SFTP (WinSCP-style), using SSH.NET. Connects as the
/// machine's own user with its stored password and lands in the server-reported home directory —
/// no admin shares, no drive redirection, no path guessing. Paths are POSIX (<c>/</c>). Copies
/// stream in 256 KiB chunks with live progress and cancellation, and never leave a partial file.
/// </summary>
public sealed class SftpRemoteFileSystem : IRemoteFileSystem, IDisposable
{
    private const int CopyBufferSize = 256 * 1024;

    private readonly ILogger<SftpRemoteFileSystem> _logger;
    private readonly object _gate = new();

    private SftpClient? _client;
    private RemoteConnection? _target;
    private byte[]? _password; // transient; cleared on disconnect

    public SftpRemoteFileSystem(ILogger<SftpRemoteFileSystem> logger) => _logger = logger;

    public IPathModel PathModel => PosixPathModel.Instance;

    public void SetTarget(RemoteConnection? connection, System.Security.SecureString? secret)
    {
        // Called on the UI thread. Must NOT block — swap fields under the lock, then tear the old
        // session down on a background thread (Disconnect/Dispose can block for seconds).
        SftpClient? old;
        byte[]? oldPassword;
        lock (_gate)
        {
            old = _client;
            oldPassword = _password;
            _client = null;
            _target = connection;
            _password = connection is null ? null : ToBytes(secret);
        }

        if (oldPassword is not null) Array.Clear(oldPassword);
        if (old is not null) _ = Task.Run(() => SafeDispose(old));

        _logger.LogInformation("SFTP target: {Target}",
            connection is null ? "(none)" : $"{connection.Username}@{connection.Host}:{connection.Port}");
    }

    public Task<string?> GetHomeDirectoryAsync(CancellationToken ct) =>
        Task.Run<string?>(() =>
        {
            var client = EnsureConnected();
            // After connect, the working directory is the user's home (WinSCP behavior).
            return client.WorkingDirectory;
        }, ct);

    public Task<IReadOnlyList<FileSystemEntry>> ListDirectoryAsync(string path, CancellationToken ct) =>
        Task.Run<IReadOnlyList<FileSystemEntry>>(() =>
        {
            var client = EnsureConnected();

            // OpenSSH-for-Windows + SSH.NET can throw "Bad message" (SSH_FX_BAD_MESSAGE) on
            // RequestOpenDir for some path forms. Try the forms a server may prefer, in order,
            // and use whichever opens. Each candidate's own canonical WorkingDirectory (after a
            // successful ChangeDirectory) gives us correct child paths.
            var (baseDir, items) = OpenDirectoryWithFallback(client, path);

            var entries = new List<FileSystemEntry>();
            foreach (var item in items)
            {
                ct.ThrowIfCancellationRequested();
                if (item.Name is "." or "..") continue;
                if (item.Name.StartsWith('.')) continue; // hidden dotfiles, matching Explorer's spirit
                entries.Add(ToEntry(item, PosixPath.Join(baseDir, item.Name)));
            }
            return LocalFileSystem.SortListing(entries);
        }, ct);

    public Task<FileSystemEntry?> StatAsync(string path, CancellationToken ct) =>
        Task.Run<FileSystemEntry?>(() =>
        {
            var client = EnsureConnected();
            try
            {
                return ToEntry(client.Get(path));
            }
            catch (SftpPathNotFoundException)
            {
                return null;
            }
        }, ct);

    public Task CreateDirectoryAsync(string path, CancellationToken ct) =>
        Task.Run(() => EnsureConnected().CreateDirectory(path), ct);

    public Task DeleteAsync(string path, CancellationToken ct) =>
        Task.Run(() =>
        {
            var client = EnsureConnected();
            var attrs = client.Get(path);
            if (attrs.IsDirectory) DeleteDirectoryRecursive(client, path);
            else client.DeleteFile(path);
        }, ct);

    public Task RenameAsync(string path, string newName, CancellationToken ct) =>
        Task.Run(() =>
        {
            var client = EnsureConnected();
            var parent = PosixPath.GetParent(path) ?? "/";
            client.RenameFile(path, PosixPath.Join(parent, newName));
        }, ct);

    public async Task CopyInAsync(Stream source, string remotePath, IProgress<long>? progress, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(source);
        var client = EnsureConnected();
        try
        {
            await PumpUploadAsync(client, source, remotePath, progress, ct).ConfigureAwait(false);
        }
        catch
        {
            TryDelete(client, remotePath);
            throw;
        }
    }

    public Task CopyOutAsync(string remotePath, Stream destination, IProgress<long>? progress, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(destination);
        return PumpDownloadAsync(EnsureConnected(), remotePath, destination, progress, ct);
    }

    // ── Chunked copy loops (SSH.NET's stream APIs are synchronous; run on a worker) ──

    private static Task PumpUploadAsync(SftpClient client, Stream source, string remotePath, IProgress<long>? progress, CancellationToken ct) =>
        Task.Run(() =>
        {
            using var remote = client.Create(remotePath);
            var buffer = new byte[CopyBufferSize];
            long total = 0;
            int read;
            while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
            {
                ct.ThrowIfCancellationRequested();
                remote.Write(buffer, 0, read);
                total += read;
                progress?.Report(total);
            }
            remote.Flush();
        }, ct);

    private static Task PumpDownloadAsync(SftpClient client, string remotePath, Stream destination, IProgress<long>? progress, CancellationToken ct) =>
        Task.Run(() =>
        {
            using var remote = client.OpenRead(remotePath);
            var buffer = new byte[CopyBufferSize];
            long total = 0;
            int read;
            while ((read = remote.Read(buffer, 0, buffer.Length)) > 0)
            {
                ct.ThrowIfCancellationRequested();
                destination.Write(buffer, 0, read);
                total += read;
                progress?.Report(total);
            }
            destination.Flush();
        }, ct);

    // ── Connection lifecycle ─────────────────────────────────────────────────

    /// <summary>
    /// Connects (or reuses the open session) and reports expected outcomes - no saved password,
    /// a rejected sign-in, an unreachable host - as an <see cref="Error"/>. Nothing here throws
    /// for those: they are everyday results of picking a machine, and raising an exception for
    /// them broke every debugger session and buried the real reason under a stack trace.
    /// </summary>
    private (SftpClient? Client, Error? Error) TryConnectCore()
    {
        // Runs off the UI thread (callers wrap in Task.Run). Snapshot the target under the lock,
        // but do the blocking Connect() OUTSIDE it so a UI-thread SetTarget never waits on us.
        RemoteConnection target;
        byte[] passwordBytes;
        lock (_gate)
        {
            if (_client is { IsConnected: true }) return (_client, null);
            if (_target is null) return (null, Error.Validation("Select a machine first."));
            target = _target;
            if (_password is null)
                return (null, new Error(ErrorKind.NeedsReconfiguration, "credentials_missing",
                    $"No saved password for {target.Username}@{target.Host}. Add it in Edit so Deskpin can sign in over SFTP."));
            passwordBytes = _password;
        }

        var password = System.Text.Encoding.Unicode.GetString(passwordBytes);
        SftpClient client;
        try
        {
            client = new SftpClient(target.Host, target.Port, target.Username, password)
            {
                // Fail fast instead of the OS's ~20s SYN timeout when nothing is listening.
                ConnectionInfo = { Timeout = TimeSpan.FromSeconds(8) },
            };
            client.Connect();
        }
        catch (Renci.SshNet.Common.SshAuthenticationException ex)
        {
            // SSH.NET's own message is the only thing that tells the two failures apart:
            // "Permission denied (password)" = the password is wrong, while "No suitable
            // authentication method found (publickey,keyboard-interactive)" = the server
            // won't take the password method at all and no password will ever work.
            // The Error carries the friendly line; the log keeps the server's own words.
            _logger.LogWarning(ex, "SFTP auth rejected: {User}@{Host}:{Port}",
                target.Username, target.Host, target.Port);
            return (null, new Error(ErrorKind.PermissionDenied, "permission_denied",
                $"SFTP sign-in to {target.Host} was rejected — check {target.Username}'s password in Edit."));
        }
        catch (Exception ex) when (ex is System.Net.Sockets.SocketException
                                      or Renci.SshNet.Common.SshConnectionException
                                      or Renci.SshNet.Common.SshOperationTimeoutException)
        {
            _logger.LogWarning(ex, "SFTP unreachable: {Host}:{Port}", target.Host, target.Port);
            return (null, new Error(ErrorKind.Unreachable, "unreachable",
                $"No SSH/SFTP server answered at {target.Host}:{target.Port}. " +
                "On Windows, enable OpenSSH Server (right-click the machine → “Enable file access” for the command); " +
                "on Linux/macOS make sure the SSH service is running and the port is open."));
        }
        finally
        {
            password = string.Empty; // best effort: drop the managed plaintext copy
        }

        lock (_gate)
        {
            // A newer SetTarget may have swapped the target while we were connecting — if so,
            // this session is stale: drop it and let the newer selection reconnect.
            if (!ReferenceEquals(_target, target))
            {
                _ = Task.Run(() => SafeDispose(client));
                return (null, Error.Cancelled());
            }
            _client = client;
            _logger.LogInformation("SFTP connected: {User}@{Host}:{Port} (home {Home})",
                target.Username, target.Host, target.Port, client.WorkingDirectory);
            return (client, null);
        }
    }

    public Task<Result<string?>> TryConnectAsync(CancellationToken ct) =>
        Task.Run(() =>
        {
            var (client, error) = TryConnectCore();
            return client is null
                ? Result<string?>.Failure(error!)
                : Result<string?>.Success(client.WorkingDirectory);
        }, ct);

    /// <summary>
    /// The exception-throwing face of <see cref="TryConnectCore"/>, kept for the browsing and
    /// transfer operations that still signal through exceptions. Those all run after a session
    /// is already open, so in practice this only throws when one drops mid-task.
    /// </summary>
    private SftpClient EnsureConnected()
    {
        var (client, error) = TryConnectCore();
        if (client is not null) return client;

        throw error!.Kind switch
        {
            ErrorKind.NeedsReconfiguration => new RemoteCredentialsMissingException(error.Message),
            ErrorKind.PermissionDenied => new UnauthorizedAccessException(error.Message),
            ErrorKind.Unreachable => new RemoteUnreachableException(error.Message),
            ErrorKind.Validation => (Exception)new InvalidOperationException(error.Message),
            _ => new IOException(error.Message),
        };
    }

    private static void SafeDispose(SftpClient client)
    {
        try { if (client.IsConnected) client.Disconnect(); } catch (Exception) { /* ignore */ }
        try { client.Dispose(); } catch (Exception) { /* ignore */ }
    }

    public void Dispose()
    {
        SftpClient? old;
        lock (_gate) { old = _client; _client = null; if (_password is not null) Array.Clear(_password); _password = null; }
        if (old is not null) SafeDispose(old);
    }

    /// <summary>
    /// Opens a directory across the path forms OpenSSH servers disagree on, returning the
    /// server-canonical base path plus its entries. Tries: ChangeDirectory + list("."),
    /// then the absolute path, then the drive form without a leading slash (Windows OpenSSH).
    /// </summary>
    private (string baseDir, IEnumerable<ISftpFile> items) OpenDirectoryWithFallback(SftpClient client, string path)
    {
        var candidates = new List<string> { path };
        // "/C:/Users/x" → "C:/Users/x" (Windows OpenSSH sometimes wants no leading slash)
        if (path.Length >= 4 && path[0] == '/' && char.IsAsciiLetter(path[1]) && path[2] == ':' && path[3] == '/')
            candidates.Add(path[1..]);

        Exception? last = null;
        foreach (var candidate in candidates)
        {
            try
            {
                client.ChangeDirectory(candidate);
                var baseDir = client.WorkingDirectory;
                var items = client.ListDirectory(".").ToList(); // materialize so a lazy failure surfaces here
                _logger.LogInformation("SFTP listed {Count} entries in {Base} (input {Input})", items.Count, baseDir, path);
                return (baseDir, items);
            }
            catch (Exception ex)
            {
                last = ex;
                _logger.LogWarning("SFTP open failed for candidate '{Candidate}': {Message}", candidate, ex.Message);
                try
                {
                    // Some servers accept a direct absolute list even when ChangeDirectory/'.' didn't.
                    var items = client.ListDirectory(candidate).ToList();
                    _logger.LogInformation("SFTP listed {Count} entries via absolute '{Candidate}'", items.Count, candidate);
                    return (candidate.TrimEnd('/'), items);
                }
                catch (Exception ex2)
                {
                    last = ex2;
                    _logger.LogWarning("SFTP absolute list also failed for '{Candidate}': {Message}", candidate, ex2.Message);
                }
            }
        }
        throw new IOException(
            $"The remote server refused to open '{path}'. It connected, but rejected the directory request " +
            $"(last error: {last?.Message}). This is usually an SFTP-server quirk on this host.", last);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static void DeleteDirectoryRecursive(SftpClient client, string path)
    {
        foreach (var item in client.ListDirectory(path))
        {
            if (item.Name is "." or "..") continue;
            if (item.IsDirectory) DeleteDirectoryRecursive(client, item.FullName);
            else client.DeleteFile(item.FullName);
        }
        client.DeleteDirectory(path);
    }

    private static void TryDelete(SftpClient client, string path)
    {
        try { if (client.Exists(path)) client.DeleteFile(path); }
        catch (Exception) { /* best effort cleanup of a partial upload */ }
    }

    private static FileSystemEntry ToEntry(ISftpFile file, string? fullPathOverride = null) =>
        new(file.Name, fullPathOverride ?? file.FullName, file.IsDirectory, file.IsDirectory ? 0 : file.Length,
            new DateTimeOffset(file.LastWriteTimeUtc, TimeSpan.Zero));

    private static byte[]? ToBytes(System.Security.SecureString? secret)
    {
        if (secret is null) return null;
        var ptr = System.Runtime.InteropServices.Marshal.SecureStringToGlobalAllocUnicode(secret);
        try
        {
            var bytes = new byte[secret.Length * 2];
            System.Runtime.InteropServices.Marshal.Copy(ptr, bytes, 0, bytes.Length);
            return bytes;
        }
        finally
        {
            System.Runtime.InteropServices.Marshal.ZeroFreeGlobalAllocUnicode(ptr);
        }
    }
}
