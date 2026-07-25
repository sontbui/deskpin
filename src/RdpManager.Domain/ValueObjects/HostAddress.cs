using System.Net;

namespace RdpManager.Domain.ValueObjects;

/// <summary>
/// A validated remote host: either a DNS name or an IP literal, plus a TCP port.
/// Immutable value object; equality is by value.
/// </summary>
public sealed record HostAddress
{
    public const int DefaultRdpPort = 3389;

    public string Host { get; }
    public int Port { get; }

    private HostAddress(string host, int port)
    {
        Host = host;
        Port = port;
    }

    public static HostAddress Create(string? host, int port = DefaultRdpPort)
    {
        if (string.IsNullOrWhiteSpace(host))
            throw new ArgumentException("Host must not be empty.", nameof(host));

        host = host.Trim();

        if (port is < 1 or > 65535)
            throw new ArgumentOutOfRangeException(nameof(port), port, "Port must be 1..65535.");

        // Accept an IP literal or a plausibly-valid DNS host. We intentionally do
        // NOT resolve DNS here — that is I/O and belongs in a service, not a value object.
        if (!IPAddress.TryParse(host, out _) && !IsPlausibleDnsName(host))
            throw new ArgumentException($"'{host}' is not a valid host or IP address.", nameof(host));

        return new HostAddress(host, port);
    }

    private static bool IsPlausibleDnsName(string host)
    {
        if (host.Length > 253) return false;
        foreach (var label in host.Split('.'))
        {
            if (label.Length is 0 or > 63) return false;
            // A label may not start or end with a hyphen (RFC 1035 §2.3.1).
            if (label[0] == '-' || label[^1] == '-') return false;
            foreach (var ch in label)
                if (!char.IsLetterOrDigit(ch) && ch != '-')
                    return false;
        }
        return true;
    }

    public override string ToString() => Port == DefaultRdpPort ? Host : $"{Host}:{Port}";
}
