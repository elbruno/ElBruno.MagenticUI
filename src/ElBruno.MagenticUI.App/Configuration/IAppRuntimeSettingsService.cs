namespace ElBruno.MagenticUI.App.Configuration;

public interface IAppRuntimeSettingsService
{
    RuntimeSettingsSnapshot GetCurrentSettings();
    Task<RuntimeSettingsUpdateResult> SaveAsync(RuntimeSettingsSnapshot settings, CancellationToken ct = default);
}
