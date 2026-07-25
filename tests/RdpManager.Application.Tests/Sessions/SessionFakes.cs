using System.Security;
using RdpManager.Application.Abstractions;
using RdpManager.Application.Display;
using RdpManager.Domain.Abstractions;
using RdpManager.Domain.Entities;
using RdpManager.Domain.ValueObjects;

namespace RdpManager.Application.Tests.Sessions;

internal sealed class FixedClock : IClock { public DateTimeOffset UtcNow => DateTimeOffset.UnixEpoch; }

internal sealed class FakeMachineRepo : IMachineRepository
{
    private readonly Dictionary<Guid, Machine> _m = new();
    public int UpdateCount;
    public void Seed(Machine m) => _m[m.Id] = m;
    public Task<Machine?> GetAsync(Guid id, CancellationToken ct) => Task.FromResult(_m.GetValueOrDefault(id));
    public Task<IReadOnlyList<Machine>> ListAsync(CancellationToken ct) => Task.FromResult((IReadOnlyList<Machine>)_m.Values.ToList());
    public Task AddAsync(Machine m, CancellationToken ct) { _m[m.Id] = m; return Task.CompletedTask; }
    public Task UpdateAsync(Machine m, CancellationToken ct) { _m[m.Id] = m; UpdateCount++; return Task.CompletedTask; }
    public int MarkConnectedCount;
    public Task MarkConnectedAsync(Guid id, DateTimeOffset when, CancellationToken ct) { MarkConnectedCount++; return Task.CompletedTask; }
    public Task SaveDisplayProfileAsync(Guid machineId, IReadOnlyList<MonitorFingerprint> monitors, DateTimeOffset when, CancellationToken ct) => Task.CompletedTask;
    public Task RemoveAsync(Guid id, CancellationToken ct) { _m.Remove(id); return Task.CompletedTask; }
}

internal sealed class FakeProbe : IReachabilityProbe
{
    public bool Reachable = true;
    public Task<ReachabilityResult> CheckAsync(HostAddress h, CancellationToken ct)
        => Task.FromResult(new ReachabilityResult(Reachable, Reachable ? 12 : null, Reachable ? "open" : "timeout"));
}

internal sealed class FakeTopology : IDisplayTopologyProvider
{
    public DisplayTopology Value = new(Array.Empty<MonitorInfo>());
    public Task<DisplayTopology> GetCurrentAsync(CancellationToken ct) => Task.FromResult(Value);
}

internal sealed class FakeCredStore : ICredentialStore
{
    public Task<CredentialRef> ProtectAsync(SecureString s, CancellationToken ct) => Task.FromResult(CredentialRef.Dpapi("ref1"));
    public Task<SecureString> RevealAsync(CredentialRef r, CancellationToken ct)
    { var ss = new SecureString(); foreach (var c in "pw") ss.AppendChar(c); ss.MakeReadOnly(); return Task.FromResult(ss); }
    public Task RemoveAsync(CredentialRef r, CancellationToken ct) => Task.CompletedTask;
}

internal sealed class FakeInjector : ICredentialInjector
{
    public int Injected, Removed;
    public Task InjectAsync(string h, string u, SecureString s, CancellationToken ct) { Injected++; return Task.CompletedTask; }
    public Task RemoveAsync(string h, CancellationToken ct) { Removed++; return Task.CompletedTask; }
}

internal sealed class FakeLauncher : IRemoteLauncher
{
    public int Launches; public string? LastRdp;
    public Task<LaunchHandle> LaunchAsync(string rdp, CancellationToken ct) { Launches++; LastRdp = rdp; return Task.FromResult(new LaunchHandle(1234)); }
}

internal sealed class FakeHistory : IHistoryRepository
{
    public readonly List<SessionHistoryEntry> Entries = new();
    public Task AddAsync(SessionHistoryEntry e, CancellationToken ct) { Entries.Add(e); return Task.CompletedTask; }
    public Task<IReadOnlyList<SessionHistoryEntry>> ListForMachineAsync(Guid id, int take, CancellationToken ct) => Task.FromResult((IReadOnlyList<SessionHistoryEntry>)Entries);
    public Task<IReadOnlyList<SessionHistoryEntry>> ListRecentAsync(int take, CancellationToken ct) => Task.FromResult((IReadOnlyList<SessionHistoryEntry>)Entries);
}
