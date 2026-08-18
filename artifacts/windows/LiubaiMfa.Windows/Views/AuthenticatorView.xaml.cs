using System.Collections.Specialized;
using System.ComponentModel;
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
    private EventHandler<object>? _statusTimerTickHandler;
    private long _statusEpoch;
    private long _attachEpoch;
    private string? _requestedRevealAccountId;
    private string? _highlightedAccountId;

    public AuthenticatorView()
    {
        InitializeComponent();
        _revealTimer.Tick += RevealTimer_Tick;
    }

    public event EventHandler? AddRequested;
    public event EventHandler? ImportQrRequested;
    public event EventHandler? LockRequested;
    public event EventHandler? ImportBackupRequested;
    public event EventHandler? ExportBackupRequested;
    public event EventHandler<AccountRequestedEventArgs>? CopyRequested;
    public event EventHandler<AccountRequestedEventArgs>? EditRequested;
    public event EventHandler<AccountRequestedEventArgs>? ShowSecretRequested;
    public event EventHandler<AccountRequestedEventArgs>? DeleteRequested;

    public void Initialize(MainViewModel viewModel) => Attach(viewModel);

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
        ConsumeAndRevealPending();
    }

    public void Detach()
    {
        var detachedViewModel = _viewModel;
        _attachEpoch++;
        _revealTimer.Stop();
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
        _statusTimerTickHandler = (_, _) =>
        {
            _statusTimer.Stop();
            if (_viewModel is not null && statusEpoch == _statusEpoch)
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
            ConsumeAndRevealPending();
        }
    }

    private void Groups_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        _isSynchronizingGroups = true;
        var attachedViewModel = _viewModel;
        _ = DispatcherQueue.TryEnqueue(() =>
        {
            if (ReferenceEquals(_viewModel, attachedViewModel))
            {
                SynchronizeGroupSelection();
            }
        });
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

    private void EmptyAddButton_Click(object sender, RoutedEventArgs e) => AddRequested?.Invoke(this, EventArgs.Empty);

    private void EmptyImportQrButton_Click(object sender, RoutedEventArgs e) => ImportQrRequested?.Invoke(this, EventArgs.Empty);

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

    private void AddButton_Click(object sender, RoutedEventArgs e) => AddRequested?.Invoke(this, EventArgs.Empty);
    private void ImportQrButton_Click(object sender, RoutedEventArgs e) => ImportQrRequested?.Invoke(this, EventArgs.Empty);
    private void LockButton_Click(object sender, RoutedEventArgs e) => LockRequested?.Invoke(this, EventArgs.Empty);
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

    private void ConsumeAndRevealPending()
    {
        var attachedViewModel = _viewModel;
        var accountId = attachedViewModel?.ConsumePendingReveal();
        if (attachedViewModel is null || accountId is null)
        {
            return;
        }

        var attachEpoch = _attachEpoch;
        _requestedRevealAccountId = accountId;
        _ = DispatcherQueue.TryEnqueue(() => RevealWhenContainerReady(attachedViewModel, attachEpoch, accountId));
    }

    private void RevealWhenContainerReady(MainViewModel attachedViewModel, long attachEpoch, string accountId)
    {
        if (!ReferenceEquals(_viewModel, attachedViewModel)
            || _attachEpoch != attachEpoch
            || _requestedRevealAccountId != accountId)
        {
            return;
        }

        var account = FindAccount(accountId);
        if (account is null)
        {
            return;
        }

        AccountsList.ScrollIntoView(account);
        _ = DispatcherQueue.TryEnqueue(() => FocusAndHighlightAccount(attachedViewModel, attachEpoch, accountId));
    }

    private void FocusAndHighlightAccount(MainViewModel attachedViewModel, long attachEpoch, string accountId)
    {
        if (!ReferenceEquals(_viewModel, attachedViewModel)
            || _attachEpoch != attachEpoch
            || _requestedRevealAccountId != accountId)
        {
            return;
        }

        var account = FindAccount(accountId);
        if (account is null)
        {
            return;
        }

        ClearRevealHighlight();
        account.SetRevealHighlight(true);
        _highlightedAccountId = accountId;
        _revealTimer.Stop();
        _revealTimer.Start();

        if (AccountsList.ContainerFromItem(account) is DependencyObject container)
        {
            FindAccountRow(container, accountId)?.Focus(FocusState.Programmatic);
        }
    }

    private void RevealTimer_Tick(object? sender, object e)
    {
        _revealTimer.Stop();
        ClearRevealHighlight();
    }

    private void ClearRevealHighlight()
    {
        if (_highlightedAccountId is not null)
        {
            FindAccount(_highlightedAccountId)?.SetRevealHighlight(false);
        }

        _highlightedAccountId = null;
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
