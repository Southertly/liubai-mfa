using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace LiubaiMfa.Windows;

public partial class App
{
    private void InitializeComponent()
    {
    }
}

public sealed partial class MainWindow
{
    private readonly Grid RootGrid = new();
    private readonly InfoBar StatusBar = new();
    private readonly TextBlock EmptyState = new();
    private readonly ListView AccountsList = new();
    private readonly Grid LockOverlay = new();
    private readonly TextBlock LockMessage = new();
    private readonly Button UnlockButton = new();

    private void InitializeComponent()
    {
    }
}
