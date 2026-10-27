using Windows.Storage;

namespace LiubaiMfa.Windows.Services;

public sealed class StartupProtectionSettings
{
    internal const string SettingKey = "Security.UseWindowsHelloOnOpen";

    private readonly Func<string, object?> _read;
    private readonly Action<string, object> _write;

    public StartupProtectionSettings()
        : this(
            key => ApplicationData.Current.LocalSettings.Values.TryGetValue(key, out var value) ? value : null,
            (key, value) => ApplicationData.Current.LocalSettings.Values[key] = value)
    {
    }

    internal StartupProtectionSettings(Func<string, object?> read, Action<string, object> write)
    {
        ArgumentNullException.ThrowIfNull(read);
        ArgumentNullException.ThrowIfNull(write);
        _read = read;
        _write = write;
    }

    public bool IsEnabled => _read(SettingKey) is bool value && value;

    public void SetEnabled(bool enabled) => _write(SettingKey, enabled);
}
