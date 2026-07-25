using Microsoft.Extensions.Logging.Abstractions;
using RdpManager.Infrastructure.Rdp;
using Xunit;

namespace RdpManager.Infrastructure.Tests;

public sealed class TempRdpFileWriterTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "rdpmgr-tests-" + Guid.NewGuid().ToString("N"));
    private readonly TempRdpFileWriter _writer;

    public TempRdpFileWriterTests() => _writer = new TempRdpFileWriter(NullLogger<TempRdpFileWriter>.Instance, _dir);

    [Fact]
    public async Task Write_creates_file_with_content_and_no_secret()
    {
        var text = "full address:s:host\r\nuse multimon:i:1\r\n";
        var path = await _writer.WriteAsync(text, CancellationToken.None);

        Assert.True(File.Exists(path));
        var written = await File.ReadAllTextAsync(path);
        Assert.Equal(text, written);
        Assert.DoesNotContain("password", written, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DeleteQuietly_removes_the_file_and_tolerates_missing()
    {
        var path = await _writer.WriteAsync("x", CancellationToken.None);
        _writer.DeleteQuietly(path);
        Assert.False(File.Exists(path));
        _writer.DeleteQuietly(path); // no throw on second delete
    }

    [Fact]
    public async Task SweepOrphans_removes_only_files_older_than_maxAge()
    {
        var path = await _writer.WriteAsync("x", CancellationToken.None);
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddHours(-2));

        var removed = _writer.SweepOrphans(TimeSpan.FromHours(1), DateTimeOffset.UtcNow);

        Assert.Equal(1, removed);
        Assert.False(File.Exists(path));
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); } catch { /* best effort */ }
    }
}
