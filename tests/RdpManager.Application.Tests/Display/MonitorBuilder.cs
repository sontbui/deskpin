using RdpManager.Application.Display;
using RdpManager.Domain.Entities;
using RdpManager.Domain.Enums;
using RdpManager.Domain.ValueObjects;

namespace RdpManager.Application.Tests.Display;

/// <summary>Terse builders so the matcher tests read like the scenarios they describe.</summary>
internal static class MonitorBuilder
{
    public static MonitorFingerprint Saved(
        int ordinal, int x, int y, int w = 1920, int h = 1080,
        bool primary = false, string? serial = null, string? device = null,
        string? manuf = null, string? product = null)
        => new(ordinal,
               new MonitorGeometry(x, y, w, h, Orientation.Landscape),
               device ?? $"\\\\.\\DISPLAY{ordinal}",
               primary, manuf, product, serial);

    public static MonitorInfo Live(
        int mstscId, int x, int y, int w = 1920, int h = 1080,
        bool primary = false, string? serial = null, string? device = null,
        string? manuf = null, string? product = null)
        => new()
        {
            MstscMonitorId = mstscId,
            Geometry = new MonitorGeometry(x, y, w, h, Orientation.Landscape),
            DevicePath = device ?? $"\\\\.\\DISPLAY{mstscId}",
            IsPrimary = primary,
            EdidManufacturer = manuf,
            EdidProductCode = product,
            EdidSerial = serial,
        };

    public static DisplayProfile Profile(params MonitorFingerprint[] monitors)
        => new(Guid.NewGuid(), monitors, DateTimeOffset.UnixEpoch);

    public static DisplayTopology Topology(params MonitorInfo[] monitors)
        => new(monitors);
}
