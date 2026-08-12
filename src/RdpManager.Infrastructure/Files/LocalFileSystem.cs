using RdpManager.Application.Files;

namespace RdpManager.Infrastructure.Files;

/// <summary>
/// <see cref="ILocalFileSystem"/> over System.IO. All streams are opened asynchronous so the
/// copy loop sees live bytes. Exceptions (UnauthorizedAccessException &amp; friends) bubble up
/// untouched — the Application layer maps them to friendly errors.
/// </summary>
public sealed class LocalFileSystem : ILocalFileSystem
{
    internal const int StreamBufferSize = 256 * 1024;

    /// <summary>
    /// Listing behavior shared by both panes: skip hidden/system entries (the "Documents and
    /// Settings"-style junctions on a drive root deny access to everyone — Explorer hides them
    /// too), and don't let one unreadable child abort the whole listing. Direct navigation into
    /// a denied directory still throws, which is what feeds the friendly access-denied pane.
    /// </summary>
    internal static readonly EnumerationOptions ListingOptions = new()
    {
        IgnoreInaccessible = true,
        // Skip only Hidden — matching Explorer's default. NOT System: user-profile special
        // folders (Desktop, Documents, Downloads, …) are marked System (for their custom icons)
        // but not Hidden, so filtering System would wrongly show a full profile as empty. The
        // drive-root junk ($Recycle.Bin, "Documents and Settings", System Volume Information) is
        // Hidden+System, so the Hidden skip still hides all of it.
        AttributesToSkip = FileAttributes.Hidden,
        RecurseSubdirectories = false,
    };

    public IPathModel PathModel => WindowsPathModel.Instance;

    public string DefaultDirectory =>
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile, Environment.SpecialFolderOption.DoNotVerify);

    public Task<IReadOnlyList<FileSystemEntry>> ListRootsAsync(CancellationToken ct) =>
        Task.Run<IReadOnlyList<FileSystemEntry>>(() =>
            DriveInfo.GetDrives()
                .Where(d => d.IsReady)
                .Select(d => new FileSystemEntry(
                    d.Name.TrimEnd('\\'), d.Name, IsDirectory: true, SizeBytes: 0,
                    ModifiedAt: null))
                .ToList(), ct);

    public Task<IReadOnlyList<FileSystemEntry>> ListDirectoryAsync(string path, CancellationToken ct) =>
        Task.Run<IReadOnlyList<FileSystemEntry>>(() =>
        {
            var dir = new DirectoryInfo(path);
            if (!dir.Exists) throw new DirectoryNotFoundException($"Directory not found: {path}");

            var entries = new List<FileSystemEntry>();
            foreach (var info in dir.EnumerateFileSystemInfos("*", ListingOptions))
            {
                ct.ThrowIfCancellationRequested();
                entries.Add(ToEntry(info));
            }
            return SortListing(entries);
        }, ct);

    public Task<FileSystemEntry?> StatAsync(string path, CancellationToken ct) =>
        Task.Run<FileSystemEntry?>(() =>
        {
            var file = new FileInfo(path);
            if (file.Exists) return ToEntry(file);
            var dir = new DirectoryInfo(path);
            return dir.Exists ? ToEntry(dir) : null;
        }, ct);

    public Task CreateDirectoryAsync(string path, CancellationToken ct) =>
        Task.Run(() => Directory.CreateDirectory(path), ct);

    public Task DeleteAsync(string path, CancellationToken ct) =>
        Task.Run(() =>
        {
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
            else if (File.Exists(path)) File.Delete(path);
            else throw new FileNotFoundException($"Nothing to delete at {path}", path);
        }, ct);

    public Task RenameAsync(string path, string newName, CancellationToken ct) =>
        Task.Run(() =>
        {
            var parent = Path.GetDirectoryName(path)
                ?? throw new IOException($"Cannot rename a root: {path}");
            var target = Path.Combine(parent, newName);
            if (Directory.Exists(path)) Directory.Move(path, target);
            else if (File.Exists(path)) File.Move(path, target);
            else throw new FileNotFoundException($"Nothing to rename at {path}", path);
        }, ct);

    public Task<Stream> OpenReadAsync(string path, CancellationToken ct) =>
        Task.FromResult<Stream>(new FileStream(
            path, FileMode.Open, FileAccess.Read, FileShare.Read,
            StreamBufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan));

    public Task<Stream> OpenWriteAsync(string path, CancellationToken ct) =>
        Task.FromResult<Stream>(new FileStream(
            path, FileMode.Create, FileAccess.Write, FileShare.None,
            StreamBufferSize, FileOptions.Asynchronous));

    internal static FileSystemEntry ToEntry(FileSystemInfo info) => info switch
    {
        FileInfo f => new FileSystemEntry(f.Name, f.FullName, IsDirectory: false, f.Length, f.LastWriteTimeUtc),
        _ => new FileSystemEntry(info.Name, info.FullName, IsDirectory: true, 0, info.LastWriteTimeUtc),
    };

    /// <summary>Folders first, then files, both alphabetically — the commander's listing order.</summary>
    internal static IReadOnlyList<FileSystemEntry> SortListing(List<FileSystemEntry> entries)
    {
        entries.Sort((a, b) => a.IsDirectory != b.IsDirectory
            ? (a.IsDirectory ? -1 : 1)
            : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        return entries;
    }
}
