using Microsoft.Extensions.Logging;
using RdpManager.Application.Abstractions;
using RdpManager.Application.Common;
using RdpManager.Application.Display;
using RdpManager.Application.Rdp;
using RdpManager.Domain.Abstractions;
using RdpManager.Domain.Entities;
using RdpManager.Domain.Enums;

namespace RdpManager.Application.Sessions;

public enum SessionResultKind
{
    Launched,
    NeedsReconfiguration,
}

/// <summary>The result of the hero flow. NeedsReconfiguration carries the match so the UI can prefill the picker.</summary>
public sealed record SessionOutcome(
    SessionResultKind Kind,
    MatchConfidence? Confidence,
    bool Remapped,
    IReadOnlyList<int> Monitors,
    int? ProcessId,
    MatchResult? Match,
    Task? CredentialCleanup);

/// <summary>
/// The hero flow: reachability → live topology → match → generate → inject credential → launch,
/// with deterministic cleanup. Pure orchestration over interfaces (no I/O of its own), so it is
/// fully unit-testable with fakes.
/// </summary>
public sealed class RemoteSessionUseCase
{
    private readonly IMachineRepository _machines;
    private readonly IReachabilityProbe _probe;
    private readonly IDisplayTopologyProvider _topology;
    private readonly DisplayMatcher _matcher;
    private readonly IRdpProfileBuilder _builder;
    private readonly ICredentialStore _credentialStore;
    private readonly ICredentialInjector _credentialInjector;
    private readonly IRemoteLauncher _launcher;
    private readonly IHistoryRepository _history;
    private readonly IClock _clock;
    private readonly ILogger<RemoteSessionUseCase> _logger;
    private readonly TimeSpan _credentialGrace;
    private readonly IDriveRedirectionSettings? _driveRedirection;

    public RemoteSessionUseCase(
        IMachineRepository machines, IReachabilityProbe probe, IDisplayTopologyProvider topology,
        DisplayMatcher matcher, IRdpProfileBuilder builder, ICredentialStore credentialStore,
        ICredentialInjector credentialInjector, IRemoteLauncher launcher, IHistoryRepository history,
        IClock clock, ILogger<RemoteSessionUseCase> logger, TimeSpan? credentialGrace = null,
        IDriveRedirectionSettings? driveRedirection = null)
    {
        _machines = machines; _probe = probe; _topology = topology; _matcher = matcher;
        _builder = builder; _credentialStore = credentialStore; _credentialInjector = credentialInjector;
        _launcher = launcher; _history = history; _clock = clock; _logger = logger;
        _credentialGrace = credentialGrace ?? TimeSpan.FromSeconds(8);
        _driveRedirection = driveRedirection;
    }

    public async Task<Result<SessionOutcome>> ExecuteAsync(Guid machineId, CancellationToken ct)
    {
        var machine = await _machines.GetAsync(machineId, ct);
        if (machine is null) return Error.NotFound("Machine");

        // First remote (or explicitly not configured): send the user to the picker.
        if (!machine.IsDisplayConfigured || machine.DisplayProfile is null)
            return new SessionOutcome(SessionResultKind.NeedsReconfiguration, null, false, Array.Empty<int>(), null, null, null);

        // Pre-flight — never spawn a failing mstsc window.
        var reach = await _probe.CheckAsync(machine.Host, ct);
        if (!reach.IsReachable)
        {
            await _history.AddAsync(new SessionHistoryEntry(machine.Id, HistoryEventType.PreflightBlocked,
                _clock.UtcNow, reach.Detail), ct);
            return Error.Unreachable(machine.Host.Host);
        }

        var topology = await _topology.GetCurrentAsync(ct);
        var match = _matcher.Match(machine.DisplayProfile, topology);

        if (match.Confidence.Band == MatchConfidence.Low)
        {
            // Can't map confidently → let the UI prompt to reconfigure.
            return new SessionOutcome(SessionResultKind.NeedsReconfiguration, MatchConfidence.Low, false,
                match.ResolvedMonitorIds, null, match, null);
        }

        var remapped = match.Confidence.Band == MatchConfidence.Medium || !match.AllMatched;

        // Per-drive redirection (Files console): when the user picked specific drives, emit them
        // verbatim; otherwise the builder falls back to "*"/"" from the machine's flags.
        string? driveValue = null;
        if (_driveRedirection is not null)
        {
            var drives = await _driveRedirection.GetAsync(machine.Id, ct);
            if (drives.IsEnabled) driveValue = drives.ToRdpValue();
        }

        var rdpText = _builder.Build(machine, new RdpOptions
        {
            SelectedMonitorIds = match.ResolvedMonitorIds,
            DriveRedirectionValue = driveValue,
        });

        // Deliver the secret to mstsc via Credential Manager (never into the .rdp).
        Task? cleanup = null;
        if (machine.Credential.Kind == CredentialStoreKind.Dpapi && machine.Credential.Reference is not null)
        {
            using var secret = await _credentialStore.RevealAsync(machine.Credential, ct);
            await _credentialInjector.InjectAsync(machine.Host.Host, machine.Username ?? string.Empty, secret, ct);
            cleanup = ScheduleCredentialCleanupAsync(machine.Host.Host);
        }

        var handle = await _launcher.LaunchAsync(rdpText, ct);

        await _machines.MarkConnectedAsync(machine.Id, _clock.UtcNow, ct);

        if (remapped)
            await _history.AddAsync(new SessionHistoryEntry(machine.Id, HistoryEventType.LayoutRemapped,
                _clock.UtcNow, $"re-mapped to {match.ResolvedMonitorIds.Count} monitor(s)", match.Confidence.Band), ct);

        await _history.AddAsync(new SessionHistoryEntry(machine.Id, HistoryEventType.Session,
            _clock.UtcNow, $"launched on {match.ResolvedMonitorIds.Count} monitor(s)", match.Confidence.Band), ct);

        return new SessionOutcome(SessionResultKind.Launched, match.Confidence.Band, remapped,
            match.ResolvedMonitorIds, handle.ProcessId, match, cleanup);
    }

    private async Task ScheduleCredentialCleanupAsync(string host)
    {
        try
        {
            if (_credentialGrace > TimeSpan.Zero) await Task.Delay(_credentialGrace);
        }
        finally
        {
            try { await _credentialInjector.RemoveAsync(host, CancellationToken.None); }
            catch (Exception ex) { _logger.LogWarning(ex, "Failed to remove injected credential for {Host}", host); }
        }
    }
}
