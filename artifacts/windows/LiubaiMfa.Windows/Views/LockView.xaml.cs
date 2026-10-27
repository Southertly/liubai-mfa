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
    public event EventHandler? PasswordUnlockRequested;

    public string Message
    {
        get => LockMessage.Text;
        set => LockMessage.Text = value;
    }

    public string ButtonContent
    {
        get => GetUnlockButtonText() ?? UnlockButton.Content?.ToString() ?? string.Empty;
        set
        {
            SetUnlockButtonText(value);
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(UnlockButton, value);
        }
    }

    public bool IsUnlockEnabled
    {
        get => UnlockButton.IsEnabled;
        set
        {
            UnlockButton.IsEnabled = value;
            PasswordUnlockButton.IsEnabled = value;
        }
    }

    public void ShowHelloPrimary()
    {
        UnlockButton.Visibility = Visibility.Visible;
        SetUnlockButtonText("使用 Windows Hello 解锁");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(UnlockButton, "使用 Windows Hello 解锁");
        PasswordUnlockButton.Visibility = Visibility.Visible;
    }

    public void ShowPasswordPrimary()
    {
        UnlockButton.Visibility = Visibility.Visible;
        SetUnlockButtonText("使用电脑密码解锁");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(UnlockButton, "使用电脑密码解锁");
        PasswordUnlockButton.Visibility = Visibility.Collapsed;
    }

    private string? GetUnlockButtonText()
    {
        if (UnlockButton.Content is not Grid layout || layout.Children.Count < 2 || layout.Children[1] is not TextBlock text)
        {
            return null;
        }

        return text.Text;
    }

    private void SetUnlockButtonText(string value)
    {
        if (UnlockButton.Content is Grid layout && layout.Children.Count >= 2 && layout.Children[1] is TextBlock text)
        {
            text.Text = value;
            return;
        }

        UnlockButton.Content = value;
    }

    private void UnlockButton_Click(object sender, RoutedEventArgs e) =>
        UnlockRequested?.Invoke(this, EventArgs.Empty);

    private void PasswordUnlockButton_Click(object sender, RoutedEventArgs e) =>
        PasswordUnlockRequested?.Invoke(this, EventArgs.Empty);
}
