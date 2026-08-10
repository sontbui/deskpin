using Microsoft.Extensions.Logging.Abstractions;
using RdpManager.Application.Common;
using RdpManager.Application.Files;
using RdpManager.Domain.Enums;
using Xunit;

namespace RdpManager.Application.Tests.Files;

public sealed class FileTransferUseCaseTests
{
    private readonly FakeLocalFileSystem _local = new();
    private readonly FakeRemoteFileSystem _remote = new();
    private readonly MutableClock _clock = new();

    private FileTransferUseCase Sut() =>
        new(_local, _remote, _clock, NullLogger<FileTransferUseCase>.Instance);

    private static TransferRequest Upload(string source, string destDir) =>
        new(TransferDirection.Upload, source, destDir);

    private static TransferRequest Download(string source, string destDir) =>
        new(TransferDirection.Download, source, destDir);

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var i = 0; i < 500 && !condition(); i++) await Task.Delay(10);
        Assert.True(condition(), "Timed out waiting for the queue to reach the expected state.");
    }

    // ── Queue progression ───────────────────────────────────────────────────

    [Fact]
    public async Task Upload_runs_to_completion_with_live_progress()
    {
        _local.AddFile(@"C:\out\build.zip", 10);
        var sut = Sut();

        var progressSnapshots = new List<long>();
        sut.TransfersChanged += (_, _) =>
        {
            var item = sut.Queue.FirstOrDefault();
            if (item is { Job.Status: TransferStatus.Active }) progressSnapshots.Add(item.Job.BytesTransferred);
        };

        var queued = await sut.EnqueueAsync(Upload(@"C:\out\build.zip", @"D:\in"), default);
        Assert.True(queued.IsSuccess);
        await sut.DrainAsync();

        var job = sut.Queue.Single().Job;
        Assert.Equal(TransferStatus.Completed, job.Status);
        Assert.Equal(10, job.TotalBytes);
        Assert.Equal(10, job.BytesTransferred);
        Assert.Equal(FakeFileSystemBase.CreatePattern(10), _remote.Files[@"D:\in\build.zip"]);

        // Chunked copy (4-byte chunks) surfaced intermediate progress, strictly increasing.
        Assert.True(progressSnapshots.Count >= 2);
        Assert.Equal(progressSnapshots.OrderBy(b => b).ToList(), progressSnapshots);

        var record = Assert.Single(sut.History);
        Assert.Equal(TransferStatus.Completed, record.Job.Status);
    }

    [Fact]
    public async Task Download_runs_to_completion()
    {
        _remote.AddFile(@"C:\dumps\nightly.bak", 9);
        var sut = Sut();

        await sut.EnqueueAsync(Download(@"C:\dumps\nightly.bak", @"C:\Users\me\Downloads"), default);
        await sut.DrainAsync();

        Assert.Equal(TransferStatus.Completed, sut.Queue.Single().Job.Status);
        Assert.Equal(FakeFileSystemBase.CreatePattern(9), _local.Files[@"C:\Users\me\Downloads\nightly.bak"]);
    }

    [Fact]
    public async Task Queue_processes_jobs_in_enqueue_order()
    {
        _local.AddFile(@"C:\src\a.txt", 4);
        _local.AddFile(@"C:\src\b.txt", 4);
        _local.AddFile(@"C:\src\c.txt", 4);
        var sut = Sut();

        await sut.EnqueueAsync(Upload(@"C:\src\a.txt", @"D:\in"), default);
        await sut.EnqueueAsync(Upload(@"C:\src\b.txt", @"D:\in"), default);
        await sut.EnqueueAsync(Upload(@"C:\src\c.txt", @"D:\in"), default);
        await sut.DrainAsync();

        Assert.Equal(new[] { @"D:\in\a.txt", @"D:\in\b.txt", @"D:\in\c.txt" }, _remote.CopyLog);
        Assert.All(sut.Queue, item => Assert.Equal(TransferStatus.Completed, item.Job.Status));
    }

    [Fact]
    public async Task Throughput_and_eta_appear_while_a_job_is_active()
    {
        _local.AddFile(@"C:\out\big.bin", 40);
        _clock.AdvancePerRead = TimeSpan.FromSeconds(1); // time passes as the pump reads the clock
        var sut = Sut();

        var sawThroughput = false;
        sut.TransfersChanged += (_, _) =>
        {
            var item = sut.Queue.FirstOrDefault();
            if (item is { BytesPerSecond: > 0, EstimatedRemaining: not null }) sawThroughput = true;
        };

        await sut.EnqueueAsync(Upload(@"C:\out\big.bin", @"D:\in"), default);
        await sut.DrainAsync();

        Assert.True(sawThroughput, "No throughput/ETA was ever reported while the job was active.");
        // Finished jobs don't advertise stale rates.
        Assert.Null(sut.Queue.Single().BytesPerSecond);
    }

    // ── Cancel / retry / clear ──────────────────────────────────────────────

    [Fact]
    public async Task Cancelling_an_active_job_marks_it_cancelled()
    {
        _local.AddFile(@"C:\out\huge.iso", 12);
        _remote.Hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var sut = Sut();

        var item = (await sut.EnqueueAsync(Upload(@"C:\out\huge.iso", @"D:\in"), default)).Value;
        await WaitUntilAsync(() => sut.Queue.Single().Job.Status == TransferStatus.Active);

        Assert.True(sut.Cancel(item.Job.Id));
        await sut.DrainAsync();

        Assert.Equal(TransferStatus.Cancelled, sut.Queue.Single().Job.Status);
        Assert.False(_remote.Files.ContainsKey(@"D:\in\huge.iso")); // no half-written destination
        Assert.Contains(sut.History, r => r.Job.Status == TransferStatus.Cancelled);
    }

    [Fact]
    public async Task Cancelling_a_queued_job_never_runs_it()
    {
        _local.AddFile(@"C:\out\first.bin", 12);
        _local.AddFile(@"C:\out\second.bin", 12);
        _remote.Hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var sut = Sut();

        await sut.EnqueueAsync(Upload(@"C:\out\first.bin", @"D:\in"), default);
        var second = (await sut.EnqueueAsync(Upload(@"C:\out\second.bin", @"D:\in"), default)).Value;
        await WaitUntilAsync(() => sut.Queue.Any(i => i.Job.Status == TransferStatus.Active));

        Assert.True(sut.Cancel(second.Job.Id));
        _remote.Hold.SetResult();
        await sut.DrainAsync();

        Assert.DoesNotContain(@"D:\in\second.bin", _remote.CopyLog);
        Assert.Equal(TransferStatus.Cancelled,
            sut.Queue.Single(i => i.Job.FileName == "second.bin").Job.Status);
    }

    [Fact]
    public async Task Failed_job_carries_the_reason_and_retry_runs_it_again()
    {
        _local.AddFile(@"C:\out\flaky.bin", 8);
        _remote.FailuresBeforeSuccess = 1;
        var sut = Sut();

        var item = (await sut.EnqueueAsync(Upload(@"C:\out\flaky.bin", @"D:\in"), default)).Value;
        await sut.DrainAsync();

        var failed = sut.Queue.Single().Job;
        Assert.Equal(TransferStatus.Failed, failed.Status);
        Assert.Equal("Connection reset", failed.ErrorReason);

        Assert.True(sut.Retry(item.Job.Id));
        await sut.DrainAsync();

        var done = sut.Queue.Single().Job;
        Assert.Equal(TransferStatus.Completed, done.Status);
        Assert.Null(done.ErrorReason);
        Assert.Equal(FakeFileSystemBase.CreatePattern(8), _remote.Files[@"D:\in\flaky.bin"]);
    }

    [Fact]
    public async Task ClearFinished_keeps_running_and_queued_jobs()
    {
        _local.AddFile(@"C:\out\done.txt", 4);
        _local.AddFile(@"C:\out\running.txt", 12);
        _local.AddFile(@"C:\out\waiting.txt", 4);
        var sut = Sut();

        await sut.EnqueueAsync(Upload(@"C:\out\done.txt", @"D:\in"), default);
        await sut.DrainAsync(); // first one completes

        _remote.Hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await sut.EnqueueAsync(Upload(@"C:\out\running.txt", @"D:\in"), default);
        await sut.EnqueueAsync(Upload(@"C:\out\waiting.txt", @"D:\in"), default);
        await WaitUntilAsync(() => sut.Queue.Any(i => i.Job.Status == TransferStatus.Active));

        Assert.Equal(1, sut.ClearFinished());
        Assert.Equal(2, sut.Queue.Count);
        Assert.DoesNotContain(sut.Queue, i => i.Job.FileName == "done.txt");
        Assert.Single(sut.History); // history keeps the receipt

        _remote.Hold.SetResult();
        await sut.DrainAsync();
    }

    // ── Conflict planning & resolution ──────────────────────────────────────

    [Fact]
    public async Task Plan_splits_ready_files_from_name_conflicts()
    {
        _local.AddFile(@"C:\out\fresh.txt", 4);
        _local.AddFile(@"C:\out\taken.txt", 6);
        _remote.AddFile(@"D:\in\taken.txt", 3);
        var sut = Sut();

        var plan = await sut.PlanAsync(new[]
        {
            Upload(@"C:\out\fresh.txt", @"D:\in"),
            Upload(@"C:\out\taken.txt", @"D:\in"),
        }, default);

        Assert.True(plan.IsSuccess);
        Assert.Equal(@"C:\out\fresh.txt", Assert.Single(plan.Value.Ready).SourcePath);
        var conflict = Assert.Single(plan.Value.Conflicts);
        Assert.Equal("taken.txt", conflict.Source.Name);
        Assert.Equal(6, conflict.Source.SizeBytes);   // the copy being sent…
        Assert.Equal(3, conflict.Existing.SizeBytes); // …vs the copy already there (dialog shows both)
    }

    [Fact]
    public async Task Replace_overwrites_the_destination()
    {
        _local.AddFile(@"C:\out\report.pdf", 8);
        _remote.AddFile(@"D:\in\report.pdf", 3);
        var sut = Sut();

        var plan = await sut.PlanAsync(new[] { Upload(@"C:\out\report.pdf", @"D:\in") }, default);
        await sut.EnqueueResolvedAsync(plan.Value.Conflicts[0], OverwriteDecision.Replace, default);
        await sut.DrainAsync();

        Assert.Equal(FakeFileSystemBase.CreatePattern(8), _remote.Files[@"D:\in\report.pdf"]);
    }

    [Fact]
    public async Task KeepBoth_writes_a_copy_and_leaves_the_original_alone()
    {
        _local.AddFile(@"C:\out\report.pdf", 8);
        var original = FakeFileSystemBase.CreatePattern(3);
        _remote.AddFile(@"D:\in\report.pdf", original);
        _remote.AddFile(@"D:\in\report (copy).pdf", 2); // first variant is taken too
        var sut = Sut();

        var plan = await sut.PlanAsync(new[] { Upload(@"C:\out\report.pdf", @"D:\in") }, default);
        await sut.EnqueueResolvedAsync(plan.Value.Conflicts[0], OverwriteDecision.KeepBoth, default);
        await sut.DrainAsync();

        Assert.Equal(original, _remote.Files[@"D:\in\report.pdf"]);
        Assert.Equal(FakeFileSystemBase.CreatePattern(8), _remote.Files[@"D:\in\report (copy 2).pdf"]);
    }

    [Fact]
    public async Task Skip_records_the_decision_without_moving_bytes()
    {
        _local.AddFile(@"C:\out\report.pdf", 8);
        var original = FakeFileSystemBase.CreatePattern(3);
        _remote.AddFile(@"D:\in\report.pdf", original);
        var sut = Sut();

        var plan = await sut.PlanAsync(new[] { Upload(@"C:\out\report.pdf", @"D:\in") }, default);
        var item = await sut.EnqueueResolvedAsync(plan.Value.Conflicts[0], OverwriteDecision.Skip, default);
        await sut.DrainAsync();

        Assert.Equal(TransferStatus.Skipped, item.Value.Job.Status);
        Assert.Equal(original, _remote.Files[@"D:\in\report.pdf"]); // untouched
        Assert.Empty(_remote.CopyLog);
        Assert.Contains(sut.History, r => r.Job.Status == TransferStatus.Skipped);
    }

    // ── Browsing errors surface as data, not exceptions ─────────────────────

    [Fact]
    public async Task Listing_a_denied_directory_returns_a_permission_error()
    {
        _remote.Directories.Add(@"C:\logs");
        _remote.DeniedPaths.Add(@"C:\logs");
        var sut = Sut();

        var result = await sut.ListDirectoryAsync(TransferEndpoint.Remote, @"C:\logs", default);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorKind.PermissionDenied, result.Error!.Kind);
    }

    [Fact]
    public async Task Listing_a_missing_directory_returns_not_found()
    {
        var sut = Sut();
        var result = await sut.ListDirectoryAsync(TransferEndpoint.Remote, @"C:\nope", default);
        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorKind.NotFound, result.Error!.Kind);
    }

    [Fact]
    public async Task Listing_an_invalid_path_fails_validation_before_any_io()
    {
        var sut = Sut();
        var result = await sut.ListDirectoryAsync(TransferEndpoint.Local, @"C:\bad|name", default);
        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorKind.Validation, result.Error!.Kind);
    }

    [Fact]
    public async Task Enqueueing_a_folder_is_rejected()
    {
        _local.Directories.Add(@"C:\out\folder");
        var sut = Sut();

        var result = await sut.EnqueueAsync(Upload(@"C:\out\folder", @"D:\in"), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorKind.Validation, result.Error!.Kind);
    }

    [Fact]
    public async Task Enqueueing_a_missing_source_is_not_found()
    {
        var sut = Sut();
        var result = await sut.EnqueueAsync(Upload(@"C:\out\ghost.txt", @"D:\in"), default);
        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorKind.NotFound, result.Error!.Kind);
    }
}
