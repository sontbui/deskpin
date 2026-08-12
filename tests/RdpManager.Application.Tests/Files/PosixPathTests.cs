using RdpManager.Application.Common;
using RdpManager.Application.Files;
using Xunit;

namespace RdpManager.Application.Tests.Files;

public sealed class PosixPathTests
{
    [Theory]
    [InlineData("/home/svc", "/home/svc")]
    [InlineData("/home/svc/", "/home/svc")]        // trailing slash trimmed
    [InlineData("/home//svc///x", "/home/svc/x")]  // repeated separators collapse
    [InlineData(@"\home\svc", "/home/svc")]        // Windows user typed backslashes
    [InlineData("/", "/")]
    [InlineData("/a/./b/../c", "/a/c")]            // . and .. resolve
    public void Normalize_handles_posix_paths(string input, string expected)
    {
        var result = PosixPath.Normalize(input);
        Assert.True(result.IsSuccess);
        Assert.Equal(expected, result.Value);
    }

    [Fact]
    public void Normalize_resolves_relative_against_base()
    {
        var result = PosixPath.Normalize("Downloads/x", "/home/svc");
        Assert.True(result.IsSuccess);
        Assert.Equal("/home/svc/Downloads/x", result.Value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("relative/without/base")]  // relative + no base
    [InlineData("/a/../..")]               // climbs above root
    public void Normalize_rejects_bad_input(string? input)
    {
        var result = PosixPath.Normalize(input);
        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorKind.Validation, result.Error!.Kind);
    }

    [Theory]
    [InlineData("/home/svc/report.pdf", "report.pdf")]
    [InlineData("/report.pdf", "report.pdf")]
    [InlineData("/home/svc", "svc")]
    public void GetFileName_returns_last_segment(string path, string expected) =>
        Assert.Equal(expected, PosixPath.GetFileName(path));

    [Theory]
    [InlineData("/home/svc/x", "/home/svc")]
    [InlineData("/home", "/")]
    [InlineData("/", null)]
    public void GetParent_stops_at_root(string input, string? expected) =>
        Assert.Equal(expected, PosixPath.GetParent(input));

    [Fact]
    public void Join_never_doubles_the_separator()
    {
        Assert.Equal("/home/svc/a.txt", PosixPath.Join("/home/svc", "a.txt"));
        Assert.Equal("/a.txt", PosixPath.Join("/", "a.txt"));
    }

    [Fact]
    public void Breadcrumbs_start_at_root_and_carry_full_paths()
    {
        var crumbs = PosixPath.Breadcrumbs("/home/svc");
        Assert.Equal(3, crumbs.Count);
        Assert.Equal("/", crumbs[0].Label);
        Assert.Equal("/", crumbs[0].Path);
        Assert.Equal("home", crumbs[1].Label);
        Assert.Equal("/home", crumbs[1].Path);
        Assert.Equal("svc", crumbs[2].Label);
        Assert.Equal("/home/svc", crumbs[2].Path);
    }

    [Theory]
    [InlineData("report.pdf", 1, "report (copy).pdf")]
    [InlineData("report.pdf", 2, "report (copy 2).pdf")]
    [InlineData("archive.tar.gz", 1, "archive.tar (copy).gz")]
    public void CopyVariant_names_keep_both_files(string name, int ordinal, string expected) =>
        Assert.Equal(expected, PosixPath.CopyVariant(name, ordinal));
}
