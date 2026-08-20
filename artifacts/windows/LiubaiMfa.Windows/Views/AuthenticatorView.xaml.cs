using System.Collections.Specialized;
using System.ComponentModel;
using LiubaiMfa.Windows.Services;
using LiubaiMfa.Windows.ViewModels;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;
using Windows.UI.Core;

namespace LiubaiMfa.Windows.Views;

public sealed partial class AuthenticatorView : UserControl
{
    private MainViewModel? _viewModel;
    private bool _isSynchronizingGroups;
    private bool _isSynchronizingSearch;
    private readonly DispatcherTimer _revealTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private readonly DispatcherTimer _statusTimer = new() { Interval = TimeSpan.FromSeconds(4) };
    private EventHandler<object>? _revealTimerTickHandler;
    private EventHandler<object>? _statusTimerTickHandler;
    private long _statusEpoch;
    private long _attachEpoch;
    private long _revealEpoch;
    private string? _requestedRevealAccountId;
    private AccountItemViewModel? _highlightedAccount;

    public AuthenticatorView()
    {
        InitializeComponent();
        AccountsList.LayoutUpdated += AccountsList_LayoutUpdated;
    }

    public event EventHandler? AddRequested;
    public event EventHandler? LockRequested;
    public event EventHandler? SettingsRequested;
    public event EventHandler? ImportBackupRequested;
    public event EventHandler? ExportBackupRequested;
    public event EventHandler<AccountRequestedEventArgs>? CopyRequested;
    public event EventHandler<AccountRequestedEventArgs>? EditRequested;
    public event EventHandler<AccountRequestedEventArgs>? ShowSecretRequested;
    public event EventHandler<AccountRequestedEventArgs>? DeleteRequested;

    public void Initialize(MainViewModel viewModel) => Attach(viewModel);

    public void FocusAddAction() => AddButton.Focus(FocusState.Programmatic);

    public void Attach(MainViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        Detach();

        _attachEpoch++;
        _viewModel = viewModel;
        _viewModel.PropertyChanged += ViewModel_PropertyChanged;
        _viewModel.Groups.CollectionChanged += Groups_CollectionChanged;
        DataContext = viewModel;
        GroupsList.ItemsSource = viewModel.Groups;
        AccountsList.ItemsSource = viewModel.VisibleAccounts;
        SynchronizeSearchText();
        SynchronizeGroupSelection();
        RefreshState();
        BeginRevealPending();
    }

    public void Detach()
    {
        var detachedViewModel = _viewModel;
        _attachEpoch++;
        StopRevealTimer();
        _revealEpoch++;
        _statusTimer.Stop();
        _statusEpoch++;
        if (_statusTimerTickHandler is not null)
        {
            _statusTimer.Tick -= _statusTimerTickHandler;
            _statusTimerTickHandler = null;
        }
        ClearRevealHighlight();
        _requestedRevealAccountId = null;
        _isSynchronizingGroups = true;
        _isSynchronizingSearch = true;
        try
        {
            SearchBox.Text = string.Empty;
            if (detachedViewModel is not null)
            {
                detachedViewModel.PropertyChanged -= ViewModel_PropertyChanged;
                detachedViewModel.Groups.CollectionChanged -= Groups_CollectionChanged;
                detachedViewModel.SearchText = string.Empty;
            }

            GroupsList.SelectedItem = null;
            GroupsList.ItemsSource = null;
            AccountsList.ItemsSource = null;
            DataContext = null;
            _viewModel = null;

            StatusBar.IsOpen = false;
            StatusBar.Message = string.Empty;
            EmptyState.Visibility = Visibility.Collapsed;
            NoResultsState.Visibility = Visibility.Collapsed;
            AccountsList.Visibility = Visibility.Collapsed;
        }
        finally
        {
            _isSynchronizingGroups = false;
            _isSynchronizingSearch = false;
        }
    }

    public void ShowStatus(string message, InfoBarSeverity severity)
    {
        if (_viewModel is null)
        {
            return;
        }

        StatusBar.Message = message;
        StatusBar.Severity = severity;
        StatusBar.IsOpen = true;
        _statusTimer.Stop();
        _statusEpoch++;
        if (_statusTimerTickHandler is not null)
        {
            _statusTimer.Tick -= _statusTimerTickHandler;
        }

        var statusEpoch = _statusEpoch;
        var attachEpoch = _attachEpoch;
        _statusTimerTickHandler = (_, _) =>
        {
            if (!EpochCallbackGuard.IsCurrent(_statusEpoch, statusEpoch, _attachEpoch, attachEpoch, null, null))
            {
                return;
            }

            _statusTimer.Stop();
            if (_viewModel is not null)
            {
                StatusBar.IsOpen = false;
                StatusBar.Message = string.Empty;
            }
        };
        _statusTimer.Tick += _statusTimerTickHandler;
        _statusTimer.Start();
    }

    public void RefreshState()
    {
        if (_viewModel is null)
        {
            EmptyState.Visibility = Visibility.Collapsed;
            NoResultsState.Visibility = Visibility.Collapsed;
            AccountsList.Visibility = Visibility.Collapsed;
            return;
        }

        EmptyState.Visibility = _viewModel.HasAccounts ? Visibility.Collapsed : Visibility.Visible;
        NoResultsState.Visibility = _viewModel.HasAccounts && !_viewModel.HasVisibleAccounts
            ? Visibility.Visible
            : Visibility.Collapsed;
        AccountsList.Visibility = _viewModel.HasVisibleAccounts ? Visibility.Visible : Visibility.Collapsed;
    }

    public void ApplyStartupProtection(bool enabled)
    {
        LockButton.Visibility = StartupProtectionPolicy.ShouldShowManualLock(enabled)
            ? Visibility.Visible
            : Visibility.Collapsed;
        ProtectionStatusText.Text = enabled ? "本机加密 · Windows Hello 保护" : "本机加密存储";
    }

    public AccountItemViewModel? FindAccount(string id) =>
        _viewModel?.VisibleAccounts.FirstOrDefault(item => item.Id == id);

    public void ScrollAccountIntoView(string id)
    {
        var account = FindAccount(id);
        if (account is not null)
        {
            AccountsList.ScrollIntoView(account);
        }
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.SearchText))
        {
            SynchronizeSearchText();
        }

        if (e.PropertyName == nameof(MainViewModel.SelectedGroup))
        {
            SynchronizeGroupSelection();
        }

        if (e.PropertyName is nameof(MainViewModel.HasAccounts) or nameof(MainViewModel.HasVisibleAccounts))
        {
            SynchronizeGroupSelection();
            RefreshState();
        }

        if (e.PropertyName == nameof(MainViewModel.PendingRevealAccountId))
        {
            BeginRevealPending();
        }
    }

    private void Groups_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        _isSynchronizingGroups = true;
        var attachedViewModel = _viewModel;
        var attachEpoch = _attachEpoch;
        var enqueued = DispatcherQueue.TryEnqueue(() =>
        {
            if (ReferenceEquals(_viewModel, attachedViewModel) && _attachEpoch == attachEpoch)
            {
                SynchronizeGroupSelection();
            }
        });

        if (!enqueued)
        {
            _isSynchronizingGroups = false;
        }
    }

    private void SynchronizeGroupSelection()
    {
        if (_viewModel is null)
        {
            return;
        }

        _isSynchronizingGroups = true;
        try
        {
            GroupsList.SelectedItem = _viewModel.Groups.FirstOrDefault(item =>
                string.Equals(item.Value, _viewModel.SelectedGroup, StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            _isSynchronizingGroups = false;
        }
    }

    private void SynchronizeSearchText()
    {
        if (_viewModel is null || SearchBox.Text == _viewModel.SearchText)
        {
            return;
        }

        _isSynchronizingSearch = true;
        SearchBox.Text = _viewModel.SearchText;
        _isSynchronizingSearch = false;
    }

    private void SearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (!_isSynchronizingSearch && _viewModel is not null)
        {
            _viewModel.SearchText = sender.Text;
        }
    }

    private void EmptyAddButton_Click(object sender, RoutedEventArgs e) => this.AddRequested?.Invoke(this, EventArgs.Empty);

    private void ClearFiltersButton_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel is null)
        {
            return;
        }

        _viewModel.SearchText = string.Empty;
        _viewModel.SelectedGroup = null;
    }

    private void GroupsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isSynchronizingGroups || _viewModel is null || GroupsList.SelectedItem is not GroupFilterItemViewModel selected)
        {
            return;
        }

        _viewModel.SelectedGroup = selected.Value;
    }

    private void AddButton_Click(object sender, RoutedEventArgs e) => this.AddRequested?.Invoke(this, EventArgs.Empty);
    private void LockButton_Click(object sender, RoutedEventArgs e) => LockRequested?.Invoke(this, EventArgs.Empty);
    private void SettingsButton_Click(object sender, RoutedEventArgs e) => SettingsRequested?.Invoke(this, EventArgs.Empty);
    private void ImportBackupItem_Click(object sender, RoutedEventArgs e) => ImportBackupRequested?.Invoke(this, EventArgs.Empty);
    private void ExportBackupItem_Click(object sender, RoutedEventArgs e) => ExportBackupRequested?.Invoke(this, EventArgs.Empty);

    private void CopyCodeButton_Click(object sender, RoutedEventArgs e)
    {
        if (TryGetAccountId(sender, out var id))
        {
            CopyRequested?.Invoke(this, new AccountRequestedEventArgs(id));
        }
    }

    private void EditSwipeItem_Invoked(SwipeItem sender, SwipeItemInvokedEventArgs args)
    {
        if (TryGetAccountId(args.SwipeControl, out var id))
        {
            RaiseAccountAction(id, AccountAction.Edit);
        }
    }

    private void ShowSecretSwipeItem_Invoked(SwipeItem sender, SwipeItemInvokedEventArgs args)
    {
        if (TryGetAccountId(args.SwipeControl, out var id))
        {
            RaiseAccountAction(id, AccountAction.ViewSecret);
        }
    }

    private void DeleteSwipeItem_Invoked(SwipeItem sender, SwipeItemInvokedEventArgs args)
    {
        if (TryGetAccountId(args.SwipeControl, out var id))
        {
            RaiseAccountAction(id, AccountAction.Delete);
        }
    }

    private void EditMenuItem_Click(object sender, RoutedEventArgs e) => RaiseMenuAccountAction(sender, AccountAction.Edit);

    private void ShowSecretMenuItem_Click(object sender, RoutedEventArgs e) => RaiseMenuAccountAction(sender, AccountAction.ViewSecret);

    private void DeleteMenuItem_Click(object sender, RoutedEventArgs e) => RaiseMenuAccountAction(sender, AccountAction.Delete);

    private void AccountRow_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (!TryGetAccountId(sender, out var id))
        {
            return;
        }

        var controlDown = IsKeyDown(VirtualKey.Control);
        var shiftDown = IsKeyDown(VirtualKey.Shift);
        var altDown = IsKeyDown(VirtualKey.Menu) || IsKeyDown(VirtualKey.LeftMenu) || IsKeyDown(VirtualKey.RightMenu);
        var windowsDown = IsKeyDown(VirtualKey.LeftWindows) || IsKeyDown(VirtualKey.RightWindows);

        if (e.Key == VirtualKey.C && AccountActionPolicy.IsCopyShortcut(controlDown, shiftDown, altDown, windowsDown, e.KeyStatus.WasKeyDown))
        {
            CopyRequested?.Invoke(this, new AccountRequestedEventArgs(id));
            e.Handled = true;
        }
    }

    private void RaiseMenuAccountAction(object sender, AccountAction action)
    {
        if (TryGetAccountId(sender, out var id))
        {
            RaiseAccountAction(id, action);
        }
    }

    private void RaiseAccountAction(string accountId, AccountAction action)
    {
        var args = new AccountRequestedEventArgs(accountId);
        switch (action)
        {
            case AccountAction.Edit:
                EditRequested?.Invoke(this, args);
                break;
            case AccountAction.ViewSecret:
                ShowSecretRequested?.Invoke(this, args);
                break;
            case AccountAction.Delete:
                DeleteRequested?.Invoke(this, args);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(action), action, null);
        }
    }

    private static bool IsKeyDown(VirtualKey key) =>
        (InputKeyboardSource.GetKeyStateForCurrentThread(key) & CoreVirtualKeyStates.Down) == CoreVirtualKeyStates.Down;

    private void BeginRevealPending()
    {
        var attachedViewModel = _viewModel;
        var accountId = attachedViewModel?.PendingRevealAccountId;
        if (attachedViewModel is null || accountId is null)
        {
            return;
        }

        StopRevealTimer();
        ClearRevealHighlight();
        var attachEpoch = _attachEpoch;
        _requestedRevealAccountId = accountId;
        var revealEpoch = ++_revealEpoch;
        ScrollPendingAccountIntoView(attachedViewModel, attachEpoch, revealEpoch, accountId);
        _ = DispatcherQueue.TryEnqueue(() => TryApplyPendingReveal(attachedViewModel, attachEpoch, revealEpoch, accountId));
    }

    private void AccountsList_LayoutUpdated(object? sender, object e)
    {
        var attachedViewModel = _viewModel;
        var accountId = _requestedRevealAccountId;
        if (attachedViewModel is null || accountId is null)
        {
            return;
        }

        TryApplyPendingReveal(attachedViewModel, _attachEpoch, _revealEpoch, accountId);
    }

    private void ScrollPendingAccountIntoView(MainViewModel attachedViewModel, long attachEpoch, long revealEpoch, string accountId)
    {
        if (!IsCurrentRevealRequest(attachedViewModel, attachEpoch, revealEpoch, accountId))
        {
            return;
        }

        var account = FindAccount(accountId);
        if (account is null)
        {
            return;
        }

        AccountsList.ScrollIntoView(account);
    }

    private void TryApplyPendingReveal(MainViewModel attachedViewModel, long attachEpoch, long revealEpoch, string accountId)
    {
        if (!IsCurrentRevealRequest(attachedViewModel, attachEpoch, revealEpoch, accountId))
        {
            return;
        }

        var account = FindAccount(accountId);
        if (account is null)
        {
            return;
        }

        if (AccountsList.ContainerFromItem(account) is not DependencyObject container)
        {
            return;
        }

        var row = FindAccountRow(container, accountId);
        if (row is null)
        {
            return;
        }

        ClearRevealHighlight();
        account.SetRevealHighlight(true);
        _highlightedAccount = account;
        if (!row.Focus(FocusState.Programmatic))
        {
            ClearRevealHighlight();
            return;
        }

        if (!attachedViewModel.ConsumePendingReveal(accountId))
        {
            ClearRevealHighlight();
            return;
        }

        _requestedRevealAccountId = null;
        StartRevealTimer(attachEpoch, revealEpoch, accountId);
    }

    private bool IsCurrentRevealRequest(MainViewModel attachedViewModel, long attachEpoch, long revealEpoch, string accountId) =>
        ReferenceEquals(_viewModel, attachedViewModel)
        && _attachEpoch == attachEpoch
        && _revealEpoch == revealEpoch
        && _requestedRevealAccountId == accountId
        && attachedViewModel.PendingRevealAccountId == accountId;

    private void StartRevealTimer(long attachEpoch, long revealEpoch, string accountId)
    {
        _revealTimer.Stop();
        if (_revealTimerTickHandler is not null)
        {
            _revealTimer.Tick -= _revealTimerTickHandler;
        }

        var timerEpoch = ++_revealEpoch;
        _revealTimerTickHandler = (_, _) =>
        {
            if (!EpochCallbackGuard.IsCurrent(
                    _revealEpoch,
                    timerEpoch,
                    _attachEpoch,
                    attachEpoch,
                    _highlightedAccount?.Id,
                    accountId))
            {
                return;
            }

            _revealTimer.Stop();
            ClearRevealHighlight();
        };
        _revealTimer.Tick += _revealTimerTickHandler;
        _revealTimer.Start();
    }

    private void StopRevealTimer()
    {
        _revealTimer.Stop();
        if (_revealTimerTickHandler is not null)
        {
            _revealTimer.Tick -= _revealTimerTickHandler;
            _revealTimerTickHandler = null;
        }
    }

    private void ClearRevealHighlight()
    {
        _highlightedAccount?.SetRevealHighlight(false);
        _highlightedAccount = null;
    }

    private static Border? FindAccountRow(DependencyObject parent, string accountId)
    {
        if (parent is Border { Tag: string tag } border && tag == accountId)
        {
            return border;
        }

        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var row = FindAccountRow(VisualTreeHelper.GetChild(parent, index), accountId);
            if (row is not null)
            {
                return row;
            }
        }

        return null;
    }

    private static bool TryGetAccountId(object? sender, out string id)
    {
        if (sender is FrameworkElement { Tag: string accountId })
        {
            id = accountId;
            return true;
        }

        if (sender is FrameworkElement { DataContext: AccountItemViewModel account })
        {
            id = account.Id;
            return true;
        }

        id = string.Empty;
        return false;
    }
}

public sealed class AccountRequestedEventArgs : EventArgs
{
    public AccountRequestedEventArgs(string accountId)
    {
        AccountId = accountId;
    }

    public string AccountId { get; }
}
