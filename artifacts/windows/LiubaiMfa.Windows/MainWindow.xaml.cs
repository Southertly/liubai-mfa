using LiubaiMfa.Core;
using LiubaiMfa.Core.Models;
using LiubaiMfa.Windows.Services;
using LiubaiMfa.Windows.ViewModels;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Runtime.InteropServices;
using Windows.ApplicationModel.DataTransfer;
using WinRT.Interop;

namespace LiubaiMfa.Windows;

public sealed partial class MainWindow : Window
{
    private static readonly TimeSpan BackgroundLockDelay = TimeSpan.FromMinutes(1);
    private readonly WindowsHelloService _windowsHello;
    private readonly NativeFileService _files;
    private readonly WindowPlacementService _placementService;
    private readonly DispatcherTimer _timer;
    private DateTimeOffset? _deactivatedAt;
    private CancellationTokenSource? _backgroundLockCancellation;
    private bool _isUnlocked;
    private bool _unlockInProgress;

    public MainWindow()
    {
        InitializeComponent();
        var dataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Liubai MFA");
        ViewModel = new MainViewModel(new EncryptedAccountRepository(dataDirectory));
        _windowsHello = new WindowsHelloService(this);
        _files = new NativeFileService(this);
        _placementService = new WindowPlacementService();
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => ViewModel.UpdateCodes();
        Activated += Window_Activated;
        RootGrid.Loaded += RootGrid_Loaded;
        _placementService.Configure(AppWindow, GetWindowDpi());
        _placementService.RestoreOrApplyDefault(AppWindow, GetWindowDpi);
        AppWindow.Changed += (_, args) => _placementService.SaveNormalPlacement(AppWindow, args, GetWindowDpi());
        AppWindow.Closing += (_, _) => _placementService.SaveCurrentNormalPlacement(AppWindow);
        AppWindow.TitleBar.ExtendsContentIntoTitleBar = false;
        AppWindow.TitleBar.ButtonBackgroundColor = Colors.Transparent;
        AppWindow.TitleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
    }

    public MainViewModel ViewModel { get; }

    private async void RootGrid_Loaded(object sender, RoutedEventArgs e)
    {
        RootGrid.Loaded -= RootGrid_Loaded;
        if (!await _windowsHello.IsAvailableAsync())
        {
            LockMessage.Text = "此电脑尚未配置 Windows Hello。请先在 Windows 设置中配置系统 PIN、指纹或面容。";
            UnlockButton.Content = "重新检测 Windows Hello";
        }
    }

    private void Window_Activated(object sender, WindowActivatedEventArgs args)
    {
        if (args.WindowActivationState == WindowActivationState.Deactivated)
        {
            _deactivatedAt = DateTimeOffset.UtcNow;
            _backgroundLockCancellation?.Cancel();
            _backgroundLockCancellation = new CancellationTokenSource();
            _ = ScheduleBackgroundLockAsync(_backgroundLockCancellation.Token);
            return;
        }

        _backgroundLockCancellation?.Cancel();
        if (_isUnlocked && _deactivatedAt is not null && DateTimeOffset.UtcNow - _deactivatedAt >= BackgroundLockDelay)
        {
            Lock();
        }

        _deactivatedAt = null;
    }

    private async Task ScheduleBackgroundLockAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(BackgroundLockDelay, cancellationToken);
            if (_deactivatedAt is not null && _isUnlocked)
            {
                Lock();
            }
        }
        catch (OperationCanceledException)
        {
            // The app returned to the foreground before the lock deadline.
        }
    }

    private async void Unlock_Click(object sender, RoutedEventArgs e)
    {
        await UnlockAsync();
    }

    private async Task UnlockAsync()
    {
        if (_unlockInProgress)
        {
            return;
        }

        _unlockInProgress = true;
        UnlockButton.IsEnabled = false;
        LockMessage.Text = "正在等待系统验证…";
        try
        {
            if (!await _windowsHello.IsAvailableAsync())
            {
                LockMessage.Text = "Windows Hello 仍不可用。请先在系统设置中完成配置后重试。";
                UnlockButton.Content = "重新检测 Windows Hello";
                return;
            }

            if (!await _windowsHello.VerifyAsync("验证身份以解锁留白 MFA"))
            {
                LockMessage.Text = "身份验证未完成，应用仍保持锁定。";
                return;
            }

            if (!ViewModel.IsLoaded)
            {
                try
                {
                    await ViewModel.LoadAsync();
                }
                catch (Exception exception) when (exception is IOException or System.Security.Cryptography.CryptographicException or FormatException)
                {
                    LockMessage.Text = "无法解锁本地账户数据。为避免覆盖，应用保持锁定。";
                    return;
                }
            }

            _isUnlocked = true;
            UnlockButton.Content = "使用 Windows Hello 解锁";
            LockOverlay.Visibility = Visibility.Collapsed;
            _timer.Start();
            RefreshEmptyState();
        }
        finally
        {
            _unlockInProgress = false;
            UnlockButton.IsEnabled = true;
        }
    }

    private void Lock_Click(object sender, RoutedEventArgs e) => Lock();

    private void Lock()
    {
        _isUnlocked = false;
        _timer.Stop();
        ViewModel.Unload();
        LockMessage.Text = "使用 Windows Hello 解锁";
        LockOverlay.Visibility = Visibility.Visible;
    }

    private async void AddAccount_Click(object sender, RoutedEventArgs e)
    {
        var input = await ShowAccountDialogAsync("添加账户", null);
        if (input is null)
        {
            return;
        }

        try
        {
            await ViewModel.AddAsync(input);
            RefreshEmptyState();
            ShowStatus("账户已添加。", InfoBarSeverity.Success);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (InvalidOperationException exception)
        {
            ShowStatus(exception.Message, InfoBarSeverity.Warning);
        }
    }

    private async void ImportQr_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var file = await _files.OpenQrImageAsync();
            if (file is null)
            {
                return;
            }

            var value = await QrCodeImportService.DecodeAsync(file);
            if (value is null || !OtpAuthUriParser.TryParse(value, out var parsed) || parsed is null)
            {
                ShowStatus("图片中没有可识别的 TOTP 二维码。", InfoBarSeverity.Warning);
                return;
            }

            var input = await ShowAccountDialogAsync("确认二维码账户", new MfaAccount
            {
                Id = Guid.NewGuid().ToString("N"),
                Issuer = parsed.Issuer,
                Account = parsed.Account,
                Secret = parsed.Secret,
                CreatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            });
            if (input is null)
            {
                return;
            }

            await ViewModel.AddAsync(input);
            RefreshEmptyState();
            ShowStatus("二维码账户已导入。", InfoBarSeverity.Success);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or FormatException)
        {
            ShowStatus(exception.Message, InfoBarSeverity.Error);
        }
    }

    private async void CopyCode_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string id })
        {
            return;
        }

        var account = FindAccount(id);
        if (account is null)
        {
            return;
        }

        CopyText(account.Code);
        ShowStatus("验证码已复制。", InfoBarSeverity.Success);
        await Task.CompletedTask;
    }

    private async void EditAccount_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string id })
        {
            return;
        }

        var existing = FindAccount(id);
        if (existing is null)
        {
            return;
        }

        var updated = await ShowAccountDialogAsync("编辑账户", existing.Model, allowSecretEdit: false);
        if (updated is null)
        {
            return;
        }

        try
        {
            await ViewModel.UpdateAsync(updated);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        ShowStatus("账户已更新。", InfoBarSeverity.Success);
    }

    private async void ShowSecret_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string id })
        {
            return;
        }

        var account = FindAccount(id);
        if (account is null || !await VerifySensitiveActionAsync("验证身份以查看 MFA 密钥"))
        {
            return;
        }

        var secretText = new TextBlock
        {
            Text = FormatSecret(account.Model.Secret),
            FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"),
            FontSize = 18,
            IsTextSelectionEnabled = true,
            TextWrapping = TextWrapping.Wrap,
        };
        var dialog = CreateDialog("MFA 密钥", secretText, "关闭", "复制密钥");
        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Secondary)
        {
            CopyText(account.Model.Secret);
            ShowStatus("MFA 密钥已复制。", InfoBarSeverity.Success);
        }
    }

    private async void DeleteAccount_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string id })
        {
            return;
        }

        var account = FindAccount(id);
        if (account is null)
        {
            return;
        }

        var dialog = CreateDialog(
            "删除账户",
            new TextBlock { Text = $"确定删除“{account.Issuer} / {account.Account}”吗？此操作不会影响其他设备。", TextWrapping = TextWrapping.Wrap },
            "删除",
            "取消");
        var result = await dialog.ShowAsync();
        if (result != ContentDialogResult.Primary)
        {
            return;
        }

        try
        {
            await ViewModel.DeleteAsync(id);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        RefreshEmptyState();
        ShowStatus("账户已删除。", InfoBarSeverity.Success);
    }

    private async void ExportBackup_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.Accounts.Count == 0)
        {
            ShowStatus("暂无账户可导出。", InfoBarSeverity.Warning);
            return;
        }

        if (!await VerifySensitiveActionAsync("验证身份以导出加密备份"))
        {
            return;
        }

        var backup = BackupCrypto.CreateBackup(ViewModel.Snapshot(), out var recoveryKey);
        var dialog = new ContentDialog
        {
            XamlRoot = RootGrid.XamlRoot,
            Title = "先保存恢复密钥",
            PrimaryButtonText = "保存备份文件",
            CloseButtonText = "取消",
            IsPrimaryButtonEnabled = false,
            DefaultButton = ContentDialogButton.None,
        };
        var keyText = new TextBlock
        {
            Text = BackupCrypto.FormatRecoveryKey(recoveryKey),
            FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"),
            IsTextSelectionEnabled = true,
            TextWrapping = TextWrapping.Wrap,
        };
        var copyButton = new Button { Content = "复制恢复密钥", HorizontalAlignment = HorizontalAlignment.Stretch };
        copyButton.Click += (_, _) =>
        {
            CopyText(BackupCrypto.FormatRecoveryKey(recoveryKey));
            copyButton.Content = "恢复密钥已复制";
            dialog.IsPrimaryButtonEnabled = true;
        };
        dialog.Content = new StackPanel
        {
            Spacing = 12,
            Children =
            {
                new TextBlock { Text = "恢复密钥不会写入备份文件。丢失后无法恢复，请与备份文件分开保存。", TextWrapping = TextWrapping.Wrap },
                keyText,
                copyButton,
            },
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary && await _files.SaveBackupAsync(backup))
        {
            ShowStatus("加密备份已保存。请妥善保管恢复密钥。", InfoBarSeverity.Success);
        }
    }

    private async void ImportBackup_Click(object sender, RoutedEventArgs e)
    {
        string? backup;
        try
        {
            backup = await _files.OpenBackupAsync();
        }
        catch (FormatException exception)
        {
            ShowStatus(exception.Message, InfoBarSeverity.Warning);
            return;
        }

        if (backup is null)
        {
            return;
        }

        var keyBox = new PasswordBox { PlaceholderText = "粘贴恢复密钥", PasswordRevealMode = PasswordRevealMode.Peek };
        var dialog = CreateDialog("导入加密备份", keyBox, "导入", "取消");
        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        try
        {
            var accounts = BackupCrypto.ReadBackup(backup, keyBox.Password);
            var (imported, skipped) = await ViewModel.ImportAsync(accounts);
            RefreshEmptyState();
            ShowStatus($"已导入 {imported} 个账户，跳过 {skipped} 个重复账户。", InfoBarSeverity.Success);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception exception) when (exception is FormatException or System.Security.Cryptography.CryptographicException or System.Text.Json.JsonException)
        {
            ShowStatus("恢复密钥错误，或备份文件无效/已损坏。", InfoBarSeverity.Error);
        }
    }

    private async Task<MfaAccount?> ShowAccountDialogAsync(string title, MfaAccount? existing, bool allowSecretEdit = true)
    {
        var issuerBox = new TextBox { Header = "服务名称", Text = existing?.Issuer ?? string.Empty, PlaceholderText = "例如 GitHub" };
        var accountBox = new TextBox { Header = "账户", Text = existing?.Account ?? string.Empty, PlaceholderText = "例如 alice@example.com" };
        var secretBox = new TextBox
        {
            Header = "Base32 密钥或 otpauth:// 地址",
            Text = allowSecretEdit ? existing?.Secret ?? string.Empty : string.Empty,
            IsEnabled = allowSecretEdit,
            Visibility = allowSecretEdit ? Visibility.Visible : Visibility.Collapsed,
            PlaceholderText = "JBSW Y3DP EHPK 3PXP",
        };
        var groupBox = new TextBox { Header = "分组（可选）", Text = existing?.Group ?? string.Empty };
        var content = new StackPanel { Spacing = 10, Children = { issuerBox, accountBox, secretBox, groupBox } };
        var dialog = CreateDialog(title, content, "保存", "取消");
        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return null;
        }

        var issuer = issuerBox.Text.Trim();
        var accountName = accountBox.Text.Trim();
        var secret = allowSecretEdit ? secretBox.Text.Trim() : existing?.Secret ?? string.Empty;
        if (OtpAuthUriParser.TryParse(secret, out var parsed) && parsed is not null)
        {
            issuer = string.IsNullOrWhiteSpace(issuer) ? parsed.Issuer : issuer;
            accountName = string.IsNullOrWhiteSpace(accountName) ? parsed.Account : accountName;
            secret = parsed.Secret;
        }
        else
        {
            secret = TotpService.NormalizeSecret(secret);
        }

        if (string.IsNullOrWhiteSpace(issuer) || string.IsNullOrWhiteSpace(accountName))
        {
            ShowStatus("请填写服务名称和账户。", InfoBarSeverity.Warning);
            return null;
        }

        try
        {
            _ = TotpService.Generate(secret);
        }
        catch (FormatException exception)
        {
            ShowStatus(exception.Message, InfoBarSeverity.Warning);
            return null;
        }

        return new MfaAccount
        {
            Id = existing?.Id ?? Guid.NewGuid().ToString("N"),
            Issuer = issuer,
            Account = accountName,
            Secret = secret,
            Group = string.IsNullOrWhiteSpace(groupBox.Text) ? null : groupBox.Text.Trim(),
            Color = existing?.Color,
            CreatedAt = existing?.CreatedAt ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
        };
    }

    private async Task<bool> VerifySensitiveActionAsync(string reason)
    {
        if (await _windowsHello.VerifyAsync(reason))
        {
            return true;
        }

        ShowStatus("系统身份验证未完成，操作已取消。", InfoBarSeverity.Warning);
        return false;
    }

    private AccountItemViewModel? FindAccount(string id) => ViewModel.Accounts.FirstOrDefault(item => item.Id == id);

    private ContentDialog CreateDialog(string title, object content, string primaryText, string secondaryText)
    {
        return new ContentDialog
        {
            XamlRoot = RootGrid.XamlRoot,
            Title = title,
            Content = content,
            PrimaryButtonText = primaryText,
            SecondaryButtonText = secondaryText,
            DefaultButton = ContentDialogButton.Primary,
        };
    }

    private static void CopyText(string text)
    {
        var package = new DataPackage();
        package.SetText(text);
        Clipboard.SetContent(package);
        Clipboard.Flush();
    }

    private void RefreshEmptyState()
    {
        EmptyState.Visibility = ViewModel.Accounts.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        AccountsList.Visibility = ViewModel.Accounts.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private void ShowStatus(string message, InfoBarSeverity severity)
    {
        StatusBar.Message = message;
        StatusBar.Severity = severity;
        StatusBar.IsOpen = true;
    }

    private static string FormatSecret(string secret) => string.Join(' ', Enumerable.Range(0, (secret.Length + 3) / 4)
        .Select(index => secret.Substring(index * 4, Math.Min(4, secret.Length - index * 4))));

    private uint GetWindowDpi()
    {
        try
        {
            var dpi = NativeMethods.GetDpiForWindow(WindowNative.GetWindowHandle(this));
            return dpi == 0 ? 96 : dpi;
        }
        catch (Exception)
        {
            return 96;
        }
    }

    private static class NativeMethods
    {
        [DllImport("user32.dll")]
        internal static extern uint GetDpiForWindow(nint windowHandle);
    }
}
