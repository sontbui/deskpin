namespace RdpManager.Application.Files;

/// <summary>One row in a directory listing — file or folder, local or remote.</summary>
public sealed record FileSystemEntry(
    string Name,
    string FullPath,
    bool IsDirectory,
    long SizeBytes,
    DateTimeOffset? ModifiedAt);

/// <summary>
/// Browsing operations shared by both sides of the commander. Implementations do real I/O
/// (Infrastructure); the Application layer only orchestrates over this surface, which is what
/// keeps <see cref="FileTransferUseCase"/> pure and unit-testable.
/// Implementations throw the standard System.IO exception types (UnauthorizedAccessException,
/// DirectoryNotFoundException, FileNotFoundException, IOException); the use case maps them to
/// <see cref="Common.Error"/>s so ViewModels never see raw exceptions.
/// </summary>
public interface IFileSystemBrowser
{
    /// <summary>Path rules for this endpoint (Windows for local, POSIX for an SFTP host).</summary>
    IPathModel PathModel { get; }

    Task<IReadOnlyList<FileSystemEntry>> ListDirectoryAsync(string path, CancellationToken ct);

    /// <summary>Returns the entry at <paramref name="path"/>, or null when nothing exists there.</summary>
    Task<FileSystemEntry?> StatAsync(string path, CancellationToken ct);

    Task CreateDirectoryAsync(string path, CancellationToken ct);
    Task DeleteAsync(string path, CancellationToken ct);

    /// <summary>Renames the file/folder at <paramref name="path"/> to <paramref name="newName"/> (bare name, same directory).</summary>
    Task RenameAsync(string path, string newName, CancellationToken ct);
}

/// <summary>The user's own machine ("This PC" pane). Implemented over System.IO.</summary>
public interface ILocalFileSystem : IFileSystemBrowser
{
    /// <summary>Where the local pane opens by default (the user profile directory).</summary>
    string DefaultDirectory { get; }

    /// <summary>Ready local drive roots (C:\, D:\, …) for breadcrumb "This PC" and the redirection dialog.</summary>
    Task<IReadOnlyList<FileSystemEntry>> ListRootsAsync(CancellationToken ct);

    /// <summary>Opens a local file for chunk-wise reading (the source of an upload).</summary>
    Task<Stream> OpenReadAsync(string path, CancellationToken ct);

    /// <summary>Creates/truncates a local file for writing (the destination of a download).</summary>
    Task<Stream> OpenWriteAsync(string path, CancellationToken ct);
}

/// <summary>Where to reach a remote host for file transfer (SFTP: host + SSH port + username).</summary>
public sealed record RemoteConnection(string Host, int Port, string Username);

/// <summary>
/// The remote host's filesystem over SFTP (WinSCP-style): connect as the machine's own user and
/// land in their home — no admin shares, no drive redirection. Copy-in/copy-out stream in chunks
/// and report cumulative bytes so the UI shows live progress.
/// </summary>
public interface IRemoteFileSystem : IFileSystemBrowser
{
    /// <summary>
    /// Opens (or replaces) the SFTP session to a host using the supplied secret. Null clears it.
    /// The secret is used only to authenticate and is not retained in plaintext.
    /// </summary>
    void SetTarget(RemoteConnection? connection, System.Security.SecureString? secret);

    /// <summary>The user's home directory as reported by the server after connecting (null if not connected).</summary>
    Task<string?> GetHomeDirectoryAsync(CancellationToken ct);

    /// <summary>Upload: writes <paramref name="source"/> to <paramref name="remotePath"/>, replacing any existing file.</summary>
    Task CopyInAsync(Stream source, string remotePath, IProgress<long>? progress, CancellationToken ct);

    /// <summary>Download: reads <paramref name="remotePath"/> into <paramref name="destination"/>.</summary>
    Task CopyOutAsync(string remotePath, Stream destination, IProgress<long>? progress, CancellationToken ct);
}
