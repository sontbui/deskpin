using RdpManager.Application.Common;

namespace RdpManager.Application.Files;

/// <summary>
/// Path semantics for one endpoint. Local (This PC) is Windows; a remote SFTP host is POSIX
/// (<c>/home/user</c>, <c>/</c> separators). Each <see cref="IFileSystemBrowser"/> exposes the
/// model for its own paths so the transfer engine joins/splits with the right rules.
/// </summary>
public interface IPathModel
{
    Result<string> Normalize(string? input, string? basePath = null);
    string Join(string directory, string name);
    string GetFileName(string path);
    string? GetParent(string path);
    IReadOnlyList<BreadcrumbSegment> Breadcrumbs(string normalizedPath);
    string CopyVariant(string fileName, int ordinal = 1);
}

/// <summary>Windows path rules — delegates to the battle-tested <see cref="TransferPath"/>.</summary>
public sealed class WindowsPathModel : IPathModel
{
    public static readonly WindowsPathModel Instance = new();

    public Result<string> Normalize(string? input, string? basePath = null) => TransferPath.Normalize(input, basePath);
    public string Join(string directory, string name) => TransferPath.Join(directory, name);
    public string GetFileName(string path) => TransferPath.GetFileName(path);
    public string? GetParent(string path) => TransferPath.GetParent(path);
    public IReadOnlyList<BreadcrumbSegment> Breadcrumbs(string normalizedPath) => TransferPath.Breadcrumbs(normalizedPath);
    public string CopyVariant(string fileName, int ordinal = 1) => TransferPath.CopyVariant(fileName, ordinal);
}

/// <summary>POSIX path rules for SFTP hosts (Linux/macOS, and Windows OpenSSH which is also '/'-based).</summary>
public sealed class PosixPathModel : IPathModel
{
    public static readonly PosixPathModel Instance = new();

    public Result<string> Normalize(string? input, string? basePath = null) => PosixPath.Normalize(input, basePath);
    public string Join(string directory, string name) => PosixPath.Join(directory, name);
    public string GetFileName(string path) => PosixPath.GetFileName(path);
    public string? GetParent(string path) => PosixPath.GetParent(path);
    public IReadOnlyList<BreadcrumbSegment> Breadcrumbs(string normalizedPath) => PosixPath.Breadcrumbs(normalizedPath);
    public string CopyVariant(string fileName, int ordinal = 1) => PosixPath.CopyVariant(fileName, ordinal);
}
