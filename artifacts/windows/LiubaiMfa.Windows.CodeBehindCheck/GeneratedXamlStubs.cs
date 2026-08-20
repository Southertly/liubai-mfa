using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using LiubaiMfa.Windows.Views;

namespace LiubaiMfa.Windows
{
    public partial class App
    {
        private void InitializeComponent()
        {
        }
    }

    public sealed partial class MainWindow
    {
        private readonly Grid RootGrid = new();
        private readonly Grid TitleBarDragRegion = new();
        private readonly AuthenticatorView AuthenticatorView = new();
        private readonly AddAccountView AddAccountView = new();
        private readonly LockView LockView = new();

        private void InitializeComponent()
        {
        }
    }
}

namespace LiubaiMfa.Windows.Views
{
    public sealed partial class AddAccountView
    {
        private readonly InfoBar StatusBar = new();
        private readonly Button ScreenshotScanButton = new();

        private void InitializeComponent()
        {
        }
    }

    public sealed partial class AuthenticatorView
    {
        private readonly InfoBar StatusBar = new();
        private readonly Button AddButton = new();
        private readonly Button LockButton = new();
        private readonly TextBlock ProtectionStatusText = new();
        private readonly AutoSuggestBox SearchBox = new();
        private readonly ListView GroupsList = new();
        private readonly StackPanel EmptyState = new();
        private readonly StackPanel NoResultsState = new();
        private readonly ListView AccountsList = new();

        private void InitializeComponent()
        {
        }
    }

    public sealed partial class LockView
    {
        private readonly TextBlock LockMessage = new();
        private readonly Button UnlockButton = new();

        private void InitializeComponent()
        {
        }
    }
}
