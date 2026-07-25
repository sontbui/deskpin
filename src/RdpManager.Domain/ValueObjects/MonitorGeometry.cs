using RdpManager.Domain.Enums;

namespace RdpManager.Domain.ValueObjects;

/// <summary>
/// The position and size of a monitor in the virtual desktop, in physical pixels.
/// (X, Y) is the top-left corner; the primary monitor's top-left is the origin (0, 0),
/// so monitors left of / above primary have negative coordinates — exactly as Windows reports.
/// </summary>
public sealed record MonitorGeometry(int X, int Y, int Width, int Height, Orientation Orientation)
{
    public long Area => (long)Width * Height;

    /// <summary>Euclidean distance between the top-left corners of two monitors, in pixels.</summary>
    public double TopLeftDistanceTo(MonitorGeometry other)
    {
        double dx = X - other.X;
        double dy = Y - other.Y;
        return Math.Sqrt((dx * dx) + (dy * dy));
    }

    public bool SameResolution(MonitorGeometry other) =>
        Width == other.Width && Height == other.Height && Orientation == other.Orientation;
}
