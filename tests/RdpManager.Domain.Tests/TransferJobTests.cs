using RdpManager.Domain.Enums;
using RdpManager.Domain.ValueObjects;
using Xunit;

namespace RdpManager.Domain.Tests;

public sealed class TransferJobTests
{
    private static TransferJob NewJob(long size = 100) =>
        TransferJob.Create("report.pdf", size, TransferDirection.Upload);

    [Fact]
    public void Creates_queued_with_zero_progress()
    {
        var job = NewJob();
        Assert.Equal(TransferStatus.Queued, job.Status);
        Assert.Equal(0, job.BytesTransferred);
        Assert.Null(job.ErrorReason);
        Assert.False(job.IsFinished);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(@"folder\file.txt")]
    [InlineData("folder/file.txt")]
    public void Rejects_bad_file_names(string name) =>
        Assert.ThrowsAny<ArgumentException>(() => TransferJob.Create(name, 1, TransferDirection.Download));

    [Fact]
    public void Rejects_negative_size() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => TransferJob.Create("a.txt", -1, TransferDirection.Upload));

    [Fact]
    public void Happy_path_walks_queued_active_completed()
    {
        var job = NewJob(10).Start().WithProgress(4).WithProgress(9).Complete();
        Assert.Equal(TransferStatus.Completed, job.Status);
        Assert.Equal(10, job.BytesTransferred); // completion snaps to the full size
        Assert.True(job.IsFinished);
    }

    [Fact]
    public void Progress_never_runs_backwards_or_past_the_total()
    {
        var job = NewJob(10).Start().WithProgress(8);
        Assert.Equal(8, job.WithProgress(3).BytesTransferred);   // clamped up
        Assert.Equal(10, job.WithProgress(99).BytesTransferred); // clamped down
    }

    [Fact]
    public void Fail_requires_a_reason_and_keeps_it()
    {
        var job = NewJob().Start();
        Assert.ThrowsAny<ArgumentException>(() => job.Fail("  "));
        Assert.Equal("Connection reset", job.Fail("Connection reset").ErrorReason);
    }

    [Fact]
    public void Retry_resets_a_failed_job_to_queued()
    {
        var failed = NewJob(10).Start().WithProgress(5).Fail("boom");
        var retried = failed.ResetForRetry();
        Assert.Equal(TransferStatus.Queued, retried.Status);
        Assert.Equal(0, retried.BytesTransferred);
        Assert.Null(retried.ErrorReason);
        Assert.Equal(failed.Id, retried.Id); // same job, new attempt
    }

    [Fact]
    public void Terminal_states_refuse_further_transitions()
    {
        var done = NewJob(10).Start().Complete();
        Assert.Throws<InvalidOperationException>(() => done.Start());
        Assert.Throws<InvalidOperationException>(() => done.WithProgress(1));
        Assert.Throws<InvalidOperationException>(() => done.Cancel());
        Assert.Throws<InvalidOperationException>(() => done.ResetForRetry()); // only Failed/Cancelled retry

        var skipped = NewJob().Skip();
        Assert.Throws<InvalidOperationException>(() => skipped.Start());
    }

    [Fact]
    public void Cancel_works_from_queued_and_active()
    {
        Assert.Equal(TransferStatus.Cancelled, NewJob().Cancel().Status);
        Assert.Equal(TransferStatus.Cancelled, NewJob().Start().Cancel().Status);
    }
}
