using LiubaiMfa.Core;
using LiubaiMfa.Core.Models;
using LiubaiMfa.Windows.Services;
using LiubaiMfa.Windows.ViewModels;
using LiubaiMfa.Windows.Views;
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
    private readonly UnlockSessionGuard _sessionGuard = new();
    private readonly DispatcherTimer _timer;
    private DateTimeOffset? _deactivatedAt;
    private CancellationTokenSource? _backgroundLockCancellation;
    private ContentDialog? _activeDialog;
    private long _activeSession;
    private bool _isUnlocked;
    private bool _unlockInProgress;

    public MainWindow()
    {
        InitializeComponent();
        var dataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Liubai MFA");
        ViewModel = new MainViewModel(new EncryptedAccountRepository(dataDirectory));
        AuthenticatorView.AddRequested += AddAccount_Click;
        AuthenticatorView.ImportQrRequested += ImportQr_Click;
        AuthenticatorView.LockRequested += Lock_Click;
        AuthenticatorView.ImportBackupRequested += ImportBackup_Click;
        AuthenticatorView.ExportBackupRequested += ExportBackup_Click;
        AuthenticatorView.CopyRequested += CopyCode_Click;
        AuthenticatorView.EditRequested += EditAccount_Click;
        AuthenticatorView.ShowSecretRequested += ShowSecret_Click;
        AuthenticatorView.DeleteRequested += DeleteAccount_Click;
        LockView.UnlockRequested += Unlock_Click;
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
        AppWindow.TitleBar.ExtendsContentIntoTitleBar = false;
        AppWindow.TitleBar.ButtonBackgroundColor = Colors.Transparent;
        AppWindow.TitleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
    }

    public MainViewModel ViewModel { get; }

    private async void RootGrid_Loaded(object sender, RoutedEventArgs e)
    {
        RootGrid.Loaded -= RootGrid_Loaded;
        try
        {
            if (!await _windowsHello.IsAvailableAsync())
            {
                SetHelloUnavailableMessage();
            }
        }
        catch (Exception)
        {
            SetHelloTemporarilyUnavailableMessage();
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

    private async void Unlock_Click(object? sender, EventArgs e)
    {
        try
        {
            await UnlockAsync();
        }
        catch (Exception)
        {
            Lock();
            LockView.Message = "无法完成解锁，请稍后重试。";
        }
    }

    private async Task UnlockAsync()
    {
        if (_unlockInProgress)
        {
            return;
        }

        _unlockInProgress = true;
        LockView.IsUnlockEnabled = false;
        LockView.Message = "正在等待系统验证…";
        try
        {
            bool isHelloAvailable;
            try
            {
                isHelloAvailable = await _windowsHello.IsAvailableAsync();
            }
            catch (Exception)
            {
                SetHelloTemporarilyUnavailableMessage();
                return;
            }

            if (!isHelloAvailable)
            {
                SetHelloUnavailableMessage();
                return;
            }

            bool isVerified;
            try
            {
                isVerified = await _windowsHello.VerifyAsync("验证身份以解锁留白 MFA");
            }
            catch (Exception)
            {
                SetHelloTemporarilyUnavailableMessage();
                return;
            }

            if (!isVerified)
            {
                LockView.Message = "身份验证未完成，应用仍保持锁定。";
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
                    LockView.Message = "无法解锁本地账户数据。为避免覆盖，应用保持锁定。";
                    return;
                }
            }

            Volatile.Write(ref _activeSession, _sessionGuard.BeginUnlockedSession());
            _isUnlocked = true;
            AuthenticatorView.Attach(ViewModel);
            LockView.ButtonContent = "使用 Windows Hello 解锁";
            LockView.Visibility = Visibility.Collapsed;
            AuthenticatorView.Visibility = Visibility.Visible;
            _timer.Start();
        }
        finally
        {
            _unlockInProgress = false;
            LockView.IsUnlockEnabled = true;
        }
    }

    private void Lock_Click(object? sender, EventArgs e) => Lock();

    private void Lock()
    {
        _sessionGuard.Invalidate();
        Volatile.Write(ref _activeSession, 0);
        _isUnlocked = false;
        HideActiveDialog();
        _timer.Stop();
        AuthenticatorView.Detach();
        ViewModel.Unload();
        LockView.Message = "使用 Windows Hello 验证身份后即可查看验证码。";
        LockView.ButtonContent = "使用 Windows Hello 解锁";
        AuthenticatorView.Visibility = Visibility.Collapsed;
        LockView.Visibility = Visibility.Visible;
    }

    private async void AddAccount_Click(object? sender, EventArgs e)
    {
        if (!TryCaptureUnlockedSession(out var token))
        {
            return;
        }

        var input = await ShowAccountDialogAsync(token, "添加账户", null);
        if (input is null || !IsSessionCurrent(token))
        {
            return;
        }

        try
        {
            await ViewModel.AddAsync(input);
            if (!IsSessionCurrent(token))
            {
                return;
            }

            AuthenticatorView.RefreshState();
            ShowStatus(token, "账户已添加。", InfoBarSeverity.Success);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (InvalidOperationException exception)
        {
            ShowStatus(token, exception.Message, InfoBarSeverity.Warning);
        }
    }

    private async void ImportQr_Click(object? sender, EventArgs e)
    {
        if (!TryCaptureUnlockedSession(out var token))
        {
            return;
        }

        try
        {
            var file = await _files.OpenQrImageAsync();
            if (file is null || !IsSessionCurrent(token))
            {
                return;
            }

            var value = await QrCodeImportService.DecodeAsync(file);
            if (!IsSessionCurrent(token))
            {
                return;
            }

            if (value is null || !OtpAuthUriParser.TryParse(value, out var parsed) || parsed is null)
            {
                ShowStatus(token, "图片中没有可识别的 TOTP 二维码。", InfoBarSeverity.Warning);
                return;
            }

            var input = await ShowAccountDialogAsync(token, "确认二维码账户", new MfaAccount
            {
                Id = Guid.NewGuid().ToString("N"),
                Issuer = parsed.Issuer,
                Account = parsed.Account,
                Secret = parsed.Secret,
                CreatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            });
            if (input is null || !IsSessionCurrent(token))
            {
                return;
            }

            await ViewModel.AddAsync(input);
            if (!IsSessionCurrent(token))
            {
                return;
            }

            AuthenticatorView.RefreshState();
            ShowStatus(token, "二维码账户已导入。", InfoBarSeverity.Success);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or FormatException)
        {
            ShowStatus(token, exception.Message, InfoBarSeverity.Error);
        }
    }

    private void CopyCode_Click(object? sender, AccountRequestedEventArgs e)
    {
        if (!TryCaptureUnlockedSession(out var token))
        {
            return;
        }

        var account = AuthenticatorView.FindAccount(e.AccountId);
        if (account is null)
        {
            return;
        }

        CopyText(account.Code);
        ShowStatus(token, "验证码已复制。", InfoBarSeverity.Success);
    }

    private async void EditAccount_Click(object? sender, AccountRequestedEventArgs e)
    {
        if (!TryCaptureUnlockedSession(out var token))
        {
            return;
        }

        var existing = AuthenticatorView.FindAccount(e.AccountId);
        if (existing is null)
        {
            return;
        }

        var updated = await ShowAccountDialogAsync(token, "编辑账户", existing.Model, allowSecretEdit: false);
        if (updated is null || !IsSessionCurrent(token))
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

        if (IsSessionCurrent(token))
        {
            ShowStatus(token, "账户已更新。", InfoBarSeverity.Success);
        }
    }

    private async void ShowSecret_Click(object? sender, AccountRequestedEventArgs e)
    {
        if (!TryCaptureUnlockedSession(out var token) || AuthenticatorView.FindAccount(e.AccountId) is null)
        {
            return;
        }

        if (!await VerifySensitiveActionAsync(token, "验证身份以查看 MFA 密钥") || !IsSessionCurrent(token))
        {
            return;
        }

        var account = AuthenticatorView.FindAccount(e.AccountId);
        if (account is null)
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
        var result = await ShowDialogAsync(dialog, token);
        if (result != ContentDialogResult.Secondary || !IsSessionCurrent(token))
        {
            return;
        }

        var currentAccount = AuthenticatorView.FindAccount(e.AccountId);
        if (currentAccount is null)
        {
            return;
        }

        CopyText(currentAccount.Model.Secret);
        ShowStatus(token, "MFA 密钥已复制。", InfoBarSeverity.Success);
    }

    private async void DeleteAccount_Click(object? sender, AccountRequestedEventArgs e)
    {
        if (!TryCaptureUnlockedSession(out var token))
        {
            return;
        }

        var account = AuthenticatorView.FindAccount(e.AccountId);
        if (account is null)
        {
            return;
        }

        var dialog = CreateDialog(
            "删除账户",
            new TextBlock { Text = $"确定删除“{account.Issuer} / {account.Account}”吗？此操作不会影响其他设备。", TextWrapping = TextWrapping.Wrap },
            "删除",
            "取消");
        var result = await ShowDialogAsync(dialog, token);
        if (result != ContentDialogResult.Primary || !IsSessionCurrent(token))
        {
            return;
        }

        try
        {
            await ViewModel.DeleteAsync(e.AccountId);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (!IsSessionCurrent(token))
        {
            return;
        }

        AuthenticatorView.RefreshState();
        ShowStatus(token, "账户已删除。", InfoBarSeverity.Success);
    }

    private async void ExportBackup_Click(object? sender, EventArgs e)
    {
        if (!TryCaptureUnlockedSession(out var token))
        {
            return;
        }

        if (!ViewModel.HasAccounts)
        {
            ShowStatus(token, "暂无账户可导出。", InfoBarSeverity.Warning);
            return;
        }

        if (!await VerifySensitiveActionAsync(token, "验证身份以导出加密备份") || !IsSessionCurrent(token))
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
            if (!IsSessionCurrent(token))
            {
                return;
            }

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

        if (await ShowDialogAsync(dialog, token) != ContentDialogResult.Primary || !IsSessionCurrent(token))
        {
            return;
        }

        var file = await _files.PickBackupSaveFileAsync();
        if (file is null || !IsSessionCurrent(token))
        {
            return;
        }

        await _files.WriteBackupAsync(file, backup);
        if (IsSessionCurrent(token))
        {
            ShowStatus(token, "加密备份已保存。请妥善保管恢复密钥。", InfoBarSeverity.Success);
        }
    }

    private async void ImportBackup_Click(object? sender, EventArgs e)
    {
        if (!TryCaptureUnlockedSession(out var token))
        {
            return;
        }

        string? backup;
        try
        {
            backup = await _files.OpenBackupAsync();
        }
        catch (FormatException exception)
        {
            ShowStatus(token, exception.Message, InfoBarSeverity.Warning);
            return;
        }

        if (backup is null || !IsSessionCurrent(token))
        {
            return;
        }

        var keyBox = new PasswordBox { PlaceholderText = "粘贴恢复密钥", PasswordRevealMode = PasswordRevealMode.Peek };
        var dialog = CreateDialog("导入加密备份", keyBox, "导入", "取消");
        if (await ShowDialogAsync(dialog, token) != ContentDialogResult.Primary || !IsSessionCurrent(token))
        {
            return;
        }

        try
        {
            var accounts = BackupCrypto.ReadBackup(backup, keyBox.Password);
            if (!IsSessionCurrent(token))
            {
                return;
            }

            var (imported, skipped) = await ViewModel.ImportAsync(accounts);
            if (!IsSessionCurrent(token))
            {
                return;
            }

            AuthenticatorView.RefreshState();
            ShowStatus(token, $"已导入 {imported} 个账户，跳过 {skipped} 个重复账户。", InfoBarSeverity.Success);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception exception) when (exception is FormatException or System.Security.Cryptography.CryptographicException or System.Text.Json.JsonException)
        {
            ShowStatus(token, "恢复密钥错误，或备份文件无效/已损坏。", InfoBarSeverity.Error);
        }
    }

    private async Task<MfaAccount?> ShowAccountDialogAsync(
        long token,
        string title,
        MfaAccount? existing,
        bool allowSecretEdit = true)
    {
        if (!IsSessionCurrent(token))
        {
            return null;
        }

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
        if (await ShowDialogAsync(dialog, token) != ContentDialogResult.Primary || !IsSessionCurrent(token))
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
            ShowStatus(token, "请填写服务名称和账户。", InfoBarSeverity.Warning);
            return null;
        }

        try
        {
            _ = TotpService.Generate(secret);
        }
        catch (FormatException exception)
        {
            ShowStatus(token, exception.Message, InfoBarSeverity.Warning);
            return null;
        }

        return IsSessionCurrent(token)
            ? new MfaAccount
            {
                Id = existing?.Id ?? Guid.NewGuid().ToString("N"),
                Issuer = issuer,
                Account = accountName,
                Secret = secret,
                Group = string.IsNullOrWhiteSpace(groupBox.Text) ? null : groupBox.Text.Trim(),
                Color = existing?.Color,
                CreatedAt = existing?.CreatedAt ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            }
            : null;
    }

    private async Task<bool> VerifySensitiveActionAsync(long token, string reason)
    {
        if (!IsSessionCurrent(token))
        {
            return false;
        }

        try
        {
            var isVerified = await _windowsHello.VerifyAsync(reason);
            if (!IsSessionCurrent(token))
            {
                return false;
            }

            if (isVerified)
            {
                return true;
            }

            ShowStatus(token, "系统身份验证未完成，操作已取消。", InfoBarSeverity.Warning);
        }
        catch (Exception)
        {
            ShowStatus(token, "Windows Hello 暂时不可用，请稍后重试。", InfoBarSeverity.Warning);
        }

        return false;
    }

    private async Task<ContentDialogResult> ShowDialogAsync(ContentDialog dialog, long token)
    {
        if (!IsSessionCurrent(token) || Interlocked.CompareExchange(ref _activeDialog, dialog, null) is not null)
        {
            return ContentDialogResult.None;
        }

        try
        {
            if (!IsSessionCurrent(token))
            {
                return ContentDialogResult.None;
            }

            var result = await dialog.ShowAsync();
            return IsSessionCurrent(token) ? result : ContentDialogResult.None;
        }
        catch (Exception)
        {
            ShowStatus(token, "无法打开操作窗口，请稍后重试。", InfoBarSeverity.Error);
            return ContentDialogResult.None;
        }
        finally
        {
            Interlocked.CompareExchange(ref _activeDialog, null, dialog);
        }
    }

    private void HideActiveDialog()
    {
        var dialog = Interlocked.Exchange(ref _activeDialog, null);
        if (dialog is null)
        {
            return;
        }

        try
        {
            dialog.Hide();
        }
        catch (Exception)
        {
            // Locking must continue even if the dialog is already closing.
        }
    }

    private bool TryCaptureUnlockedSession(out long token)
    {
        token = Volatile.Read(ref _activeSession);
        return _isUnlocked && _sessionGuard.IsCurrent(token);
    }

    private bool IsSessionCurrent(long token) =>
        _isUnlocked && token == Volatile.Read(ref _activeSession) && _sessionGuard.IsCurrent(token);

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

    private void ShowStatus(long token, string message, InfoBarSeverity severity)
    {
        if (IsSessionCurrent(token))
        {
            AuthenticatorView.ShowStatus(message, severity);
        }
    }

    private void SetHelloUnavailableMessage()
    {
        LockView.Message = "此电脑尚未配置 Windows Hello。请先在 Windows 设置中配置系统 PIN、指纹或面容。";
        LockView.ButtonContent = "重新检测 Windows Hello";
    }

    private void SetHelloTemporarilyUnavailableMessage()
    {
        LockView.Message = "Windows Hello 暂时不可用，请稍后重试。";
        LockView.ButtonContent = "重新检测 Windows Hello";
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
