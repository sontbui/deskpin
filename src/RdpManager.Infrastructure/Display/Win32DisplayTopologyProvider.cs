using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using RdpManager.Application.Abstractions;
using RdpManager.Application.Display;
using RdpManager.Domain.Enums;
using RdpManager.Domain.ValueObjects;
using RdpManager.Infrastructure.Interop;

namespace RdpManager.Infrastructure.Display;

/// <summary>
/// Enumerates the live monitor topology using the Win32 display API (EnumDisplayMonitors +
/// GetMonitorInfo). This replaces the brief's "mstsc /l" step: mstsc /l renders a modal dialog
/// and cannot be parsed reliably, whereas this is silent and authoritative.
///
/// mstsc's <c>selectedmonitors</c> ids correspond to the EnumDisplayMonitors ordering, so the
/// enumeration index IS the id we later write to the .rdp — derived fresh every launch.
///
/// EDID identity (manufacturer/product/serial) enriches matching when available; it is filled by
/// <see cref="IEdidReader"/>. When EDID is hidden the matcher degrades gracefully to geometry,
/// which still tolerates index churn.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class Win32DisplayTopologyProvider : IDisplayTopologyProvider
{
    private readonly IEdidReader _edid;
    private readonly ILogger<Win32DisplayTopologyProvider> _logger;

    public Win32DisplayTopologyProvider(IEdidReader edid, ILogger<Win32DisplayTopologyProvider> logger)
    {
        _edid = edid;
        _logger = logger;
    }

    public Task<DisplayTopology> GetCurrentAsync(CancellationToken ct)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Display enumeration requires Windows.");

        ct.ThrowIfCancellationRequested();

        var monitors = new List<MonitorInfo>();
        var index = 0;

        bool Callback(IntPtr hMonitor, IntPtr hdc, ref NativeMethods.RECT _, IntPtr __)
        {
            var mi = new NativeMethods.MONITORINFOEX { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MONITORINFOEX>() };
            if (!NativeMethods.GetMonitorInfo(hMonitor, ref mi))
            {
                _logger.LogWarning("GetMonitorInfo failed for a monitor handle; skipping.");
                return true; // keep enumerating
            }

            var isPrimary = (mi.dwFlags & NativeMethods.MONITORINFOF_PRIMARY) != 0;
            var geometry = new MonitorGeometry(
                mi.rcMonitor.Left, mi.rcMonitor.Top, mi.rcMonitor.Width, mi.rcMonitor.Height, Orientation.Landscape);

            // Resolve a STABLE device path (monitor instance id), not the shuffling GDI number.
            var stablePath = ResolveStableDevicePath(mi.szDevice) ?? mi.szDevice;
            var edid = _edid.TryRead(mi.szDevice);

            monitors.Add(new MonitorInfo
            {
                // mstsc numbers monitors as (GDI display number - 1), e.g. \\.\DISPLAY5 -> id 4.
                // These ids can have gaps (0,1,4), so we must NOT use the enumeration index.
                MstscMonitorId = MstscIdFromDevice(mi.szDevice, index),
                DevicePath = stablePath,
                Geometry = geometry,
                IsPrimary = isPrimary,
                EdidManufacturer = edid?.Manufacturer,
                EdidProductCode = edid?.ProductCode,
                EdidSerial = edid?.Serial,
            });
            index++;
            return true;
        }

        if (!NativeMethods.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, Callback, IntPtr.Zero))
            throw new InvalidOperationException("EnumDisplayMonitors failed.");

        return Task.FromResult(new DisplayTopology(monitors));
    }

    /// <summary>Parses "\\.\DISPLAY5" -> 5 and returns the zero-based mstsc id (4). Falls back to the index.</summary>
    private static int MstscIdFromDevice(string gdiName, int fallback)
    {
        var n = 0; var any = false;
        foreach (var c in gdiName ?? string.Empty)
            if (char.IsDigit(c)) { n = (n * 10) + (c - '0'); any = true; }
        return any && n > 0 ? n - 1 : fallback;
    }

    private static string? ResolveStableDevicePath(string gdiDeviceName)
    {
        var dd = new NativeMethods.DISPLAY_DEVICE { cb = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.DISPLAY_DEVICE>() };
        return NativeMethods.EnumDisplayDevices(gdiDeviceName, 0, ref dd, 0) && !string.IsNullOrEmpty(dd.DeviceID)
            ? dd.DeviceID
            : null;
    }
}

/// <summary>Best-effort EDID identity for a GDI device name (e.g. <c>\\.\DISPLAY1</c>).</summary>
public interface IEdidReader
{
    EdidInfo? TryRead(string gdiDeviceName);
}

public sealed record EdidInfo(string? Manufacturer, string? ProductCode, string? Serial);
