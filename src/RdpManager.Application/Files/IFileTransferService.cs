using RdpManager.Application.Common;

namespace RdpManager.Application.Files;

/// <summary>
/// Everything the Files console needs: browse both sides, plan transfers (conflict detection),
/// run the queue with progress/throughput/ETA, cancel, retry, and clear.
/// Implemented by <see cref="FileTransferUseCase"/>; ViewModels talk only to this interface.
/// </summary>
public interface IFileTransferService
{
    /// <summary>Current queue, newest first. Snapshots — safe to enumerate from any thread.</summary>
    IReadOnlyList<TransferItem> Queue { get; }

    /// <summary>Finished transfers, newest first.</summary>
    IReadOnlyList<TransferRecord> History { get; }

    /// <summary>
    /// Raised whenever any job changes (enqueued, progress, finished). May fire on a background
    /// thread — Presentation marshals to the UI thread.
    /// </summary>
    event EventHandler? TransfersChanged;

    // ── Browsing ────────────────────────────────────────────────────────────
    Task<Result<IReadOnlyList<FileSystemEntry>>> ListDirectoryAsync(TransferEndpoint endpoint, string path, CancellationToken ct);
    Task<Result<IReadOnlyList<FileSystemEntry>>> ListLocalRootsAsync(CancellationToken ct);
    Task<Result<bool>> CreateDirectoryAsync(TransferEndpoint endpoint, string parentPath, string name, CancellationToken ct);
    Task<Result<bool>> DeleteAsync(TransferEndpoint endpoint, string path, CancellationToken ct);
    Task<Result<bool>> RenameAsync(TransferEndpoint endpoint, string path, string newName, CancellationToken ct);
    Task<Result<FileSystemEntry?>> StatAsync(TransferEndpoint endpoint, string path, CancellationToken ct);

    /// <summary>Where the local pane opens by default.</summary>
    string DefaultLocalDirectory { get; }

    // ── Transfers ───────────────────────────────────────────────────────────
    /// <summary>Stats every source and destination; splits requests into ready-to-queue and name conflicts.</summary>
    Task<Result<TransferPlan>> PlanAsync(IReadOnlyList<TransferRequest> requests, CancellationToken ct);

    /// <summary>Queues a conflict-free (or Replace-resolved) request and starts the queue if idle.</summary>
    Task<Result<TransferItem>> EnqueueAsync(TransferRequest request, CancellationToken ct);

    /// <summary>Applies the user's overwrite decision to one conflict and queues the outcome.</summary>
    Task<Result<TransferItem>> EnqueueResolvedAsync(NameConflict conflict, OverwriteDecision decision, CancellationToken ct);

    /// <summary>Cancels a queued or active job. Returns false when the job is already finished or unknown.</summary>
    bool Cancel(Guid jobId);

    /// <summary>Re-queues a failed or cancelled job with progress reset.</summary>
    bool Retry(Guid jobId);

    /// <summary>Removes finished jobs from the queue view (they stay in History). Returns how many were cleared.</summary>
    int ClearFinished();

    /// <summary>Completes when the queue has gone idle. Used by tests and orderly shutdown.</summary>
    Task DrainAsync();
}
