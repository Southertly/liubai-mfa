using LiubaiMfa.Core;
using LiubaiMfa.Core.Models;
using LiubaiMfa.Windows.Services;
using LiubaiMfa.Windows.ViewModels;
using LiubaiMfa.Windows.Views;
using Microsoft.UI;
using Microsoft.UI.Composition;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using System.Numerics;
using System.Runtime.InteropServices;
using WinRT.Interop;
using Windows.Storage;
using Windows.UI.ViewManagement;

namespace LiubaiMfa.Windows;

public sealed partial class MainWindow : Window
{
    private enum QrImportSource
    {
        Screenshot,
        Image,
    }

    private static readonly TimeSpan BackgroundLockDelay = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan PageTransitionDuration = TimeSpan.FromMilliseconds(180);
    private const float PageTransitionOffset = 20f;
    private readonly WindowsHelloService _windowsHello;
    private readonly StartupProtectionSettings _startupProtectionSettings;
    private readonly NativeFileService _files;
    private readonly ClipboardService _clipboard;
    private readonly AccountDialogService _accountDialogs;
    private readonly BackupDialogService _backupDialogs;
    private readonly ProtectionSettingsDialogService _protectionSettingsDialogs;
    private readonly WindowPlacementService _placementService;
    private readonly UnlockSessionGuard _sessionGuard = new();
    private readonly SessionCancellationGuard _sessionCancellation = new();
    private readonly AutoLockActivationState _autoLockActivationState = new();
    private readonly SnippingCaptureState _snippingCaptureState = new();
    private readonly SnippingToolCaptureService _snippingToolCaptureService = new();
    private readonly QrAccountImportCoordinator _qrImportCoordinator;
    private readonly UISettings _uiSettings = new();
    private readonly DispatcherTimer _timer;
    private CancellationTokenSource? _backgroundLockCancellation;
    private ContentDialog? _activeDialog;
    private long _activeSession;
    private bool _isUnlocked;
    private bool _unlockInProgress;
    private bool _settingsInProgress;
    private bool _startupProtectionEnabled;
    private long _navigationEpoch;
    private readonly DeferredNoticeGate _cancelledBackupNotice = new();

    public MainWindow()
    {
        InitializeComponent();
        SystemBackdrop = new MicaBackdrop { Kind = MicaKind.BaseAlt };
        var dataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Liubai MFA");
        ViewModel = new MainViewModel(new EncryptedAccountRepository(dataDirectory));
        AuthenticatorView.AddRequested += ShowAddAccountView;
        AuthenticatorView.LockRequested += Lock_Click;
        AuthenticatorView.ImportBackupRequested += ImportBackup_Click;
        AuthenticatorView.ExportBackupRequested += ExportBackup_Click;
        AuthenticatorView.CopyRequested += CopyCode_Click;
        AuthenticatorView.EditRequested += EditAccount_Click;
        AuthenticatorView.ShowSecretRequested += ShowSecret_Click;
        AuthenticatorView.DeleteRequested += DeleteAccount_Click;
        AuthenticatorView.SettingsRequested += Settings_Click;
        AddAccountView.BackRequested += ShowAuthenticatorView;
        AddAccountView.ScreenshotScanRequested += ScreenshotScan_Click;
        AddAccountView.ImportImageRequested += ImportImage_Click;
        AddAccountView.ManualEntryRequested += ManualEntry_Click;
        LockView.UnlockRequested += Unlock_Click;
        _windowsHello = new WindowsHelloService(this);
        _startupProtectionSettings = new StartupProtectionSettings();
        _files = new NativeFileService(this);
        _clipboard = new ClipboardService();
        _accountDialogs = new AccountDialogService(_windowsHello, ShowDialogAsync, IsSessionCurrent, ShowStatus);
        _qrImportCoordinator = new QrAccountImportCoordinator(
            QrCodeImportService.DecodeAsync,
            ConfirmQrAccountAsync,
            SaveQrAccountAsync,
            IsSessionCurrent);
        _backupDialogs = new BackupDialogService(ShowDialogAsync, IsSessionCurrent);
        _protectionSettingsDialogs = new ProtectionSettingsDialogService(ShowDialogAsync, IsSessionCurrent);
        _placementService = new WindowPlacementService();
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => ViewModel.UpdateCodes();
        Activated += Window_Activated;
        Closed += (_, _) =>
        {
            _snippingCaptureState.Abandon();
            _sessionCancellation.Dispose();
        };
        RootGrid.Loaded += RootGrid_Loaded;
        _placementService.Configure(AppWindow, GetWindowDpi());
        _placementService.RestoreOrApplyDefault(AppWindow, GetWindowDpi);
        AppWindow.Changed += (_, args) => _placementService.SaveNormalPlacement(AppWindow, args, GetWindowDpi());
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBarDragRegion);
        AppWindow.TitleBar.ButtonBackgroundColor = Colors.Transparent;
        AppWindow.TitleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
    }

    public MainViewModel ViewModel { get; }

    internal async Task HandleProtocolActivationAsync(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        if (!SnippingToolProtocol.TryParseResponse(uri, out var response))
        {
            return;
        }

        var sessionCurrent = TryCaptureUnlockedSession(out var sessionToken);
        var decision = _snippingCaptureState.Receive(
            response.CorrelationId,
            response.StatusCode,
            response.FileAccessToken,
            sessionCurrent);
        await ProcessSnippingCaptureDecisionAsync(decision, sessionCurrent ? sessionToken : 0);
    }

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
        var now = DateTimeOffset.UtcNow;
        CancelBackgroundLock();
        if (!_startupProtectionEnabled)
        {
            _autoLockActivationState.Clear();
            return;
        }

        if (args.WindowActivationState == WindowActivationState.Deactivated)
        {
            _autoLockActivationState.RecordDeactivated(true, now);
            ApplyBackgroundLockDecision(_autoLockActivationState.Evaluate(
                true,
                _isUnlocked,
                now,
                BackgroundLockDelay));
            return;
        }

        var activationDecision = _autoLockActivationState.Activate(
            _startupProtectionEnabled,
            _isUnlocked,
            now,
            BackgroundLockDelay);
        if (activationDecision.Kind == AutoLockDecisionKind.LockNow)
        {
            Lock();
        }
    }

    private void ApplyBackgroundLockDecision(AutoLockDecision decision)
    {
        if (decision.Kind == AutoLockDecisionKind.LockNow)
        {
            Lock();
            return;
        }

        if (decision.Kind != AutoLockDecisionKind.Schedule)
        {
            return;
        }

        var sessionToken = Volatile.Read(ref _activeSession);
        if (!IsSessionCurrent(sessionToken))
        {
            return;
        }

        var cancellation = new CancellationTokenSource();
        _backgroundLockCancellation = cancellation;
        _ = ScheduleBackgroundLockAsync(decision.RemainingDelay, sessionToken, cancellation);
    }

    private async Task ScheduleBackgroundLockAsync(
        TimeSpan remainingDelay,
        long sessionToken,
        CancellationTokenSource cancellation)
    {
        try
        {
            while (true)
            {
                await Task.Delay(remainingDelay, cancellation.Token);
                cancellation.Token.ThrowIfCancellationRequested();
                if (!ReferenceEquals(_backgroundLockCancellation, cancellation) ||
                    !IsSessionCurrent(sessionToken))
                {
                    return;
                }

                var decision = _autoLockActivationState.Evaluate(
                    _startupProtectionEnabled,
                    _isUnlocked,
                    DateTimeOffset.UtcNow,
                    BackgroundLockDelay);
                if (decision.Kind == AutoLockDecisionKind.LockNow)
                {
                    _backgroundLockCancellation = null;
                    Lock();
                    return;
                }

                if (decision.Kind != AutoLockDecisionKind.Schedule)
                {
                    return;
                }

                remainingDelay = decision.RemainingDelay;
            }
        }
        catch (OperationCanceledException)
        {
            // The app returned to the foreground before the lock deadline.
        }
        finally
        {
            if (ReferenceEquals(_backgroundLockCancellation, cancellation))
            {
                _backgroundLockCancellation = null;
            }

            cancellation.Dispose();
        }
    }

    private void CancelBackgroundLock()
    {
        var cancellation = _backgroundLockCancellation;
        _backgroundLockCancellation = null;
        cancellation?.Cancel();
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
                _snippingCaptureState.Abandon();
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

        var backgroundLockDecision = _autoLockActivationState.Evaluate(
            _startupProtectionEnabled,
            true,
            DateTimeOffset.UtcNow,
            BackgroundLockDelay);
        if (backgroundLockDecision.Kind == AutoLockDecisionKind.LockNow)
        {
            ForceLock("使用 Windows Hello 验证身份后即可查看验证码。");
            return false;
        }

        var sessionToken = _sessionGuard.BeginUnlockedSession();
        _sessionCancellation.Begin(sessionToken);
        Volatile.Write(ref _activeSession, sessionToken);
        _isUnlocked = true;
        AuthenticatorView.ApplyStartupProtection(_startupProtectionEnabled);
        AuthenticatorView.Attach(ViewModel);
        LockView.ButtonContent = _startupProtectionEnabled ? "使用 Windows Hello 解锁" : "重试打开";
        LockView.Visibility = Visibility.Collapsed;
        AddAccountView.Visibility = Visibility.Collapsed;
        AuthenticatorView.Visibility = Visibility.Visible;
        _timer.Start();
        TryDeliverCancelledBackupNotice(sessionToken);
        ApplyBackgroundLockDecision(backgroundLockDecision);
        var pendingCapture = _snippingCaptureState.ResumeAfterUnlock(sessionCurrent: true);
        if (pendingCapture.Kind != SnippingCaptureDecisionKind.Ignore)
        {
            ShowAddAccountViewImmediately();
        }

        await ProcessSnippingCaptureDecisionAsync(pendingCapture, sessionToken);
        return true;
    }

    private void Lock_Click(object? sender, EventArgs e)
    {
        if (_startupProtectionEnabled)
        {
            ForceLock("使用 Windows Hello 验证身份后即可查看验证码。");
        }
    }

    private async void Settings_Click(object? sender, EventArgs e)
    {
        if (!TryCaptureUnlockedSession(out var token) || _settingsInProgress)
        {
            return;
        }

        _settingsInProgress = true;
        try
        {
            var currentValue = _startupProtectionEnabled;
            var requested = await _protectionSettingsDialogs.ShowAsync(
                RootGrid.XamlRoot,
                token,
                currentValue);
            if (requested is null ||
                !IsSessionCurrent(token) ||
                requested.Value == currentValue)
            {
                return;
            }

            var reason = requested.Value
                ? "验证身份以开启留白 MFA 启动保护"
                : "验证身份以关闭留白 MFA 启动保护";
            var verified = await VerifySensitiveActionAsync(token, reason);
            if (!StartupProtectionPolicy.CanCommitSettingChange(
                    currentValue,
                    requested.Value,
                    verified,
                    IsSessionCurrent(token)))
            {
                return;
            }

            try
            {
                _startupProtectionSettings.SetEnabled(requested.Value);
            }
            catch (Exception)
            {
                ShowStatus(token, "设置未保存，请重试。", InfoBarSeverity.Warning);
                return;
            }

            _startupProtectionEnabled = requested.Value;
            AuthenticatorView.ApplyStartupProtection(_startupProtectionEnabled);
            if (!_startupProtectionEnabled)
            {
                CancelBackgroundLock();
                _autoLockActivationState.Clear();
            }

            ShowStatus(
                token,
                _startupProtectionEnabled ? "启动保护已开启。" : "启动保护已关闭。",
                InfoBarSeverity.Success);
        }
        finally
        {
            _settingsInProgress = false;
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
        Interlocked.Increment(ref _navigationEpoch);
        ResetPageVisual(AuthenticatorView);
        ResetPageVisual(AddAccountView);
        LockView.Message = message;
        LockView.ButtonContent = _startupProtectionEnabled ? "使用 Windows Hello 解锁" : "重试打开";
        AuthenticatorView.Visibility = Visibility.Collapsed;
        AddAccountView.Visibility = Visibility.Collapsed;
        LockView.Visibility = Visibility.Visible;
        CancelBackgroundLock();
        _autoLockActivationState.Clear();
    }

    private async void ShowAddAccountView(object? sender, EventArgs e)
    {
        if (!TryCaptureUnlockedSession(out var token))
        {
            return;
        }

        var keyboardInitiated = IsKeyboardNavigation();
        try
        {
            await NavigateToAddAccountViewAsync(token, keyboardInitiated);
        }
        catch (Exception)
        {
            if (IsSessionCurrent(token))
            {
                ShowAddAccountViewImmediately();
            }
        }
    }

    private async void ShowAuthenticatorView(object? sender, EventArgs e)
    {
        if (!TryCaptureUnlockedSession(out var token))
        {
            return;
        }

        var keyboardInitiated = IsKeyboardNavigation();
        try
        {
            await NavigateToAuthenticatorViewAsync(token, keyboardInitiated);
        }
        catch (Exception)
        {
            if (IsSessionCurrent(token))
            {
                ShowAuthenticatorView();
            }
        }
    }

    private void ShowAuthenticatorView()
    {
        Interlocked.Increment(ref _navigationEpoch);
        ResetPageVisual(AddAccountView);
        ResetPageVisual(AuthenticatorView);
        AddAccountView.Visibility = Visibility.Collapsed;
        AuthenticatorView.Visibility = Visibility.Visible;
        AuthenticatorView.FocusAddAction();
    }

    private void ShowAddAccountViewImmediately()
    {
        Interlocked.Increment(ref _navigationEpoch);
        ResetPageVisual(AuthenticatorView);
        ResetPageVisual(AddAccountView);
        AuthenticatorView.Visibility = Visibility.Collapsed;
        AddAccountView.Visibility = Visibility.Visible;
        AddAccountView.ClearStatus();
        AddAccountView.FocusPrimaryAction();
    }

    private async Task NavigateToAddAccountViewAsync(long sessionToken, bool keyboardInitiated)
    {
        if (!IsSessionCurrent(sessionToken))
        {
            return;
        }

        var navigationEpoch = Interlocked.Increment(ref _navigationEpoch);
        ResetPageVisual(AuthenticatorView);
        ResetPageVisual(AddAccountView);
        AuthenticatorView.Visibility = Visibility.Collapsed;
        AddAccountView.Visibility = Visibility.Visible;
        AddAccountView.ClearStatus();
        await RunPageEntranceTransitionAsync(
            AddAccountView,
            keyboardInitiated ? 0f : PageTransitionOffset);
        if (navigationEpoch == Volatile.Read(ref _navigationEpoch) && IsSessionCurrent(sessionToken))
        {
            AddAccountView.FocusPrimaryAction();
        }
    }

    private async Task NavigateToAuthenticatorViewAsync(long sessionToken, bool keyboardInitiated)
    {
        if (!IsSessionCurrent(sessionToken))
        {
            return;
        }

        var navigationEpoch = Interlocked.Increment(ref _navigationEpoch);
        ResetPageVisual(AddAccountView);
        ResetPageVisual(AuthenticatorView);
        AddAccountView.Visibility = Visibility.Collapsed;
        AuthenticatorView.Visibility = Visibility.Visible;
        await RunPageEntranceTransitionAsync(
            AuthenticatorView,
            keyboardInitiated ? 0f : -PageTransitionOffset);
        if (navigationEpoch == Volatile.Read(ref _navigationEpoch) && IsSessionCurrent(sessionToken))
        {
            AuthenticatorView.FocusAddAction();
        }
    }

    private async Task RunPageEntranceTransitionAsync(FrameworkElement target, float horizontalOffset)
    {
        var visual = ElementCompositionPreview.GetElementVisual(target);
        var effectiveOffset = AreSystemAnimationsEnabled() ? horizontalOffset : 0f;
        visual.StopAnimation(nameof(visual.Opacity));
        visual.StopAnimation(nameof(visual.Offset));
        visual.Opacity = 0f;
        visual.Offset = new Vector3(effectiveOffset, 0f, 0f);

        var compositor = visual.Compositor;
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var batch = compositor.CreateScopedBatch(CompositionBatchTypes.Animation);
        batch.Completed += (_, _) => completion.TrySetResult(true);

        var opacityAnimation = compositor.CreateScalarKeyFrameAnimation();
        opacityAnimation.Duration = PageTransitionDuration;
        opacityAnimation.InsertKeyFrame(1f, 1f);
        visual.StartAnimation(nameof(visual.Opacity), opacityAnimation);

        if (effectiveOffset != 0f)
        {
            var offsetAnimation = compositor.CreateVector3KeyFrameAnimation();
            offsetAnimation.Duration = PageTransitionDuration;
            offsetAnimation.InsertKeyFrame(1f, Vector3.Zero);
            visual.StartAnimation(nameof(visual.Offset), offsetAnimation);
        }

        batch.End();
        await completion.Task;
        ResetPageVisual(target);
    }

    private bool AreSystemAnimationsEnabled()
    {
        try
        {
            return _uiSettings.AnimationsEnabled;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private bool IsKeyboardNavigation() =>
        FocusManager.GetFocusedElement(RootGrid.XamlRoot) is Control { FocusState: FocusState.Keyboard };

    private static void ResetPageVisual(UIElement element)
    {
        try
        {
            var visual = ElementCompositionPreview.GetElementVisual(element);
            visual.StopAnimation(nameof(visual.Opacity));
            visual.StopAnimation(nameof(visual.Offset));
            visual.Opacity = 1f;
            visual.Offset = Vector3.Zero;
        }
        catch (Exception)
        {
            // 动效清理不得阻止锁定、关闭或页面切换。
        }
    }

    private async void ManualEntry_Click(object? sender, EventArgs e)
    {
        if (!TryCaptureUnlockedSession(out var token))
        {
            return;
        }

        try
        {
            var draft = await _accountDialogs.ShowAddAsync(RootGrid.XamlRoot, token);
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
            ShowAuthenticatorView();
            ShowStatus(token, "账户已添加。", InfoBarSeverity.Success);
        }
        catch (OperationCanceledException)
        {
            // 锁定或用户取消后保持当前安全状态。
        }
        catch (DuplicateMfaSecretException)
        {
            ShowAddAccountStatus(token, "该 MFA 密钥已存在。", InfoBarSeverity.Warning);
        }
        catch (Exception)
        {
            ShowAddAccountStatus(token, "账户未保存，请稍后重试。", InfoBarSeverity.Error);
        }
    }

    private async void ImportImage_Click(object? sender, EventArgs e)
    {
        if (!TryCaptureUnlockedSession(out var token))
        {
            return;
        }

        AddAccountView.ClearStatus();
        try
        {
            var file = await _files.OpenQrImageAsync();
            if (file is null || !IsSessionCurrent(token))
            {
                return;
            }

            await ImportQrFileAsync(file, token, QrImportSource.Image);
        }
        catch (OperationCanceledException)
        {
            // 锁定或用户取消后保持当前安全状态。
        }
        catch (Exception)
        {
            ShowAddAccountStatus(token, "图片账户未导入，请稍后重试。", InfoBarSeverity.Error);
        }
    }

    private async void ScreenshotScan_Click(object? sender, EventArgs e)
    {
        if (!TryCaptureUnlockedSession(out var token))
        {
            return;
        }

        AddAccountView.ClearStatus();
        var correlationId = _snippingCaptureState.Begin(Guid.NewGuid());
        try
        {
            var launched = await _snippingToolCaptureService.LaunchAsync(correlationId);
            if (!launched)
            {
                if (_snippingCaptureState.Abandon(correlationId))
                {
                    ShowAddAccountStatus(
                        token,
                        "未能打开截图工具，请使用图片导入或手动输入",
                        InfoBarSeverity.Error);
                }
            }
        }
        catch (Exception)
        {
            if (_snippingCaptureState.Abandon(correlationId))
            {
                ShowAddAccountStatus(
                    token,
                    "未能打开截图工具，请使用图片导入或手动输入",
                    InfoBarSeverity.Error);
            }
        }
    }

    private async Task<AccountDraft?> ConfirmQrAccountAsync(
        long sessionToken,
        AccountDraft draft,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsSessionCurrent(sessionToken))
        {
            return null;
        }

        var confirmed = await _accountDialogs.ShowImportConfirmationAsync(
            RootGrid.XamlRoot,
            sessionToken,
            draft);
        cancellationToken.ThrowIfCancellationRequested();
        return IsSessionCurrent(sessionToken) ? confirmed : null;
    }

    private async Task<QrAccountSaveOutcome> SaveQrAccountAsync(
        long sessionToken,
        AccountDraft draft,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsSessionCurrent(sessionToken))
        {
            throw new OperationCanceledException(cancellationToken);
        }

        try
        {
            await ViewModel.AddAsync(CreateAccount(draft), cancellationToken);
            return QrAccountSaveOutcome.Imported;
        }
        catch (DuplicateMfaSecretException)
        {
            return QrAccountSaveOutcome.Conflict;
        }
    }

    private async Task ImportQrFileAsync(StorageFile file, long sessionToken, QrImportSource source)
    {
        if (!_sessionCancellation.TryAcquire(sessionToken, out var lease))
        {
            return;
        }

        using (lease)
        {
            var outcome = await _qrImportCoordinator.ImportAsync(file, sessionToken, lease.Token);
            if (!IsSessionCurrent(sessionToken) || lease.Token.IsCancellationRequested)
            {
                return;
            }

            HandleQrImportOutcome(outcome, sessionToken, source);
        }
    }

    private void HandleQrImportOutcome(
        QrAccountImportOutcome outcome,
        long sessionToken,
        QrImportSource source)
    {
        switch (outcome)
        {
            case QrAccountImportOutcome.Imported:
                AuthenticatorView.RefreshState();
                ShowAuthenticatorView();
                ShowStatus(sessionToken, "二维码账户已导入。", InfoBarSeverity.Success);
                break;
            case QrAccountImportOutcome.Cancelled:
                break;
            case QrAccountImportOutcome.InvalidQr:
                ShowAddAccountStatus(
                    sessionToken,
                    source == QrImportSource.Screenshot
                        ? "选区中没有可识别的 TOTP 二维码"
                        : "图片中没有可识别的 TOTP 二维码。",
                    InfoBarSeverity.Warning);
                break;
            case QrAccountImportOutcome.Conflict:
                ShowAddAccountStatus(sessionToken, "该 MFA 密钥已存在。", InfoBarSeverity.Warning);
                break;
            default:
                throw new InvalidOperationException("二维码导入返回了未知结果。");
        }
    }

    private async Task ProcessSnippingCaptureDecisionAsync(
        SnippingCaptureDecision decision,
        long sessionToken)
    {
        switch (decision.Kind)
        {
            case SnippingCaptureDecisionKind.Ignore:
            case SnippingCaptureDecisionKind.Cancelled:
                return;
            case SnippingCaptureDecisionKind.DeferUntilUnlock:
                if (_isUnlocked && !IsSessionCurrent(sessionToken))
                {
                    ForceLock("使用 Windows Hello 验证身份后即可继续导入截图。");
                }

                return;
            case SnippingCaptureDecisionKind.BadRequest:
                ShowAddAccountStatus(
                    sessionToken,
                    "截图功能暂时不可用，请使用图片导入或手动输入",
                    InfoBarSeverity.Warning);
                return;
            case SnippingCaptureDecisionKind.Timeout:
                ShowAddAccountStatus(sessionToken, "截图已超时，请重新截图", InfoBarSeverity.Warning);
                return;
            case SnippingCaptureDecisionKind.Failed:
                ShowAddAccountStatus(
                    sessionToken,
                    "未能打开截图工具，请使用图片导入或手动输入",
                    InfoBarSeverity.Error);
                return;
            case SnippingCaptureDecisionKind.RedeemNow:
                break;
            default:
                return;
        }

        if (string.IsNullOrWhiteSpace(decision.FileAccessToken)
            || !IsSessionCurrent(sessionToken)
            || !_sessionCancellation.TryAcquire(sessionToken, out var lease))
        {
            return;
        }

        using (lease)
        {
            StorageFile file;
            try
            {
                file = await _snippingToolCaptureService.RedeemAsync(decision.FileAccessToken);
            }
            catch (Exception)
            {
                ShowAddAccountStatus(sessionToken, "无法读取截图，请重新截图。", InfoBarSeverity.Error);
                return;
            }

            if (!IsSessionCurrent(sessionToken) || lease.Token.IsCancellationRequested)
            {
                return;
            }

            try
            {
                var outcome = await _qrImportCoordinator.ImportAsync(file, sessionToken, lease.Token);
                if (IsSessionCurrent(sessionToken) && !lease.Token.IsCancellationRequested)
                {
                    HandleQrImportOutcome(outcome, sessionToken, QrImportSource.Screenshot);
                }
            }
            catch (OperationCanceledException) when (lease.Token.IsCancellationRequested)
            {
                // 锁定已使旧会话失效，不显示旧会话提示。
            }
            catch (Exception)
            {
                ShowAddAccountStatus(
                    sessionToken,
                    "未能打开截图工具，请使用图片导入或手动输入",
                    InfoBarSeverity.Error);
            }
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

    private void ShowAddAccountStatus(long token, string message, InfoBarSeverity severity)
    {
        if (!IsSessionCurrent(token))
        {
            return;
        }

        ShowAddAccountViewImmediately();
        AddAccountView.ShowStatus(message, severity);
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
