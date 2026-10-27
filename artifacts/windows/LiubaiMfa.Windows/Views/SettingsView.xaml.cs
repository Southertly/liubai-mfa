using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace LiubaiMfa.Windows.Views;

public sealed partial class SettingsView : UserControl
{
    private readonly DispatcherTimer _statusTimer = new() { Interval = TimeSpan.FromSeconds(4) };
    public SettingsView()
    {
        InitializeComponent();
        _statusTimer.Tick += (_, _) => ClearStatus();
    }

    public event EventHandler? BackRequested;
    public event EventHandler? LockRequested;
    public event EventHandler? StartupProtectionRequested;
    public event EventHandler? ImportBackupRequested;
    public event EventHandler? ExportBackupRequested;

    public void FocusBackAction() => BackButton.Focus(FocusState.Programmatic);

    public void ApplyStartupProtection(bool enabled)
    {
        StartupProtectionStatusText.Text = enabled ? "已开启" : "未开启";
    }

    public void ShowStatus(string message, InfoBarSeverity severity)
    {
        StatusBar.Message = message;
        StatusBar.Severity = severity;
        StatusBar.IsOpen = true;
        _statusTimer.Stop();
        _statusTimer.Start();
    }

    public void ClearStatus()
    {
        _statusTimer.Stop();
        StatusBar.IsOpen = false;
        StatusBar.Message = string.Empty;
    }

    private void BackButton_Click(object sender, RoutedEventArgs e) => BackRequested?.Invoke(this, EventArgs.Empty);
    private void LockButton_Click(object sender, RoutedEventArgs e) => LockRequested?.Invoke(this, EventArgs.Empty);
    private void StartupProtectionButton_Click(object sender, RoutedEventArgs e) => StartupProtectionRequested?.Invoke(this, EventArgs.Empty);
    private void ImportBackupButton_Click(object sender, RoutedEventArgs e) => ImportBackupRequested?.Invoke(this, EventArgs.Empty);
    private void ExportBackupButton_Click(object sender, RoutedEventArgs e) => ExportBackupRequested?.Invoke(this, EventArgs.Empty);
}
