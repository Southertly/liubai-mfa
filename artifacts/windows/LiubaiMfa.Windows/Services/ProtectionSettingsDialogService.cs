using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace LiubaiMfa.Windows.Services;

public sealed class ProtectionSettingsDialogService
{
    private readonly Func<ContentDialog, long, Task<ContentDialogResult>> _showDialog;
    private readonly Func<long, bool> _isSessionCurrent;

    public ProtectionSettingsDialogService(
        Func<ContentDialog, long, Task<ContentDialogResult>> showDialog,
        Func<long, bool> isSessionCurrent)
    {
        _showDialog = showDialog;
        _isSessionCurrent = isSessionCurrent;
    }

    public async Task<bool?> ShowAsync(XamlRoot root, long token, bool currentValue)
    {
        if (!_isSessionCurrent(token))
        {
            return null;
        }

        var toggle = new ToggleSwitch
        {
            Header = "打开应用时启用安全锁定",
            OffContent = "关闭",
            OnContent = "开启",
            IsOn = currentValue,
        };
        var dialog = new ContentDialog
        {
            XamlRoot = root,
            Title = "启动保护",
            PrimaryButtonText = "保存",
            SecondaryButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
            Content = new StackPanel
            {
                Spacing = 12,
                Children =
                {
                    toggle,
                    new TextBlock
                    {
                        Text = "开启后，启动应用、手动锁定和离开 1 分钟后需要验证身份。优先使用 Windows Hello，不可用时使用电脑密码；查看密钥和导出备份始终单独验证。",
                        TextWrapping = TextWrapping.Wrap,
                    },
                },
            },
        };

        if (await _showDialog(dialog, token) != ContentDialogResult.Primary || !_isSessionCurrent(token))
        {
            return null;
        }

        return toggle.IsOn;
    }
}
