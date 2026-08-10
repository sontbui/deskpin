using RdpManager.Domain.Enums;

namespace RdpManager.Domain.ValueObjects;

/// <summary>
/// One item in the transfer queue: a single file moving up or down. Immutable value object —
/// every state change returns a new instance, and the status state machine is enforced here so
/// no layer above can produce an impossible transition (e.g. progress on a completed job).
/// Holds no I/O and no paths: where the bytes come from and go to is the Application's concern.
/// </summary>
public sealed record TransferJob
{
    public Guid Id { get; }
    public string FileName { get; }
    public long TotalBytes { get; }
    public TransferDirection Direction { get; }
    public TransferStatus Status { get; private init; }
    public long BytesTransferred { get; private init; }
    /// <summary>Human-readable failure reason. Non-null exactly when <see cref="Status"/> is Failed.</summary>
    public string? ErrorReason { get; private init; }

    private TransferJob(
        Guid id, string fileName, long totalBytes, TransferDirection direction,
        TransferStatus status, long bytesTransferred, string? errorReason)
    {
        Id = id;
        FileName = fileName;
        TotalBytes = totalBytes;
        Direction = direction;
        Status = status;
        BytesTransferred = bytesTransferred;
        ErrorReason = errorReason;
    }

    public static TransferJob Create(string? fileName, long totalBytes, TransferDirection direction)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            throw new ArgumentException("File name must not be empty.", nameof(fileName));

        fileName = fileName.Trim();
        if (fileName.Contains('\\', StringComparison.Ordinal) || fileName.Contains('/', StringComparison.Ordinal))
            throw new ArgumentException("File name must be a bare name, not a path.", nameof(fileName));

        if (totalBytes < 0)
            throw new ArgumentOutOfRangeException(nameof(totalBytes), totalBytes, "Size must not be negative.");

        return new TransferJob(Guid.NewGuid(), fileName, totalBytes, direction, TransferStatus.Queued, 0, null);
    }

    public bool IsFinished => Status is TransferStatus.Completed or TransferStatus.Failed
        or TransferStatus.Skipped or TransferStatus.Cancelled;

    /// <summary>Queued → Active.</summary>
    public TransferJob Start()
    {
        Require(Status is TransferStatus.Queued, "start");
        return this with { Status = TransferStatus.Active, BytesTransferred = 0, ErrorReason = null };
    }

    /// <summary>Updates progress while Active. Bytes are clamped to [current, TotalBytes] so progress never runs backwards.</summary>
    public TransferJob WithProgress(long bytesTransferred)
    {
        Require(Status is TransferStatus.Active, "report progress on");
        var clamped = Math.Clamp(bytesTransferred, BytesTransferred, TotalBytes);
        return this with { BytesTransferred = clamped };
    }

    /// <summary>Active → Completed. Progress snaps to the full size.</summary>
    public TransferJob Complete()
    {
        Require(Status is TransferStatus.Active, "complete");
        return this with { Status = TransferStatus.Completed, BytesTransferred = TotalBytes };
    }

    /// <summary>Queued/Active → Failed, with a mandatory reason.</summary>
    public TransferJob Fail(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("A failure needs a reason.", nameof(reason));
        Require(Status is TransferStatus.Queued or TransferStatus.Active, "fail");
        return this with { Status = TransferStatus.Failed, ErrorReason = reason.Trim() };
    }

    /// <summary>Queued → Skipped (the user chose Skip in a conflict).</summary>
    public TransferJob Skip()
    {
        Require(Status is TransferStatus.Queued, "skip");
        return this with { Status = TransferStatus.Skipped };
    }

    /// <summary>Queued/Active → Cancelled.</summary>
    public TransferJob Cancel()
    {
        Require(Status is TransferStatus.Queued or TransferStatus.Active, "cancel");
        return this with { Status = TransferStatus.Cancelled };
    }

    /// <summary>Failed/Cancelled → Queued, with progress reset. This is the inline Retry affordance.</summary>
    public TransferJob ResetForRetry()
    {
        Require(Status is TransferStatus.Failed or TransferStatus.Cancelled, "retry");
        return this with { Status = TransferStatus.Queued, BytesTransferred = 0, ErrorReason = null };
    }

    private void Require(bool allowed, string verb)
    {
        if (!allowed)
            throw new InvalidOperationException($"Cannot {verb} a transfer that is {Status}.");
    }

    public override string ToString() => $"{FileName} [{Direction}] {Status} {BytesTransferred}/{TotalBytes}";
}
