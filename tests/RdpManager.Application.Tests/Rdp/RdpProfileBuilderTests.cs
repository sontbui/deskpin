using RdpManager.Application.Rdp;
using RdpManager.Domain.Entities;
using RdpManager.Domain.Enums;
using RdpManager.Domain.ValueObjects;
using Xunit;

namespace RdpManager.Application.Tests.Rdp;

public sealed class RdpProfileBuilderTests
{
    private readonly RdpProfileBuilder _builder = new();

    private static Machine NewMachine(string host = "10.0.4.21", int port = 3389, string? user = "svc_build")
        => new("build-agent-01", HostAddress.Create(host, port), DateTimeOffset.UnixEpoch, user);

    [Fact]
    public void Multi_monitor_writes_multimon_and_selectedmonitors()
    {
        var rdp = _builder.Build(NewMachine(), new RdpOptions { SelectedMonitorIds = new[] { 0, 2 } });

        Assert.Contains("use multimon:i:1", rdp);
        Assert.Contains("selectedmonitors:s:0,2", rdp);
        Assert.Contains("full address:s:10.0.4.21", rdp);
        Assert.Contains("username:s:svc_build", rdp);
    }

    [Fact]
    public void Single_monitor_disables_multimon()
    {
        var rdp = _builder.Build(NewMachine(), new RdpOptions { SelectedMonitorIds = new[] { 1 } });

        Assert.Contains("use multimon:i:0", rdp);
        Assert.Contains("selectedmonitors:s:1", rdp);
    }

    [Fact]
    public void Never_emits_a_password_field()
    {
        var rdp = _builder.Build(NewMachine(), new RdpOptions { SelectedMonitorIds = new[] { 0, 1 } });

        // This is the security guarantee: no secret is ever written to disk by the builder.
        Assert.DoesNotContain("password 51:b:", rdp);
        Assert.DoesNotContain("password", rdp, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Non_default_port_is_appended_to_full_address()
    {
        var rdp = _builder.Build(NewMachine(host: "srv", port: 3390), new RdpOptions());
        Assert.Contains("full address:s:srv:3390", rdp);
    }

    [Fact]
    public void Gateway_produces_first_class_gateway_fields()
    {
        var m = NewMachine();
        m.SetGateway("gw.corp.local");
        var rdp = _builder.Build(m, new RdpOptions { SelectedMonitorIds = new[] { 0 } });

        Assert.Contains("gatewayhostname:s:gw.corp.local", rdp);
        Assert.Contains("gatewayusagemethod:i:1", rdp);
    }

    [Fact]
    public void Redirection_flags_map_to_rdp_lines()
    {
        var m = NewMachine();
        m.SetRedirection(RedirectionFlags.Clipboard | RedirectionFlags.Drives | RedirectionFlags.Audio);
        var rdp = _builder.Build(m, new RdpOptions { SelectedMonitorIds = new[] { 0 } });

        Assert.Contains("redirectclipboard:i:1", rdp);
        Assert.Contains("drivestoredirect:s:*", rdp);
        Assert.Contains("audiomode:i:0", rdp);   // 0 = play on this computer
        Assert.Contains("redirectprinters:i:0", rdp);
    }

    [Fact]
    public void Uses_crlf_line_endings()
    {
        var rdp = _builder.Build(NewMachine(), new RdpOptions { SelectedMonitorIds = new[] { 0 } });
        Assert.Contains("\r\n", rdp);
    }
}
