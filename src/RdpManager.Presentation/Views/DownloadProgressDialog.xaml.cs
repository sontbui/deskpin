using System;
using System.Threading;
using Microsoft.UI.Xaml.Controls;

namespace RdpManager.Presentation.Views;

public sealed partial class DownloadProgressDialog : ContentDialog
{
    private readonly CancellationTokenSource _cts = new();

    /// <summary>Cancelled when the user clicks Cancel.</summary>
    public CancellationToken Token => _cts.Token;

    public DownloadProgressDialog()
    {
        InitializeComponent();
        CloseButtonClick += (_, _) => _cts.Cancel();
    }

    /// <summary>Updates the bar. Pass a negative fraction for an indeterminate (unknown-size) download.</summary>
    public void Report(double fraction, string status)
    {
        if (fraction < 0)
        {
            Bar.IsIndeterminate = true;
        }
        else
        {
            Bar.IsIndeterminate = false;
            Bar.Value = Math.Clamp(fraction * 100.0, 0, 100);
        }
        PercentText.Text = status;
    }
}
