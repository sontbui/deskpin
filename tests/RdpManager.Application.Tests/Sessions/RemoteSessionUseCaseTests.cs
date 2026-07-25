using Microsoft.Extensions.Logging.Abstractions;
using RdpManager.Application.Common;
using RdpManager.Application.Display;
using RdpManager.Application.Rdp;
using RdpManager.Application.Sessions;
using RdpManager.Domain.Entities;
using RdpManager.Domain.Enums;
using RdpManager.Domain.ValueObjects;
using Xunit;

namespace RdpManager.Application.Tests.Sessions;

public sealed class RemoteSessionUseCaseTests
{
    private readonly FixedClock _clock = new();
    private readonly FakeMachineRepo _repo = new();
    private readonly FakeProbe _probe = new();
    private readonly FakeTopology _topo = new();
    private readonly FakeInjector _injector = new();
    private readonly FakeLauncher _launcher = new();
    private readonly FakeHistory _history = new();

    private RemoteSessionUseCase Sut() => new(
        _repo, _probe, _topo, new DisplayMatcher(), new RdpProfileBuilder(),
        new FakeCredStore(), _injector, _launcher, _history, _clock,
        NullLogger<RemoteSessionUseCase>.Instance, TimeSpan.Zero);

    private static MonitorFingerprint S(int o, int x, int y, int w = 1920, int h = 1080, bool p = false, string? ser = null, string? dev = null)
        => new(o, new MonitorGeometry(x, y, w, h, Orientation.Landscape), dev ?? $"D{o}", p, null, null, ser);
    private static MonitorInfo L(int id, int x, int y, int w = 1920, int h = 1080, bool p = false, string? ser = null, string? dev = null)
        => new() { MstscMonitorId = id, Geometry = new MonitorGeometry(x, y, w, h, Orientation.Landscape), DevicePath = dev ?? $"D{id}", IsPrimary = p, EdidSerial = ser };

    private Machine Configured(params MonitorFingerprint[] mons)
    {
        var m = new Machine("box", HostAddress.Create("10.0.0.1"), _clock.UtcNow, "user");
        m.SetCredential(CredentialRef.Dpapi("ref1"));
        m.ConfigureDisplay(new DisplayProfile(m.Id, mons, _clock.UtcNow));
        _repo.Seed(m);
        return m;
    }

    [Fact]
    public async Task Unknown_machine_returns_not_found()
    {
        var r = await Sut().ExecuteAsync(Guid.NewGuid(), default);
        Assert.False(r.IsSuccess);
        Assert.Equal(ErrorKind.NotFound, r.Error!.Kind);
    }

    [Fact]
    public async Task Unconfigured_machine_requests_reconfiguration_without_launching()
    {
        var m = new Machine("box", HostAddress.Create("10.0.0.1"), _clock.UtcNow, "user");
        _repo.Seed(m);
        var r = await Sut().ExecuteAsync(m.Id, default);
        Assert.True(r.IsSuccess);
        Assert.Equal(SessionResultKind.NeedsReconfiguration, r.Value.Kind);
        Assert.Equal(0, _launcher.Launches);
    }

    [Fact]
    public async Task High_confidence_launches_injects_and_removes_credential_and_writes_no_secret()
    {
        var m = Configured(S(0, 0, 0, p: true, ser: "SN-A"), S(1, 1920, 0, ser: "SN-B"));
        _topo.Value = new DisplayTopology(new[] { L(0, 0, 0, p: true, ser: "SN-A"), L(1, 1920, 0, ser: "SN-B") });

        var r = await Sut().ExecuteAsync(m.Id, default);
        if (r.Value.CredentialCleanup is not null) await r.Value.CredentialCleanup;

        Assert.Equal(SessionResultKind.Launched, r.Value.Kind);
        Assert.False(r.Value.Remapped);
        Assert.Equal(new[] { 0, 1 }, r.Value.Monitors);
        Assert.Equal(1, _launcher.Launches);
        Assert.Equal(1, _injector.Injected);
        Assert.Equal(1, _injector.Removed);
        Assert.DoesNotContain("password", _launcher.LastRdp!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Unreachable_host_is_blocked_before_launch_and_logged()
    {
        var m = Configured(S(0, 0, 0, p: true, ser: "SN-A"));
        _topo.Value = new DisplayTopology(new[] { L(0, 0, 0, p: true, ser: "SN-A") });
        _probe.Reachable = false;

        var r = await Sut().ExecuteAsync(m.Id, default);

        Assert.False(r.IsSuccess);
        Assert.Equal(ErrorKind.Unreachable, r.Error!.Kind);
        Assert.Equal(0, _launcher.Launches);
        Assert.Contains(_history.Entries, e => e.Type == HistoryEventType.PreflightBlocked);
    }

    [Fact]
    public async Task Low_confidence_requests_reconfiguration_without_launching()
    {
        var m = Configured(S(0, 0, 0, p: true, ser: "SN-A"), S(1, 1920, 0, ser: "SN-B"), S(2, 3840, 0, ser: "SN-C"));
        _topo.Value = new DisplayTopology(new[] { L(0, 0, 0, p: true, ser: "SN-A") });

        var r = await Sut().ExecuteAsync(m.Id, default);

        Assert.Equal(SessionResultKind.NeedsReconfiguration, r.Value.Kind);
        Assert.Equal(MatchConfidence.Low, r.Value.Confidence);
        Assert.Equal(0, _launcher.Launches);
    }

    [Fact]
    public async Task Medium_confidence_launches_and_records_a_remap()
    {
        var m = Configured(S(0, 0, 0, 2560, 1440, p: true, dev: "D1"));
        _topo.Value = new DisplayTopology(new[] { L(0, 0, 0, 1920, 1080, p: true, dev: "OTHER") });

        var r = await Sut().ExecuteAsync(m.Id, default);
        if (r.Value.CredentialCleanup is not null) await r.Value.CredentialCleanup;

        Assert.Equal(SessionResultKind.Launched, r.Value.Kind);
        Assert.True(r.Value.Remapped);
        Assert.Equal(MatchConfidence.Medium, r.Value.Confidence);
        Assert.Contains(_history.Entries, e => e.Type == HistoryEventType.LayoutRemapped);
    }
}
