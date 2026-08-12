using RdpManager.Domain.ValueObjects;

namespace RdpManager.Application.Files;

/// <summary>Which side of the commander an operation targets.</summary>
public enum TransferEndpoint
{
    Local = 0,
    Remote = 1,
}

/// <summary>How the user resolved a name conflict in the overwrite dialog.</summary>
public enum OverwriteDecision
{
    Replace = 0,
    KeepBoth = 1,
    Skip = 2,
}

/// <summary>A decision plus the "apply to all conflicts in this transfer" checkbox.</summary>
public sealed record OverwriteChoice(OverwriteDecision Decision, bool ApplyToAll);

/// <summary>
/// One requested file move: a source file and the directory it should land in.
/// <see cref="DestinationFileName"/> overrides the name at the destination (Keep-both).
/// </summary>
public sealed record TransferRequest(
    Domain.Enums.TransferDirection Direction,
    string SourcePath,
    string DestinationDirectory)
{
    /// <summary>Overrides the name at the destination (Keep-both). Null = keep the source's name.</summary>
    public string? DestinationFileName { get; init; }
}

/// <summary>A request whose destination already has a file with the same name.</summary>
public sealed record NameConflict(TransferRequest Request, FileSystemEntry Source, FileSystemEntry Existing);

/// <summary>The outcome of planning a transfer: what can go straight to the queue, and what needs the user.</summary>
public sealed record TransferPlan(IReadOnlyList<TransferRequest> Ready, IReadOnlyList<NameConflict> Conflicts);

/// <summary>
/// A queue snapshot the UI binds against: the immutable domain job plus the Application-computed
/// live numbers (throughput and ETA are meaningful only while the job is Active).
/// </summary>
public sealed record TransferItem(
    TransferJob Job,
    string SourcePath,
    string DestinationPath,
    double? BytesPerSecond,
    TimeSpan? EstimatedRemaining,
    DateTimeOffset EnqueuedAt);

/// <summary>A finished transfer, kept for the History tab.</summary>
public sealed record TransferRecord(
    TransferJob Job,
    string SourcePath,
    string DestinationPath,
    DateTimeOffset FinishedAt);
