using Microsoft.Extensions.Logging;
using RdpManager.Application.Common;
using RdpManager.Domain.Abstractions;
using RdpManager.Domain.Enums;
using RdpManager.Domain.ValueObjects;

namespace RdpManager.Application.Files;

/// <summary>
/// Orchestrates the Files console: browsing both endpoints, conflict planning, and a sequential
/// transfer queue with live progress, throughput and ETA. Pure orchestration over
/// <see cref="ILocalFileSystem"/>/<see cref="IRemoteFileSystem"/> — no I/O of its own, so the
/// whole thing is unit-testable with in-memory fakes (see FileTransferUseCaseTests).
/// </summary>
public sealed class FileTransferUseCase : IFileTransferService
{
    private const int MaxKeepBothProbes = 100;

    private readonly ILocalFileSystem _local;
    private readonly IRemoteFileSystem _remote;
    private readonly IClock _clock;
    private readonly ILogger<FileTransferUseCase> _logger;

    private readonly object _gate = new();
    private readonly List<QueueEntry> _entries = new();   // newest first
    private readonly List<TransferRecord> _history = new(); // newest first
    private Task? _pump;

    public FileTransferUseCase(ILocalFileSystem local, IRemoteFileSystem remote, IClock clock, ILogger<FileTransferUseCase> logger)
    {
        _local = local;
        _remote = remote;
        _clock = clock;
        _logger = logger;
    }

    public event EventHandler? TransfersChanged;

    public string DefaultLocalDirectory => _local.DefaultDirectory;

    public void SetRemoteTarget(RemoteConnection? connection, System.Security.SecureString? secret) =>
        _remote.SetTarget(connection, secret);

    public async Task<Result<string?>> GetRemoteHomeAsync(CancellationToken ct)
    {
        try { return await _remote.GetHomeDirectoryAsync(ct); }
        catch (Exception ex) when (ex is not OperationCanceledException) { return Map(ex, "the remote home directory"); }
    }

    public async Task<Result<string?>> ConnectRemoteAsync(CancellationToken ct)
    {
        // TryConnectAsync already classifies the expected failures, so there is nothing to map.
        // The catch is only for a fault it could not foresee.
        try { return await _remote.TryConnectAsync(ct); }
        catch (Exception ex) when (ex is not OperationCanceledException) { return Map(ex, "the remote host"); }
    }

    public IPathModel RemotePathModel => _remote.PathModel;
    public IPathModel LocalPathModel => _local.PathModel;

    public IReadOnlyList<TransferItem> Queue
    {
        get { lock (_gate) return _entries.Select(e => e.Snapshot()).ToList(); }
    }

    public IReadOnlyList<TransferRecord> History
    {
        get { lock (_gate) return _history.ToList(); }
    }

    // ── Browsing ────────────────────────────────────────────────────────────

    public async Task<Result<IReadOnlyList<FileSystemEntry>>> ListDirectoryAsync(
        TransferEndpoint endpoint, string path, CancellationToken ct)
    {
        var browser = Browser(endpoint);
        var parsed = browser.PathModel.Normalize(path);
        if (!parsed.IsSuccess) return parsed.Error!;
        try
        {
            return Result<IReadOnlyList<FileSystemEntry>>.Success(
                await browser.ListDirectoryAsync(parsed.Value, ct));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Map(ex, parsed.Value);
        }
    }

    public async Task<Result<IReadOnlyList<FileSystemEntry>>> ListLocalRootsAsync(CancellationToken ct)
    {
        try
        {
            return Result<IReadOnlyList<FileSystemEntry>>.Success(await _local.ListRootsAsync(ct));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Map(ex, "local drives");
        }
    }

    public async Task<Result<bool>> CreateDirectoryAsync(TransferEndpoint endpoint, string parentPath, string name, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(name)) return Error.Validation("Folder name must not be empty.");
        var browser = Browser(endpoint);
        var parsed = browser.PathModel.Normalize(browser.PathModel.Join(parentPath, name.Trim()));
        if (!parsed.IsSuccess) return parsed.Error!;
        try
        {
            await browser.CreateDirectoryAsync(parsed.Value, ct);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Map(ex, parsed.Value);
        }
    }

    public async Task<Result<bool>> DeleteAsync(TransferEndpoint endpoint, string path, CancellationToken ct)
    {
        try
        {
            await Browser(endpoint).DeleteAsync(path, ct);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Map(ex, path);
        }
    }

    public async Task<Result<bool>> RenameAsync(TransferEndpoint endpoint, string path, string newName, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(newName)) return Error.Validation("The new name must not be empty.");
        try
        {
            await Browser(endpoint).RenameAsync(path, newName.Trim(), ct);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Map(ex, path);
        }
    }

    public async Task<Result<FileSystemEntry?>> StatAsync(TransferEndpoint endpoint, string path, CancellationToken ct)
    {
        try
        {
            return await Browser(endpoint).StatAsync(path, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Map(ex, path);
        }
    }

    // ── Planning & queueing ─────────────────────────────────────────────────

    public async Task<Result<TransferPlan>> PlanAsync(IReadOnlyList<TransferRequest> requests, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(requests);
        var ready = new List<TransferRequest>();
        var conflicts = new List<NameConflict>();

        foreach (var request in requests)
        {
            FileSystemEntry? source;
            FileSystemEntry? existing;
            try
            {
                source = await SourceBrowser(request).StatAsync(request.SourcePath, ct);
                if (source is null) return Error.NotFound(request.SourcePath);
                if (source.IsDirectory)
                    return Error.Validation($"'{source.Name}' is a folder — open it and select the files to transfer.");

                existing = await DestinationBrowser(request).StatAsync(DestinationPathOf(request), ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return Map(ex, request.SourcePath);
            }

            if (existing is null) ready.Add(request);
            else conflicts.Add(new NameConflict(request, source, existing));
        }

        return new TransferPlan(ready, conflicts);
    }

    public async Task<Result<TransferItem>> EnqueueAsync(TransferRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        FileSystemEntry? source;
        try
        {
            source = await SourceBrowser(request).StatAsync(request.SourcePath, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Map(ex, request.SourcePath);
        }
        if (source is null) return Error.NotFound(request.SourcePath);
        if (source.IsDirectory)
            return Error.Validation($"'{source.Name}' is a folder — open it and select the files to transfer.");

        var job = TransferJob.Create(FileNameOf(request), source.SizeBytes, request.Direction);
        var entry = new QueueEntry(job, request.SourcePath, DestinationPathOf(request), _clock.UtcNow);
        lock (_gate) _entries.Insert(0, entry);
        _logger.LogInformation("Queued {Direction} of {File} ({Bytes} bytes) → {Destination}",
            job.Direction, job.FileName, job.TotalBytes, entry.DestinationPath);
        RaiseChanged();
        EnsurePumping();
        return entry.Snapshot();
    }

    public async Task<Result<TransferItem>> EnqueueResolvedAsync(NameConflict conflict, OverwriteDecision decision, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(conflict);
        switch (decision)
        {
            case OverwriteDecision.Replace:
                return await EnqueueAsync(conflict.Request, ct);

            case OverwriteDecision.KeepBoth:
            {
                var name = await FindFreeNameAsync(conflict.Request, ct);
                if (!name.IsSuccess) return name.Error!;
                return await EnqueueAsync(conflict.Request with { DestinationFileName = name.Value }, ct);
            }

            case OverwriteDecision.Skip:
            default:
            {
                // Record the skip so the dock and history are honest about what didn't move.
                var job = TransferJob.Create(FileNameOf(conflict.Request), conflict.Source.SizeBytes, conflict.Request.Direction).Skip();
                var entry = new QueueEntry(job, conflict.Request.SourcePath, DestinationPathOf(conflict.Request), _clock.UtcNow);
                lock (_gate)
                {
                    _entries.Insert(0, entry);
                    _history.Insert(0, new TransferRecord(job, entry.SourcePath, entry.DestinationPath, _clock.UtcNow));
                }
                _logger.LogInformation("Skipped {File} — destination already has it", job.FileName);
                RaiseChanged();
                return entry.Snapshot();
            }
        }
    }

    private async Task<Result<string>> FindFreeNameAsync(TransferRequest request, CancellationToken ct)
    {
        var destModel = DestinationBrowser(request).PathModel;
        var original = SourceBrowser(request).PathModel.GetFileName(request.SourcePath);
        for (var i = 1; i <= MaxKeepBothProbes; i++)
        {
            var candidate = destModel.CopyVariant(original, i);
            FileSystemEntry? taken;
            try
            {
                taken = await DestinationBrowser(request).StatAsync(
                    destModel.Join(request.DestinationDirectory, candidate), ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return Map(ex, request.DestinationDirectory);
            }
            if (taken is null) return candidate;
        }
        return new Error(ErrorKind.Conflict, "conflict", $"Couldn't find a free name for '{original}' in the destination.");
    }

    // ── Queue control ───────────────────────────────────────────────────────

    public bool Cancel(Guid jobId)
    {
        CancellationTokenSource? toSignal = null;
        var changed = false;
        lock (_gate)
        {
            var entry = _entries.FirstOrDefault(e => e.Job.Id == jobId);
            if (entry is null || entry.Job.IsFinished) return false;

            if (entry.Job.Status == TransferStatus.Active)
            {
                toSignal = entry.Cancellation; // signalled outside the lock; the pump finalizes the state
                changed = toSignal is not null;
            }
            else if (entry.Job.Status == TransferStatus.Queued)
            {
                entry.Job = entry.Job.Cancel();
                _history.Insert(0, new TransferRecord(entry.Job, entry.SourcePath, entry.DestinationPath, _clock.UtcNow));
                changed = true;
            }
        }
        if (toSignal is not null)
        {
            try { toSignal.Cancel(); }
            catch (ObjectDisposedException) { /* the job finished in the same instant */ }
        }
        if (changed)
        {
            _logger.LogInformation("Cancel requested for job {JobId}", jobId);
            RaiseChanged();
        }
        return changed;
    }

    public bool Retry(Guid jobId)
    {
        lock (_gate)
        {
            var entry = _entries.FirstOrDefault(e => e.Job.Id == jobId);
            if (entry is null || entry.Job.Status is not (TransferStatus.Failed or TransferStatus.Cancelled))
                return false;
            entry.Job = entry.Job.ResetForRetry();
            entry.BytesPerSecond = null;
            entry.EstimatedRemaining = null;
        }
        _logger.LogInformation("Retrying job {JobId}", jobId);
        RaiseChanged();
        EnsurePumping();
        return true;
    }

    public int ClearFinished()
    {
        int removed;
        lock (_gate) removed = _entries.RemoveAll(e => e.Job.IsFinished);
        if (removed > 0) RaiseChanged();
        return removed;
    }

    public async Task DrainAsync()
    {
        while (true)
        {
            Task? pump;
            lock (_gate)
            {
                pump = _pump;
                if ((pump is null || pump.IsCompleted) && !_entries.Any(e => e.Job.Status is TransferStatus.Queued or TransferStatus.Active))
                    return;
            }
            if (pump is not null) await pump.ConfigureAwait(false);
            else await Task.Yield();
        }
    }

    // ── The pump ────────────────────────────────────────────────────────────

    private void EnsurePumping()
    {
        lock (_gate)
        {
            if (_pump is { IsCompleted: false }) return;
            if (!_entries.Any(e => e.Job.Status == TransferStatus.Queued)) return;
            _pump = Task.Run(PumpAsync);
        }
    }

    private async Task PumpAsync()
    {
        while (true)
        {
            QueueEntry? entry;
            CancellationTokenSource cts;
            lock (_gate)
            {
                // Oldest queued first — the list is newest-first, so scan from the back.
                entry = _entries.LastOrDefault(e => e.Job.Status == TransferStatus.Queued);
                if (entry is null) return;
                cts = new CancellationTokenSource();
                entry.Cancellation = cts;
                entry.Job = entry.Job.Start();
                entry.StartedAtUtc = _clock.UtcNow;
            }
            RaiseChanged();

            try
            {
                var progress = new SynchronousProgress(bytes => OnProgress(entry, bytes));
                if (entry.Job.Direction == TransferDirection.Upload)
                {
                    var source = await _local.OpenReadAsync(entry.SourcePath, cts.Token).ConfigureAwait(false);
                    await using (source.ConfigureAwait(false))
                    {
                        await _remote.CopyInAsync(source, entry.DestinationPath, progress, cts.Token).ConfigureAwait(false);
                    }
                }
                else
                {
                    var destination = await _local.OpenWriteAsync(entry.DestinationPath, cts.Token).ConfigureAwait(false);
                    await using (destination.ConfigureAwait(false))
                    {
                        await _remote.CopyOutAsync(entry.SourcePath, destination, progress, cts.Token).ConfigureAwait(false);
                    }
                }
                Finalize(entry, job => job.Complete());
            }
            catch (OperationCanceledException)
            {
                Finalize(entry, job => job.Cancel());
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Transfer of {File} failed", entry.Job.FileName);
                Finalize(entry, job => job.Fail(FriendlyReason(ex)));
            }
            finally
            {
                lock (_gate) entry.Cancellation = null;
                cts.Dispose();
            }
        }
    }

    private void Finalize(QueueEntry entry, Func<TransferJob, TransferJob> transition)
    {
        lock (_gate)
        {
            entry.Job = transition(entry.Job);
            entry.BytesPerSecond = null;
            entry.EstimatedRemaining = null;
            _history.Insert(0, new TransferRecord(entry.Job, entry.SourcePath, entry.DestinationPath, _clock.UtcNow));
        }
        _logger.LogInformation("Transfer {File}: {Status}{Reason}", entry.Job.FileName, entry.Job.Status,
            entry.Job.ErrorReason is null ? string.Empty : $" — {entry.Job.ErrorReason}");
        RaiseChanged();
    }

    private void OnProgress(QueueEntry entry, long bytes)
    {
        lock (_gate)
        {
            if (entry.Job.Status != TransferStatus.Active) return;
            entry.Job = entry.Job.WithProgress(bytes);

            var elapsed = _clock.UtcNow - (entry.StartedAtUtc ?? _clock.UtcNow);
            if (elapsed > TimeSpan.Zero && entry.Job.BytesTransferred > 0)
            {
                var bps = entry.Job.BytesTransferred / elapsed.TotalSeconds;
                entry.BytesPerSecond = bps;
                entry.EstimatedRemaining = bps > 0
                    ? TimeSpan.FromSeconds((entry.Job.TotalBytes - entry.Job.BytesTransferred) / bps)
                    : null;
            }
        }
        RaiseChanged();
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private IFileSystemBrowser Browser(TransferEndpoint endpoint) =>
        endpoint == TransferEndpoint.Local ? _local : _remote;

    private IFileSystemBrowser SourceBrowser(TransferRequest request) =>
        request.Direction == TransferDirection.Upload ? _local : _remote;

    private IFileSystemBrowser DestinationBrowser(TransferRequest request) =>
        request.Direction == TransferDirection.Upload ? _remote : _local;

    /// <summary>The destination file name — the override (Keep-both) or the source's own name, in the SOURCE's path style.</summary>
    private string FileNameOf(TransferRequest request) =>
        request.DestinationFileName ?? SourceBrowser(request).PathModel.GetFileName(request.SourcePath);

    /// <summary>The full destination path, joined with the DESTINATION endpoint's path style.</summary>
    private string DestinationPathOf(TransferRequest request) =>
        DestinationBrowser(request).PathModel.Join(request.DestinationDirectory, FileNameOf(request));

    private static Error Map(Exception ex, string what) => ex switch
    {
        // Nothing was ever rejected here - the machine simply has no sign-in details saved,
        // so the fix is "add them", not "your password is wrong".
        RemoteCredentialsMissingException => new Error(ErrorKind.NeedsReconfiguration, "credentials_missing", ex.Message),
        // Same reasoning as NotFound below: the thrower names which credential was refused
        // and where to fix it, which beats a generic "Access to <what> was denied."
        UnauthorizedAccessException when !string.IsNullOrWhiteSpace(ex.Message) =>
            new Error(ErrorKind.PermissionDenied, "permission_denied", ex.Message),
        UnauthorizedAccessException => Error.PermissionDenied(what),
        // Keep the thrower's message when it has one — the remote bridge explains *why*
        // a path is unreachable (admin share, SMB, credentials), which beats "not found".
        DirectoryNotFoundException or FileNotFoundException => new Error(ErrorKind.NotFound, "not_found",
            string.IsNullOrWhiteSpace(ex.Message) ? $"{what} was not found." : ex.Message),
        // A network/service problem is an expected outcome, not a defect - keeping it under
        // Unexpected is what made "the machine is off" look the same as "the app is broken".
        RemoteUnreachableException => new Error(ErrorKind.Unreachable, "unreachable", ex.Message),
        IOException io => Error.Unexpected(io.Message),
        _ => Error.Unexpected(ex.Message),
    };

    private static string FriendlyReason(Exception ex) => ex switch
    {
        UnauthorizedAccessException => "Access denied",
        FileNotFoundException or DirectoryNotFoundException => "Source is gone",
        IOException io when !string.IsNullOrWhiteSpace(io.Message) => io.Message,
        _ => string.IsNullOrWhiteSpace(ex.Message) ? "Transfer failed" : ex.Message,
    };

    private void RaiseChanged() => TransfersChanged?.Invoke(this, EventArgs.Empty);

    /// <summary>Mutable queue slot. All mutation happens under the use case's gate.</summary>
    private sealed class QueueEntry
    {
        public QueueEntry(TransferJob job, string sourcePath, string destinationPath, DateTimeOffset enqueuedAt)
        {
            Job = job;
            SourcePath = sourcePath;
            DestinationPath = destinationPath;
            EnqueuedAt = enqueuedAt;
        }

        public TransferJob Job { get; set; }
        public string SourcePath { get; }
        public string DestinationPath { get; }
        public DateTimeOffset EnqueuedAt { get; }
        public DateTimeOffset? StartedAtUtc { get; set; }
        public double? BytesPerSecond { get; set; }
        public TimeSpan? EstimatedRemaining { get; set; }
        public CancellationTokenSource? Cancellation { get; set; }

        public TransferItem Snapshot() =>
            new(Job, SourcePath, DestinationPath, BytesPerSecond, EstimatedRemaining, EnqueuedAt);
    }

    /// <summary>IProgress that reports inline (no SynchronizationContext posting) so tests are deterministic.</summary>
    private sealed class SynchronousProgress : IProgress<long>
    {
        private readonly Action<long> _handler;
        public SynchronousProgress(Action<long> handler) => _handler = handler;
        public void Report(long value) => _handler(value);
    }
}
