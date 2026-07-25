using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RdpManager.Application.Abstractions;
using RdpManager.Domain.Entities;

namespace RdpManager.Presentation.ViewModels;

public enum DetailTab { Overview, Credential, Display, History, Advanced }

public sealed partial class MachineDetailViewModel : ObservableObject
{
    private readonly IReachabilityProbe _probe;

    [ObservableProperty] private Machine? _machine;
    [ObservableProperty] private DetailTab _tab = DetailTab.Overview;
    [ObservableProperty] private string _pingStatus = "Ping";
    [ObservableProperty] private bool _isPinging;

    public MachineDetailViewModel(IReachabilityProbe probe) => _probe = probe;

    public void Set(Machine machine) => Machine = machine;

    [RelayCommand] private void SelectTab(DetailTab tab) => Tab = tab;

    [RelayCommand]
    private async Task PingAsync(CancellationToken ct)
    {
        if (Machine is null) return;
        IsPinging = true;
        PingStatus = "Pinging…";
        try
        {
            var result = await _probe.CheckAsync(Machine.Host, ct);
            PingStatus = result.IsReachable ? $"Reachable · {result.RoundtripMs} ms" : $"Unreachable · {result.Detail}";
        }
        finally { IsPinging = false; }
    }
}
