using RdpManager.Application.Common;
using RdpManager.Application.Files;
using Xunit;

namespace RdpManager.Application.Tests.Files;

public sealed class TransferPathTests
{
    // ── Normalize: happy paths ──────────────────────────────────────────────

    [Theory]
    [InlineData(@"C:\Users\svc", @"C:\Users\svc")]
    [InlineData("C:/Users/svc", @"C:\Users\svc")]
    [InlineData(@"C:/Users\svc/Downloads", @"C:\Users\svc\Downloads")]
    [InlineData(@"c:\users", @"C:\users")]                    // drive letter normalized, casing kept
    [InlineData(@"C:\Users\\svc\", @"C:\Users\svc")]          // repeated + trailing separators collapse
    [InlineData("C:", @"C:\")]                                 // bare drive → drive root
    [InlineData("C:/", @"C:\")]
    [InlineData(@"C:\a\.\b\..\c", @"C:\a\c")]                 // . and .. resolve
    public void Normalize_accepts_both_separators_and_resolves(string input, string expected)
    {
        var result = TransferPath.Normalize(input);
        Assert.True(result.IsSuccess);
        Assert.Equal(expected, result.Value);
    }

    [Theory]
    [InlineData(@"\\server\share", @"\\server\share")]
    [InlineData("//server/share/folder", @"\\server\share\folder")]
    [InlineData(@"\\tsclient\C\Users", @"\\tsclient\C\Users")]
    public void Normalize_handles_unc_paths(string input, string expected)
    {
        var result = TransferPath.Normalize(input);
        Assert.True(result.IsSuccess);
        Assert.Equal(expected, result.Value);
    }

    [Fact]
    public void Normalize_resolves_relative_input_against_the_current_directory()
    {
        var result = TransferPath.Normalize("Documents/specs", @"C:\Users\svc");
        Assert.True(result.IsSuccess);
        Assert.Equal(@"C:\Users\svc\Documents\specs", result.Value);
    }

    // ── Normalize: rejections ───────────────────────────────────────────────

    [Fact]
    public void Normalize_rejects_null()
    {
        var result = TransferPath.Normalize(null);
        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorKind.Validation, result.Error!.Kind);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(@"\\serveronly")]           // UNC needs server + share
    [InlineData(@"C:\a\..\..")]             // climbs above the root
    [InlineData(@"C:\repor<t")]             // invalid character
    [InlineData(@"C:\folder.")]             // segment ends with a dot
    [InlineData(@"C:\folder \x")]           // segment ends with a space
    [InlineData("Documents")]               // relative without a base
    [InlineData(@"CD:\folder")]             // not a drive
    public void Normalize_rejects_bad_input(string input)
    {
        var result = TransferPath.Normalize(input);
        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorKind.Validation, result.Error!.Kind);
    }

    // ── The small helpers the panes lean on ─────────────────────────────────

    [Fact]
    public void Join_and_GetFileName_round_trip()
    {
        var path = TransferPath.Join(@"C:\Users\svc", "report.pdf");
        Assert.Equal(@"C:\Users\svc\report.pdf", path);
        Assert.Equal("report.pdf", TransferPath.GetFileName(path));

        Assert.Equal(@"C:\report.pdf", TransferPath.Join(@"C:\", "report.pdf")); // no double separator
    }

    [Theory]
    [InlineData(@"C:\Users\svc", @"C:\Users")]
    [InlineData(@"C:\Users", @"C:\")]
    [InlineData(@"C:\", null)]
    [InlineData(@"\\server\share\a", @"\\server\share")]
    [InlineData(@"\\server\share", null)]
    public void GetParent_stops_at_the_root(string input, string? expected)
    {
        Assert.Equal(expected, TransferPath.GetParent(input));
    }

    [Fact]
    public void Breadcrumbs_carry_the_full_path_for_each_segment()
    {
        var crumbs = TransferPath.Breadcrumbs(@"C:\Users\svc");

        Assert.Equal(3, crumbs.Count);
        Assert.Equal("C:", crumbs[0].Label);
        Assert.Equal(@"C:\", crumbs[0].Path);
        Assert.Equal("Users", crumbs[1].Label);
        Assert.Equal(@"C:\Users", crumbs[1].Path);
        Assert.Equal("svc", crumbs[2].Label);
        Assert.Equal(@"C:\Users\svc", crumbs[2].Path);
    }

    [Fact]
    public void Breadcrumbs_treat_the_unc_share_as_the_root()
    {
        var crumbs = TransferPath.Breadcrumbs(@"\\tsclient\C\Users");

        Assert.Equal(2, crumbs.Count);
        Assert.Equal(@"tsclient\C", crumbs[0].Label);
        Assert.Equal(@"\\tsclient\C", crumbs[0].Path);
        Assert.Equal("Users", crumbs[1].Label);
        Assert.Equal(@"\\tsclient\C\Users", crumbs[1].Path);
    }

    [Theory]
    [InlineData("report.pdf", 1, "report (copy).pdf")]
    [InlineData("report.pdf", 2, "report (copy 2).pdf")]
    [InlineData("archive.tar.gz", 1, "archive.tar (copy).gz")]
    [InlineData("README", 1, "README (copy)")]
    public void CopyVariant_names_keep_both_files(string name, int ordinal, string expected)
    {
        Assert.Equal(expected, TransferPath.CopyVariant(name, ordinal));
    }
}
