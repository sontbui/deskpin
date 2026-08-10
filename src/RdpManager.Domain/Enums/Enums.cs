namespace RdpManager.Domain.Enums;

/// <summary>How many monitors a session is configured to span.</summary>
public enum DisplayMode
{
    NotConfigured = 0,
    SingleMonitor = 1,
    MultiMonitor = 2,
}

public enum ConnectionStatus
{
    Unknown = 0,
    Online = 1,
    HighLatency = 2,
    Offline = 3,
}

/// <summary>Confidence band produced by the <c>DisplayMatcher</c>.</summary>
public enum MatchConfidence
{
    /// <summary>&lt; 0.5 — cannot map reliably; ask the user to reconfigure.</summary>
    Low = 0,
    /// <summary>0.5–0.85 — remap and proceed, but surface an undo affordance.</summary>
    Medium = 1,
    /// <summary>0.85 or above — remap silently.</summary>
    High = 2,
}

public enum Orientation
{
    Landscape = 0,
    Portrait = 90,
    LandscapeFlipped = 180,
    PortraitFlipped = 270,
}

public enum CredentialStoreKind
{
    None = 0,
    Dpapi = 1,
    CredentialManager = 2,
    PromptEachTime = 3,
}

public enum HistoryEventType
{
    Session = 0,
    LayoutRemapped = 1,
    Ping = 2,
    PreflightBlocked = 3,
    DisplayConfigured = 4,
}

[Flags]
public enum RedirectionFlags
{
    None = 0,
    Clipboard = 1 << 0,
    Printers = 1 << 1,
    Drives = 1 << 2,
    Audio = 1 << 3,        // audio playback (remote → local speakers)
    Usb = 1 << 4,
    SmartCards = 1 << 5,
    WebAuthn = 1 << 6,     // Windows Hello / security keys
    Microphone = 1 << 7,   // audio capture (local mic → remote)
    Camera = 1 << 8,

    /// <summary>Low-risk defaults that don't trigger mstsc's consent prompt.</summary>
    Default = Clipboard | Printers | Audio,
}

/// <summary>Which way a file transfer moves relative to this machine.</summary>
public enum TransferDirection
{
    /// <summary>Local → remote ("send").</summary>
    Upload = 0,
    /// <summary>Remote → local ("get").</summary>
    Download = 1,
}

/// <summary>Lifecycle of one <c>TransferJob</c>. Terminal states: Completed, Failed, Skipped, Cancelled.</summary>
public enum TransferStatus
{
    Queued = 0,
    Active = 1,
    Completed = 2,
    Failed = 3,
    Skipped = 4,
    Cancelled = 5,
}
