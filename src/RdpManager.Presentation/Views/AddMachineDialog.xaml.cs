using System;
using System.Globalization;
using System.Linq;
using Microsoft.UI.Xaml.Controls;
using RdpManager.Application.Machines;
using RdpManager.Domain.Entities;

namespace RdpManager.Presentation.Views;

public sealed partial class AddMachineDialog : ContentDialog
{
    /// <summary>The captured input, or null if the user cancelled or entered invalid data.</summary>
    public CreateMachineRequest? Result { get; private set; }

    /// <summary>Typed password (empty means "don't change / prompt at connect").</summary>
    public string Password => PasswordBox.Password ?? string.Empty;

    public AddMachineDialog()
    {
        InitializeComponent();
        PrimaryButtonClick += OnPrimary;
    }

    /// <summary>Puts the dialog into edit mode, pre-filled with an existing machine.</summary>
    public void Prefill(Machine machine)
    {
        Title = "Edit machine";
        NameBox.Text = machine.Name;
        HostBox.Text = machine.Host.Host;
        PortBox.Text = machine.Host.Port.ToString(CultureInfo.InvariantCulture);
        UserBox.Text = machine.Username ?? string.Empty;
        TagsBox.Text = string.Join(", ", machine.Tags.Select(t => t.Name));
    }

    private void OnPrimary(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        var name = NameBox.Text?.Trim();
        var host = HostBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(host))
        {
            args.Cancel = true; // keep the dialog open; required fields missing
            return;
        }

        if (!int.TryParse(PortBox.Text, out var port)) port = 3389;

        var tags = (TagsBox.Text ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var username = string.IsNullOrWhiteSpace(UserBox.Text) ? null : UserBox.Text.Trim();
        Result = new CreateMachineRequest(name, host, port, username, tags, null, null);
    }
}
