using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace LiubaiMfa.Windows.Views;

public sealed partial class AddAccountView : UserControl
{
    public event EventHandler? BackRequested;
    public event EventHandler? ScreenshotScanRequested;
    public event EventHandler? ImportImageRequested;
    public event EventHandler? ManualEntryRequested;

    public AddAccountView() => InitializeComponent();

    public void FocusPrimaryAction() => ScreenshotScanButton.Focus(FocusState.Programmatic);

    private void BackRequested_Click(object sender, RoutedEventArgs e) => BackRequested?.Invoke(this, EventArgs.Empty);

    private void ScreenshotScanRequested_Click(object sender, RoutedEventArgs e) =>
        ScreenshotScanRequested?.Invoke(this, EventArgs.Empty);

    private void ImportImageRequested_Click(object sender, RoutedEventArgs e) =>
        ImportImageRequested?.Invoke(this, EventArgs.Empty);

    private void ManualEntryRequested_Click(object sender, RoutedEventArgs e) =>
        ManualEntryRequested?.Invoke(this, EventArgs.Empty);
}
