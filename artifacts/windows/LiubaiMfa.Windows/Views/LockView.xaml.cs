using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace LiubaiMfa.Windows.Views;

public sealed partial class LockView : UserControl
{
    public LockView()
    {
        InitializeComponent();
    }

    public event EventHandler? UnlockRequested;

    public string Message
    {
        get => LockMessage.Text;
        set => LockMessage.Text = value;
    }

    public string ButtonContent
    {
        get => UnlockButton.Content?.ToString() ?? string.Empty;
        set
        {
            UnlockButton.Content = value;
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(UnlockButton, value);
        }
    }

    public bool IsUnlockEnabled
    {
        get => UnlockButton.IsEnabled;
        set => UnlockButton.IsEnabled = value;
    }

    private void UnlockButton_Click(object sender, RoutedEventArgs e) =>
        UnlockRequested?.Invoke(this, EventArgs.Empty);
}
