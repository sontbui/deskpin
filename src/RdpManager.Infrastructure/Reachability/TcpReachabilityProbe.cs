using System.Diagnostics;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using RdpManager.Application.Abstractions;
using RdpManager.Domain.ValueObjects;

namespace RdpManager.Infrastructure.Reachability;

/// <summary>
/// Pre-flight check: can we open TCP to the RDP port? Cheaper and more accurate than ICMP ping
/// (which is often firewalled) and it answers the only question that matters before launch —
/// will mstsc be able to connect. Cross-platform and fully cancellable.
/// </summary>
public sealed class TcpReachabilityProbe : IReachabilityProbe
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(3);
    private readonly ILogger<TcpReachabilityProbe> _logger;

    public TcpReachabilityProbe(ILogger<TcpReachabilityProbe> logger) => _logger = logger;

    public async Task<ReachabilityResult> CheckAsync(HostAddress host, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(host);
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(Timeout);

        var sw = Stopwatch.StartNew();
        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(host.Host, host.Port, timeoutCts.Token);
            sw.Stop();
            return new ReachabilityResult(true, (int)sw.ElapsedMilliseconds, $"port {host.Port} open");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw; // caller cancelled — propagate
        }
        catch (OperationCanceledException)
        {
            return new ReachabilityResult(false, null, $"timeout after {Timeout.TotalSeconds:0}s");
        }
        catch (SocketException ex)
        {
            _logger.LogDebug(ex, "Reachability check failed for {Host}", host);
            return new ReachabilityResult(false, null, ex.SocketErrorCode.ToString());
        }
    }
}
