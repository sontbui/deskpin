using RdpManager.Domain.ValueObjects;

namespace RdpManager.Domain.Entities;

/// <summary>
/// A persisted snapshot of one monitor that the user selected for a session.
/// This is the match key — the app never stores a monitor <em>index</em>.
/// </summary>
public sealed class MonitorFingerprint : IMonitorIdentity
{
    public Guid Id { get; private set; } = Guid.NewGuid();

    /// <summary>Position of this monitor within the profile's selected set (for stable display order).</summary>
    public int OrdinalInProfile { get; private set; }

    public string? EdidManufacturer { get; private set; }
    public string? EdidProductCode { get; private set; }
    public string? EdidSerial { get; private set; }
    public string DevicePath { get; private set; } = string.Empty;
    public MonitorGeometry Geometry { get; private set; } = new(0, 0, 0, 0, Enums.Orientation.Landscape);
    public bool IsPrimary { get; private set; }

    private MonitorFingerprint() { } // EF

    public MonitorFingerprint(
        int ordinalInProfile,
        MonitorGeometry geometry,
        string devicePath,
        bool isPrimary,
        string? edidManufacturer = null,
        string? edidProductCode = null,
        string? edidSerial = null)
    {
        if (string.IsNullOrWhiteSpace(devicePath))
            throw new ArgumentException("DevicePath must not be empty.", nameof(devicePath));
        if (geometry.Width <= 0 || geometry.Height <= 0)
            throw new ArgumentException("Monitor must have positive dimensions.", nameof(geometry));

        OrdinalInProfile = ordinalInProfile;
        Geometry = geometry;
        DevicePath = devicePath;
        IsPrimary = isPrimary;
        EdidManufacturer = Normalize(edidManufacturer);
        EdidProductCode = Normalize(edidProductCode);
        EdidSerial = Normalize(edidSerial);
    }

    private static string? Normalize(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
