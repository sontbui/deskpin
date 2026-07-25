using RdpManager.Domain.Entities;
using RdpManager.Domain.Enums;
using RdpManager.Domain.ValueObjects;
using Xunit;

namespace RdpManager.Domain.Tests;

public sealed class HostAddressTests
{
    [Theory]
    [InlineData("10.0.4.21")]
    [InlineData("build-agent-01")]
    [InlineData("gw.corp.local")]
    [InlineData("::1")]
    public void Accepts_valid_hosts(string host) => Assert.Equal(host, HostAddress.Create(host).Host);

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("bad host!")]
    [InlineData("-leadinghyphen.com")]
    public void Rejects_invalid_hosts(string host) =>
        Assert.ThrowsAny<ArgumentException>(() => HostAddress.Create(host));

    [Theory]
    [InlineData(0)]
    [InlineData(70000)]
    public void Rejects_out_of_range_ports(int port) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => HostAddress.Create("host", port));

    [Fact]
    public void Equality_is_by_value()
    {
        Assert.Equal(HostAddress.Create("host", 3389), HostAddress.Create("host", 3389));
        Assert.NotEqual(HostAddress.Create("host", 3389), HostAddress.Create("host", 3390));
    }
}

public sealed class ConfidenceScoreTests
{
    [Theory]
    [InlineData(0.95, MatchConfidence.High)]
    [InlineData(0.85, MatchConfidence.High)]
    [InlineData(0.70, MatchConfidence.Medium)]
    [InlineData(0.50, MatchConfidence.Medium)]
    [InlineData(0.20, MatchConfidence.Low)]
    public void Bands_map_to_thresholds(double v, MatchConfidence band) =>
        Assert.Equal(band, ConfidenceScore.Of(v).Band);

    [Theory]
    [InlineData(-1.0, 0.0)]
    [InlineData(2.0, 1.0)]
    public void Clamps_to_unit_interval(double input, double expected) =>
        Assert.Equal(expected, ConfidenceScore.Of(input).Value);
}

public sealed class MachineTests
{
    private static Machine New() => new("box", HostAddress.Create("10.0.0.1"), DateTimeOffset.UnixEpoch);

    [Fact]
    public void New_machine_is_not_display_configured() => Assert.False(New().IsDisplayConfigured);

    [Fact]
    public void Rename_rejects_blank() => Assert.Throws<ArgumentException>(() => New().Rename("  "));

    [Fact]
    public void Configuring_display_rejects_foreign_profile()
    {
        var m = New();
        var foreign = new DisplayProfile(Guid.NewGuid(),
            new[] { new MonitorFingerprint(0, new MonitorGeometry(0, 0, 1920, 1080, Orientation.Landscape), "\\\\.\\D1", true) },
            DateTimeOffset.UnixEpoch);
        Assert.Throws<InvalidOperationException>(() => m.ConfigureDisplay(foreign));
    }

    [Fact]
    public void Domain_cannot_hold_a_plaintext_password()
    {
        // There is intentionally no API and no type to set a plaintext secret on a Machine.
        var props = typeof(Machine).GetProperties();
        Assert.DoesNotContain(props, p => p.Name.Contains("Password", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Adding_duplicate_tag_is_idempotent()
    {
        var m = New();
        m.AddTag(new Tag("CI"));
        m.AddTag(new Tag("ci"));
        Assert.Single(m.Tags);
    }
}
