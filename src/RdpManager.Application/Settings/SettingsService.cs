using System.Globalization;
using RdpManager.Application.Abstractions;

namespace RdpManager.Application.Settings;

public enum CredentialBackend { CredentialManager, PromptEachTime }
public enum AppTheme { Dark, Light, System }

/// <summary>Typed access over the key/value settings store, with sane defaults.</summary>
public sealed class SettingsService
{
    private const string KeyBackend = "credential.backend";
    private const string KeyTheme = "general.theme";
    private const string KeyAutoIncludeMonitors = "display.autoIncludeNewMonitors";
    private const string KeyPreflight = "launch.preflight";

    private readonly ISettingsStore _store;
    public SettingsService(ISettingsStore store) => _store = store;

    public async Task<CredentialBackend> GetBackendAsync(CancellationToken ct) =>
        Enum.TryParse(await _store.GetAsync(KeyBackend, ct), out CredentialBackend v) ? v : CredentialBackend.CredentialManager;

    public Task SetBackendAsync(CredentialBackend v, CancellationToken ct) => _store.SetAsync(KeyBackend, v.ToString(), ct);

    public async Task<AppTheme> GetThemeAsync(CancellationToken ct) =>
        Enum.TryParse(await _store.GetAsync(KeyTheme, ct), out AppTheme v) ? v : AppTheme.Dark;

    public Task SetThemeAsync(AppTheme v, CancellationToken ct) => _store.SetAsync(KeyTheme, v.ToString(), ct);

    public async Task<bool> GetPreflightAsync(CancellationToken ct) =>
        !bool.TryParse(await _store.GetAsync(KeyPreflight, ct), out var v) || v; // default on

    public Task SetPreflightAsync(bool v, CancellationToken ct) =>
        _store.SetAsync(KeyPreflight, v.ToString(CultureInfo.InvariantCulture), ct);

    public async Task<bool> GetAutoIncludeMonitorsAsync(CancellationToken ct) =>
        !bool.TryParse(await _store.GetAsync(KeyAutoIncludeMonitors, ct), out var v) || v; // default on

    public Task SetAutoIncludeMonitorsAsync(bool v, CancellationToken ct) =>
        _store.SetAsync(KeyAutoIncludeMonitors, v.ToString(CultureInfo.InvariantCulture), ct);
}
