using RdpManager.Domain.ValueObjects;

namespace RdpManager.Domain.Entities;

/// <summary>
/// The stable, matchable description of a single physical monitor.
/// Implemented by the persisted <see cref="MonitorFingerprint"/> and by the live
/// topology model in the Application layer, so the matcher can compare the two
/// without either side depending on the other's concrete type.
/// </summary>
public interface IMonitorIdentity
{
    /// <summary>EDID manufacturer PNP id (e.g. "DEL"). Null if the driver hides EDID.</summary>
    string? EdidManufacturer { get; }
    string? EdidProductCode { get; }
    /// <summary>EDID serial — the strongest identity signal. Often null on cheap panels.</summary>
    string? EdidSerial { get; }
    /// <summary>OS device path / interface name — stable per port, changes if you replug elsewhere.</summary>
    string DevicePath { get; }
    MonitorGeometry Geometry { get; }
    bool IsPrimary { get; }
}
