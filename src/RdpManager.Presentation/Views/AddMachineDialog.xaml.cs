using System;
using System.Linq;
using Microsoft.UI.Xaml.Controls;
using RdpManager.Application.Machines;
using RdpManager.Domain.Entities;
using RdpManager.Domain.Enums;

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
        ApplyRedirection(RedirectionFlags.Default);
        PrimaryButtonClick += OnPrimary;
    }

    /// <summary>Puts the dialog into edit mode, pre-filled with an existing machine.</summary>
    public void Prefill(Machine machine)
    {
        Title = "Edit machine";
        NameBox.Text = machine.Name;
        HostBox.Text = machine.Host.Host;
        PortBox.Text = machine.Host.Port.ToString(System.Globalization.CultureInfo.InvariantCulture);
        UserBox.Text = machine.Username ?? string.Empty;
        TagsBox.Text = string.Join(", ", machine.Tags.Select(t => t.Name));
        ApplyRedirection(machine.Redirection);
    }

    private void ApplyRedirection(RedirectionFlags r)
    {
        ClipboardCheck.IsChecked = r.HasFlag(RedirectionFlags.Clipboard);
        PrintersCheck.IsChecked = r.HasFlag(RedirectionFlags.Printers);
        AudioCheck.IsChecked = r.HasFlag(RedirectionFlags.Audio);
        MicCheck.IsChecked = r.HasFlag(RedirectionFlags.Microphone);
        DrivesCheck.IsChecked = r.HasFlag(RedirectionFlags.Drives);
        WebAuthnCheck.IsChecked = r.HasFlag(RedirectionFlags.WebAuthn);
        CameraCheck.IsChecked = r.HasFlag(RedirectionFlags.Camera);
        SmartCardCheck.IsChecked = r.HasFlag(RedirectionFlags.SmartCards);
    }

    private RedirectionFlags ReadRedirection()
    {
        var r = RedirectionFlags.None;
        if (ClipboardCheck.IsChecked == true) r |= RedirectionFlags.Clipboard;
        if (PrintersCheck.IsChecked == true) r |= RedirectionFlags.Printers;
        if (AudioCheck.IsChecked == true) r |= RedirectionFlags.Audio;
        if (MicCheck.IsChecked == true) r |= RedirectionFlags.Microphone;
        if (DrivesCheck.IsChecked == true) r |= RedirectionFlags.Drives;
        if (WebAuthnCheck.IsChecked == true) r |= RedirectionFlags.WebAuthn;
        if (CameraCheck.IsChecked == true) r |= RedirectionFlags.Camera;
        if (SmartCardCheck.IsChecked == true) r |= RedirectionFlags.SmartCards;
        return r;
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
        Result = new CreateMachineRequest(name, host, port, username, tags, null, null, ReadRedirection());
    }
}
