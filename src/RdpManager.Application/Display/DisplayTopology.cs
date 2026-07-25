using RdpManager.Domain.Entities;
using RdpManager.Domain.ValueObjects;

namespace RdpManager.Application.Display;

/// <summary>
/// A live monitor as reported by the OS at launch time. Implements the same
/// <see cref="IMonitorIdentity"/> contract as the persisted fingerprint so the
/// matcher compares like with like. <see cref="MstscMonitorId"/> is the value
/// this monitor contributes to the RDP <c>selectedmonitors</c> field right now —
/// it is derived from the live enumeration and is NEVER persisted.
/// </summary>
public sealed class MonitorInfo : IMonitorIdentity
{
    public required int MstscMonitorId { get; init; }
    public string? EdidManufacturer { get; init; }
    public string? EdidProductCode { get; init; }
    public string? EdidSerial { get; init; }
    public required string DevicePath { get; init; }
    public required MonitorGeometry Geometry { get; init; }
    public bool IsPrimary { get; init; }
}

/// <summary>The full set of monitors currently attached, in OS enumeration order.</summary>
public sealed class DisplayTopology
{
    public IReadOnlyList<MonitorInfo> Monitors { get; }

    public DisplayTopology(IEnumerable<MonitorInfo> monitors)
    {
        Monitors = monitors?.ToList() ?? throw new ArgumentNullException(nameof(monitors));
    }

    public int Count => Monitors.Count;
    public MonitorInfo? Primary => Monitors.FirstOrDefault(m => m.IsPrimary) ?? Monitors.FirstOrDefault();
}
