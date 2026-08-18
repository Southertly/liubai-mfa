using LiubaiMfa.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace LiubaiMfa.Windows.Services;

public sealed class BackupDialogService
{
    private readonly Func<ContentDialog, long, Task<ContentDialogResult>> _showDialog;
    private readonly Func<long, bool> _isSessionCurrent;

    public BackupDialogService(
        Func<ContentDialog, long, Task<ContentDialogResult>> showDialog,
        Func<long, bool> isSessionCurrent)
    {
        _showDialog = showDialog;
        _isSessionCurrent = isSessionCurrent;
    }

    public async Task<bool> ConfirmExportAsync(XamlRoot root, long token, string recoveryKey, Action<string> copyText)
    {
        if (!_isSessionCurrent(token))
        {
            return false;
        }

        var dialog = new ContentDialog
        {
            XamlRoot = root,
            Title = "先保存恢复密钥",
            PrimaryButtonText = "保存备份文件",
            CloseButtonText = "取消",
            IsPrimaryButtonEnabled = false,
            DefaultButton = ContentDialogButton.None,
        };
        var copyButton = new Button { Content = "复制恢复密钥", HorizontalAlignment = HorizontalAlignment.Stretch };
        copyButton.Click += (_, _) =>
        {
            if (_isSessionCurrent(token))
            {
                copyText(recoveryKey);
                copyButton.Content = "恢复密钥已复制";
                dialog.IsPrimaryButtonEnabled = true;
            }
        };
        dialog.Content = new StackPanel
        {
            Spacing = 12,
            Children =
            {
                new TextBlock { Text = "恢复密钥不会写入备份文件。丢失后无法恢复，请与备份文件分开保存。", TextWrapping = TextWrapping.Wrap },
                new TextBlock { Text = recoveryKey, FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"), IsTextSelectionEnabled = true, TextWrapping = TextWrapping.Wrap },
                copyButton,
            },
        };

        return await _showDialog(dialog, token) == ContentDialogResult.Primary && _isSessionCurrent(token);
    }

    public async Task<string?> RequestRecoveryKeyAsync(XamlRoot root, long token)
    {
        if (!_isSessionCurrent(token))
        {
            return null;
        }

        var keyBox = new PasswordBox { PlaceholderText = "粘贴恢复密钥", PasswordRevealMode = PasswordRevealMode.Peek };
        var dialog = new ContentDialog
        {
            XamlRoot = root,
            Title = "导入加密备份",
            Content = keyBox,
            PrimaryButtonText = "导入",
            SecondaryButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
        };
        if (await _showDialog(dialog, token) != ContentDialogResult.Primary || !_isSessionCurrent(token))
        {
            keyBox.Password = string.Empty;
            return null;
        }

        var recoveryKey = keyBox.Password;
        keyBox.Password = string.Empty;
        return recoveryKey;
    }
}
