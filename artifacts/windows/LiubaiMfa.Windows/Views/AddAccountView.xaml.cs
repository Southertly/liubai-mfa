using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace LiubaiMfa.Windows.Views;

public sealed partial class AddAccountView : UserControl
{
    public event EventHandler? BackRequested;
    public event EventHandler? ScreenshotScanRequested;
    public event EventHandler? ImportImageRequested;
    public event EventHandler? ManualEntryRequested;

    public AddAccountView() => InitializeComponent();

    public void FocusPrimaryAction() => ScreenshotScanButton.Focus(FocusState.Programmatic);

    public void ShowStatus(string message, InfoBarSeverity severity)
    {
        StatusBar.Message = message;
        StatusBar.Severity = severity;
        StatusBar.IsOpen = true;
    }

    public void ClearStatus()
    {
        StatusBar.IsOpen = false;
        StatusBar.Message = string.Empty;
    }

    private void BackButton_Click(object sender, RoutedEventArgs e) => this.BackRequested?.Invoke(this, EventArgs.Empty);

    private void ScreenshotScanButton_Click(object sender, RoutedEventArgs e) =>
        this.ScreenshotScanRequested?.Invoke(this, EventArgs.Empty);

    private void ImportImageButton_Click(object sender, RoutedEventArgs e) =>
        this.ImportImageRequested?.Invoke(this, EventArgs.Empty);

    private void ManualEntryButton_Click(object sender, RoutedEventArgs e) =>
        this.ManualEntryRequested?.Invoke(this, EventArgs.Empty);

    private void AddAccountView_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Escape)
        {
            return;
        }

        this.BackRequested?.Invoke(this, EventArgs.Empty);
        e.Handled = true;
    }
}
