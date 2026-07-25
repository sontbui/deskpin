namespace RdpManager.Application.Rdp;

/// <summary>Launch-time options that shape the generated .rdp, none of them secret.</summary>
public sealed record RdpOptions
{
    /// <summary>Live mstsc monitor ids for this session, from the matcher. Empty = use primary only.</summary>
    public IReadOnlyList<int> SelectedMonitorIds { get; init; } = Array.Empty<int>();

    /// <summary>2 = full screen (default for RDP Manager sessions).</summary>
    public int ScreenModeId { get; init; } = 2;

    public bool DynamicResolution { get; init; } = true;
}
