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
using WinRT.Interop;

namespace LiubaiMfa.Windows;

public sealed partial class MainWindow : Window
{
    private static readonly TimeSpan BackgroundLockDelay = TimeSpan.FromMinutes(1);
    private readonly WindowsHelloService _windowsHello;
    private readonly StartupProtectionSettings _startupProtectionSettings;
    private readonly NativeFileService _files;
    private readonly ClipboardService _clipboard;
    private readonly AccountDialogService _accountDialogs;
    private readonly BackupDialogService _backupDialogs;
    private readonly WindowPlacementService _placementService;
    private readonly UnlockSessionGuard _sessionGuard = new();
    private readonly SessionCancellationGuard _sessionCancellation = new();
    private readonly DispatcherTimer _timer;
    private DateTimeOffset? _deactivatedAt;
    private CancellationTokenSource? _backgroundLockCancellation;
    private ContentDialog? _activeDialog;
    private long _activeSession;
    private bool _isUnlocked;
    private bool _unlockInProgress;
    private bool _startupProtectionEnabled;
    private readonly DeferredNoticeGate _cancelledBackupNotice = new();

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
        _startupProtectionSettings = new StartupProtectionSettings();
        _files = new NativeFileService(this);
        _clipboard = new ClipboardService();
        _accountDialogs = new AccountDialogService(_windowsHello, ShowDialogAsync, IsSessionCurrent, ShowStatus);
        _backupDialogs = new BackupDialogService(ShowDialogAsync, IsSessionCurrent);
        _placementService = new WindowPlacementService();
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => ViewModel.UpdateCodes();
        Activated += Window_Activated;
        Closed += (_, _) => _sessionCancellation.Dispose();
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
        _startupProtectionEnabled = _startupProtectionSettings.IsEnabled;
        AuthenticatorView.ApplyStartupProtection(_startupProtectionEnabled);
        if (!StartupProtectionPolicy.ShouldStartLocked(_startupProtectionEnabled))
        {
            try
            {
                await OpenWithoutVerificationAsync();
            }
            catch (Exception)
            {
                ForceLock("无法完成打开操作，请稍后重试。");
            }

            return;
        }

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
        if (!StartupProtectionPolicy.ShouldScheduleAutoLock(_startupProtectionEnabled, _isUnlocked))
        {
            _backgroundLockCancellation?.Cancel();
            _backgroundLockCancellation = null;
            _deactivatedAt = null;
            return;
        }

        if (args.WindowActivationState == WindowActivationState.Deactivated)
        {
            _deactivatedAt = DateTimeOffset.UtcNow;
            _backgroundLockCancellation?.Cancel();
            _backgroundLockCancellation = new CancellationTokenSource();
            _ = ScheduleBackgroundLockAsync(_backgroundLockCancellation.Token);
            return;
        }

        _backgroundLockCancellation?.Cancel();
        if (_deactivatedAt is { } deactivatedAt && StartupProtectionPolicy.ShouldLockAfterDelay(
                _startupProtectionEnabled,
                _isUnlocked,
                DateTimeOffset.UtcNow - deactivatedAt,
                BackgroundLockDelay))
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
            if (_deactivatedAt is { } deactivatedAt && StartupProtectionPolicy.ShouldLockAfterDelay(
                    _startupProtectionEnabled,
                    _isUnlocked,
                    DateTimeOffset.UtcNow - deactivatedAt,
                    BackgroundLockDelay))
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
            if (_startupProtectionEnabled)
            {
                await UnlockAsync();
            }
            else
            {
                await OpenWithoutVerificationAsync();
            }
        }
        catch (Exception)
        {
            ForceLock("无法完成打开操作，请稍后重试。");
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

            await EnterMainViewAsync("无法解锁本地账户数据。为避免覆盖，应用保持锁定。");
        }
        finally
        {
            _unlockInProgress = false;
            LockView.IsUnlockEnabled = true;
        }
    }

    private async Task OpenWithoutVerificationAsync()
    {
        if (_unlockInProgress)
        {
            return;
        }

        _unlockInProgress = true;
        LockView.IsUnlockEnabled = false;
        LockView.Message = "正在打开本机验证码…";
        try
        {
            await EnterMainViewAsync("无法打开本地账户数据。为避免覆盖，应用不会写入新数据。");
        }
        finally
        {
            _unlockInProgress = false;
            LockView.IsUnlockEnabled = true;
        }
    }

    private async Task<bool> EnterMainViewAsync(string loadFailureMessage)
    {
        if (!ViewModel.IsLoaded)
        {
            try
            {
                await ViewModel.LoadAsync();
            }
            catch (Exception exception) when (exception is IOException or System.Security.Cryptography.CryptographicException or FormatException)
            {
                LockView.Message = loadFailureMessage;
                LockView.ButtonContent = _startupProtectionEnabled ? "使用 Windows Hello 解锁" : "重试打开";
                AuthenticatorView.Visibility = Visibility.Collapsed;
                LockView.Visibility = Visibility.Visible;
                return false;
            }
        }

        var sessionToken = _sessionGuard.BeginUnlockedSession();
        _sessionCancellation.Begin(sessionToken);
        Volatile.Write(ref _activeSession, sessionToken);
        _isUnlocked = true;
        AuthenticatorView.ApplyStartupProtection(_startupProtectionEnabled);
        AuthenticatorView.Attach(ViewModel);
        LockView.ButtonContent = _startupProtectionEnabled ? "使用 Windows Hello 解锁" : "重试打开";
        LockView.Visibility = Visibility.Collapsed;
        AuthenticatorView.Visibility = Visibility.Visible;
        _timer.Start();
        TryDeliverCancelledBackupNotice(sessionToken);
        return true;
    }

    private void Lock_Click(object? sender, EventArgs e)
    {
        if (_startupProtectionEnabled)
        {
            ForceLock("使用 Windows Hello 验证身份后即可查看验证码。");
        }
    }

    private void Lock() => ForceLock("使用 Windows Hello 验证身份后即可查看验证码。");

    private void ForceLock(string message)
    {
        _sessionGuard.Invalidate();
        _sessionCancellation.Invalidate();
        Volatile.Write(ref _activeSession, 0);
        _isUnlocked = false;
        HideActiveDialog();
        _timer.Stop();
        AuthenticatorView.Detach();
        ViewModel.Unload();
        LockView.Message = message;
        LockView.ButtonContent = _startupProtectionEnabled ? "使用 Windows Hello 解锁" : "重试打开";
        AuthenticatorView.Visibility = Visibility.Collapsed;
        LockView.Visibility = Visibility.Visible;
    }

    private async void AddAccount_Click(object? sender, EventArgs e)
    {
        if (!TryCaptureUnlockedSession(out var token))
        {
            return;
        }

        var draft = await _accountDialogs.ShowAddAsync(RootGrid.XamlRoot, token);
        if (draft is null || !IsSessionCurrent(token))
        {
            return;
        }

        try
        {
            await ViewModel.AddAsync(CreateAccount(draft));
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
        catch (InvalidOperationException)
        {
            ShowStatus(token, "账户未保存。请确认密钥未重复并重试。", InfoBarSeverity.Warning);
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

            var draft = await _accountDialogs.ShowImportConfirmationAsync(
                RootGrid.XamlRoot,
                token,
                new AccountDraft(parsed.Issuer, parsed.Account, parsed.Secret, null));
            if (draft is null || !IsSessionCurrent(token))
            {
                return;
            }

            await ViewModel.AddAsync(CreateAccount(draft));
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
            ShowStatus(token, "二维码账户未导入。请确认图片和账户信息后重试。", InfoBarSeverity.Error);
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

        var copyFeedback = ClipboardResultPolicy.CreateStatus(_clipboard.TryCopyText(account.Code), "验证码已复制。");
        ShowStatus(token, copyFeedback.Message, copyFeedback.Severity);
    }

    private async void EditAccount_Click(object? sender, AccountRequestedEventArgs e)
    {
        if (!TryCaptureUnlockedSession(out var token))
        {
            return;
        }

        var existing = FindCurrentAccount(e.AccountId);
        if (existing is null)
        {
            return;
        }

        var edit = await _accountDialogs.ShowEditAsync(RootGrid.XamlRoot, token, ToAccountEdit(existing));
        if (edit is null || !IsSessionCurrent(token))
        {
            return;
        }

        var current = FindCurrentAccount(edit.Id);
        if (current is null || !IsSessionCurrent(token))
        {
            return;
        }

        try
        {
            await ViewModel.UpdateAsync(current with { Issuer = edit.Issuer, Account = edit.Account, Group = edit.Group });
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
        if (!TryCaptureUnlockedSession(out var token))
        {
            return;
        }

        await _accountDialogs.ShowSecretAsync(
            RootGrid.XamlRoot,
            token,
            e.AccountId,
            () => FindCurrentAccount(e.AccountId),
            _clipboard.TryCopyText);
    }

    private async void DeleteAccount_Click(object? sender, AccountRequestedEventArgs e)
    {
        if (!TryCaptureUnlockedSession(out var token))
        {
            return;
        }

        var account = FindCurrentAccount(e.AccountId);
        if (account is null)
        {
            return;
        }

        if (!await _accountDialogs.ConfirmDeleteAsync(RootGrid.XamlRoot, token, ToAccountEdit(account)) || !IsSessionCurrent(token))
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

        var backup = string.Empty;
        var recoveryKey = string.Empty;
        var formattedRecoveryKey = string.Empty;
        try
        {
            try
            {
                backup = BackupCrypto.CreateBackup(ViewModel.Snapshot(), out recoveryKey);
                formattedRecoveryKey = BackupCrypto.FormatRecoveryKey(recoveryKey);
            }
            catch (Exception exception) when (exception is System.Security.Cryptography.CryptographicException or System.Text.Json.JsonException or InvalidOperationException)
            {
                ShowStatus(token, "备份准备失败。请稍后重试；如问题持续，请先确认本地账户数据可正常读取。", InfoBarSeverity.Error);
                return;
            }

            if (!IsSessionCurrent(token) || !await _backupDialogs.ConfirmExportAsync(
                    RootGrid.XamlRoot,
                    token,
                    formattedRecoveryKey,
                    _clipboard.TryCopyText) || !IsSessionCurrent(token))
            {
                return;
            }

            global::Windows.Storage.StorageFile? file;
            try
            {
                file = await _files.PickBackupSaveFileAsync();
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
            {
                if (!IsSessionCurrent(token))
                {
                    QueueCancelledBackupNotice();
                    return;
                }

                ShowStatus(token, "无法选择备份位置。请检查目标位置权限后重试。", InfoBarSeverity.Error);
                return;
            }

            if (file is null)
            {
                return;
            }

            if (!IsSessionCurrent(token))
            {
                QueueCancelledBackupNotice();
                return;
            }

            if (!_sessionCancellation.TryAcquire(token, out var writeLease))
            {
                QueueCancelledBackupNotice();
                return;
            }

            using (writeLease)
            {
                if (!IsSessionCurrent(token))
                {
                    QueueCancelledBackupNotice();
                    return;
                }

                try
                {
                    var writeResult = await _files.WriteBackupAsync(file, backup, writeLease.Token, () => IsSessionCurrent(token));
                    if (writeResult == BackupWriteResult.NotReplaced)
                    {
                        if (!IsSessionCurrent(token))
                        {
                            QueueCancelledBackupNotice();
                        }

                        return;
                    }

                    if (writeResult == BackupWriteResult.ReplacedAfterSessionInvalid || !IsSessionCurrent(token))
                    {
                        QueueCancelledBackupNotice();
                        return;
                    }
                }
                catch (OperationCanceledException)
                {
                    if (!IsSessionCurrent(token))
                    {
                        QueueCancelledBackupNotice();
                    }

                    return;
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
                {
                    if (!IsSessionCurrent(token))
                    {
                        QueueCancelledBackupNotice();
                        return;
                    }

                    ShowStatus(token, "备份文件未保存。请检查目标位置权限或磁盘空间后重试。", InfoBarSeverity.Error);
                    return;
                }
            }

            if (IsSessionCurrent(token))
            {
                ShowStatus(token, "加密备份已保存。请妥善保管恢复密钥。", InfoBarSeverity.Success);
            }
        }
        finally
        {
            recoveryKey = string.Empty;
            formattedRecoveryKey = string.Empty;
            backup = string.Empty;
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
        catch (FormatException)
        {
            ShowStatus(token, "备份文件无法读取。请确认选择了正确的备份文件后重试。", InfoBarSeverity.Warning);
            return;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
        {
            ShowStatus(token, "备份文件无法读取。请检查文件是否仍可访问后重试。", InfoBarSeverity.Error);
            return;
        }

        if (backup is null || !IsSessionCurrent(token))
        {
            return;
        }

        var recoveryKey = await _backupDialogs.RequestRecoveryKeyAsync(RootGrid.XamlRoot, token);
        if (recoveryKey is null || !IsSessionCurrent(token))
        {
            return;
        }

        try
        {
            var accounts = BackupCrypto.ReadBackup(backup, recoveryKey);
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
        finally
        {
            recoveryKey = string.Empty;
        }
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

    private MfaAccount? FindCurrentAccount(string id) =>
        ViewModel.Snapshot().FirstOrDefault(account => account.Id == id);

    private static AccountEdit ToAccountEdit(MfaAccount account) => new(
        account.Id,
        account.Issuer,
        account.Account,
        account.Group,
        account.Color,
        account.CreatedAt);

    private static MfaAccount CreateAccount(AccountDraft draft) => new()
    {
        Id = Guid.NewGuid().ToString("N"),
        Issuer = draft.Issuer,
        Account = draft.Account,
        Secret = draft.Secret,
        Group = draft.Group,
        CreatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
    };

    private void QueueCancelledBackupNotice()
    {
        _cancelledBackupNotice.MarkPending();
        var currentToken = Volatile.Read(ref _activeSession);
        if (!IsSessionCurrent(currentToken))
        {
            return;
        }

        _ = DispatcherQueue.TryEnqueue(() => TryDeliverCancelledBackupNotice(currentToken));
    }

    private void TryDeliverCancelledBackupNotice(long token)
    {
        if (!IsSessionCurrent(token) || !_cancelledBackupNotice.TryConsume())
        {
            return;
        }

        ShowStatus(token, "上次备份在锁定时中断或完成，请检查目标位置的备份文件。", InfoBarSeverity.Warning);
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
