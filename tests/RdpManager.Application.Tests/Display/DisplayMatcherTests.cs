using RdpManager.Application.Display;
using RdpManager.Domain.Enums;
using Xunit;
using static RdpManager.Application.Tests.Display.MonitorBuilder;

namespace RdpManager.Application.Tests.Display;

public sealed class DisplayMatcherTests
{
    private readonly DisplayMatcher _matcher = new();

    [Fact]
    public void Index_swap_same_hardware_matches_with_high_confidence()
    {
        // Saved: Center(primary) + Right, identified by serial.
        var profile = Profile(
            Saved(0, 0, 0, 2560, 1440, primary: true, serial: "SN-CENTER"),
            Saved(1, 2560, 0, serial: "SN-RIGHT"));

        // Live: Windows re-indexed after a dock re-plug — ids shuffled, hardware unchanged.
        var live = Topology(
            Live(1, -1920, 0, serial: "SN-LEFT"),                       // an extra/left screen
            Live(0, 0, 0, 2560, 1440, primary: true, serial: "SN-CENTER"),
            Live(2, 2560, 0, serial: "SN-RIGHT"));

        var result = _matcher.Match(profile, live);

        Assert.Equal(MatchConfidence.High, result.Confidence.Band);
        Assert.True(result.AllMatched);
        // Resolves to the *current* mstsc ids for center(0) and right(2), in saved order.
        Assert.Equal(new[] { 0, 2 }, result.ResolvedMonitorIds);
    }

    [Fact]
    public void Serial_match_survives_physical_rearrangement()
    {
        var profile = Profile(
            Saved(0, 0, 0, primary: true, serial: "SN-A"),
            Saved(1, 1920, 0, serial: "SN-B"));

        // Panels physically swapped sides, serials intact.
        var live = Topology(
            Live(5, 0, 0, primary: true, serial: "SN-B"),
            Live(6, 1920, 0, serial: "SN-A"));

        var result = _matcher.Match(profile, live);

        Assert.Equal(MatchConfidence.High, result.Confidence.Band);
        // A follows its panel to mstsc id 6, B to id 5.
        Assert.Equal(new[] { 6, 5 }, result.ResolvedMonitorIds);
    }

    [Fact]
    public void Identical_panels_no_serial_match_by_position()
    {
        // Two indistinguishable panels (same model, no serial). Only geometry can separate them.
        var profile = Profile(
            Saved(0, 0, 0, primary: true, manuf: "DEL", product: "U2419", device: "\\\\.\\DISPLAY1"),
            Saved(1, 1920, 0, manuf: "DEL", product: "U2419", device: "\\\\.\\DISPLAY2"));

        var live = Topology(
            Live(0, 0, 0, primary: true, manuf: "DEL", product: "U2419", device: "\\\\.\\DISPLAY9"),
            Live(1, 1920, 0, manuf: "DEL", product: "U2419", device: "\\\\.\\DISPLAY8"));

        var result = _matcher.Match(profile, live);

        Assert.True(result.AllMatched);
        Assert.True(result.Confidence.Value >= ConfidenceThresholds.Medium);
        Assert.Equal(new[] { 0, 1 }, result.ResolvedMonitorIds); // left→left, right→right
    }

    [Fact]
    public void Added_monitor_does_not_disturb_saved_selection()
    {
        var profile = Profile(
            Saved(0, 0, 0, primary: true, serial: "SN-A"),
            Saved(1, 1920, 0, serial: "SN-B"));

        // A third monitor appeared; saved two are unchanged.
        var live = Topology(
            Live(0, 0, 0, primary: true, serial: "SN-A"),
            Live(1, 1920, 0, serial: "SN-B"),
            Live(2, 3840, 0, serial: "SN-NEW"));

        var result = _matcher.Match(profile, live);

        Assert.Equal(MatchConfidence.High, result.Confidence.Band);
        Assert.Equal(2, result.MatchedCount);
        Assert.Equal(new[] { 0, 1 }, result.ResolvedMonitorIds); // new monitor stays unselected
    }

    [Fact]
    public void Resolution_change_same_spot_no_identity_is_medium()
    {
        var profile = Profile(Saved(0, 0, 0, 2560, 1440, primary: true, device: "\\\\.\\D1"));
        var live = Topology(Live(0, 0, 0, 1920, 1080, primary: true, device: "\\\\.\\OTHER"));

        var result = _matcher.Match(profile, live);

        Assert.True(result.AllMatched);
        Assert.Equal(MatchConfidence.Medium, result.Confidence.Band);
    }

    [Fact]
    public void Three_saved_but_only_one_present_is_low_confidence()
    {
        var profile = Profile(
            Saved(0, 0, 0, primary: true, serial: "SN-A"),
            Saved(1, 1920, 0, serial: "SN-B"),
            Saved(2, 3840, 0, serial: "SN-C"));

        var live = Topology(Live(0, 0, 0, primary: true, serial: "SN-A")); // laptop-only

        var result = _matcher.Match(profile, live);

        Assert.Equal(MatchConfidence.Low, result.Confidence.Band); // → prompt to reconfigure
        Assert.Equal(1, result.MatchedCount);
    }

    [Fact]
    public void Single_monitor_exact_match_is_high_and_deterministic()
    {
        var profile = Profile(Saved(0, 0, 0, primary: true, serial: "SN-ONLY"));
        var live = Topology(Live(3, 0, 0, primary: true, serial: "SN-ONLY"));

        var r1 = _matcher.Match(profile, live);
        var r2 = _matcher.Match(profile, live);

        Assert.Equal(MatchConfidence.High, r1.Confidence.Band);
        Assert.Equal(new[] { 3 }, r1.ResolvedMonitorIds);
        Assert.Equal(r1.ResolvedMonitorIds, r2.ResolvedMonitorIds); // deterministic
    }

    private static class ConfidenceThresholds
    {
        public const double Medium = 0.50;
    }
}
