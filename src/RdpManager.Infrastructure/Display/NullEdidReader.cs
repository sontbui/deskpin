namespace RdpManager.Infrastructure.Display;

/// <summary>
/// Default EDID reader that returns no identity. The matcher then relies on the stable device
/// instance path + geometry, which already tolerates index churn.
///
/// FAST-FOLLOW (documented in the build plan roadmap): replace with a RegistryEdidReader that
/// reads <c>SYSTEM\CurrentControlSet\Enum\...\Device Parameters\EDID</c> for the monitor instance
/// and parses manufacturer (bytes 8–9), product code (10–11) and serial (12–15 + descriptors).
/// That upgrades identical-panel disambiguation from "position" to "serial".
/// </summary>
public sealed class NullEdidReader : IEdidReader
{
    public EdidInfo? TryRead(string gdiDeviceName) => null;
}
