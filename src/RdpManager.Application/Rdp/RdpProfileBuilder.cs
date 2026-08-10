using System.Globalization;
using System.Text;
using RdpManager.Domain.Entities;
using RdpManager.Domain.Enums;

namespace RdpManager.Application.Rdp;

/// <summary>
/// Pure generator of .rdp file text. Deterministic and dependency-free (golden-file testable).
///
/// SECURITY: this builder never emits a <c>password 51:b:</c> field. The secret is delivered
/// at launch through Windows Credential Manager (TERMSRV/&lt;host&gt;), so nothing sensitive
/// is ever written to disk by RDP Manager.
/// </summary>
public sealed class RdpProfileBuilder : IRdpProfileBuilder
{
    public string Build(Machine machine, RdpOptions options)
    {
        ArgumentNullException.ThrowIfNull(machine);
        ArgumentNullException.ThrowIfNull(options);

        var sb = new StringBuilder();
        void Line(string s) => sb.Append(s).Append("\r\n"); // .rdp uses CRLF

        var address = machine.Host.Port == Domain.ValueObjects.HostAddress.DefaultRdpPort
            ? machine.Host.Host
            : $"{machine.Host.Host}:{machine.Host.Port}";

        Line($"full address:s:{address}");
        if (!string.IsNullOrWhiteSpace(machine.Username))
            Line($"username:s:{machine.Username}");

        // Multi-monitor handling — the whole point of the app.
        var ids = options.SelectedMonitorIds;
        if (ids.Count > 1)
        {
            Line("use multimon:i:1");
            Line("selectedmonitors:s:" + string.Join(",", ids.Select(i => i.ToString(CultureInfo.InvariantCulture))));
        }
        else
        {
            Line("use multimon:i:0");
            if (ids.Count == 1)
                Line("selectedmonitors:s:" + ids[0].ToString(CultureInfo.InvariantCulture));
        }

        Line($"screen mode id:i:{options.ScreenModeId}");
        Line("dynamic resolution:i:" + (options.DynamicResolution ? "1" : "0"));

        // Every redirection is written explicitly from the machine's chosen flags, so only what the
        // user ticked in the app is requested. Sensitive ones (drives, smart cards, WebAuthn, camera)
        // default OFF, which is what keeps mstsc's consent dialog away unless the user opts in.
        var r = machine.Redirection;
        Line("redirectclipboard:i:" + Bit(r, RedirectionFlags.Clipboard));
        Line("redirectprinters:i:" + Bit(r, RedirectionFlags.Printers));
        Line("drivestoredirect:s:" + (options.DriveRedirectionValue ?? (r.HasFlag(RedirectionFlags.Drives) ? "*" : "")));
        Line("audiomode:i:" + (r.HasFlag(RedirectionFlags.Audio) ? "0" : "2"));   // 0 = play locally
        Line("audiocapturemode:i:" + Bit(r, RedirectionFlags.Microphone));
        Line("redirectsmartcards:i:" + Bit(r, RedirectionFlags.SmartCards));
        Line("redirectwebauthn:i:" + Bit(r, RedirectionFlags.WebAuthn));
        Line("camerastoredirect:s:" + (r.HasFlag(RedirectionFlags.Camera) ? "*" : ""));
        Line("redirectlocation:i:0");
        Line("keyboardhook:i:2");

        // Gateway (jump host) as a first-class field, not a raw override.
        if (!string.IsNullOrWhiteSpace(machine.Gateway))
        {
            Line($"gatewayhostname:s:{machine.Gateway}");
            Line("gatewayusagemethod:i:1");
            Line("gatewaycredentialssource:i:0");
            Line("gatewayprofileusagemethod:i:1");
        }

        Line("authentication level:i:2");
        Line("prompt for credentials:i:0"); // creds come from Credential Manager
        Line("administrative session:i:0");

        return sb.ToString();
    }

    private static string Bit(RedirectionFlags flags, RedirectionFlags flag) => flags.HasFlag(flag) ? "1" : "0";
}
