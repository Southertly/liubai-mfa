using System.Collections.Specialized;
using System.ComponentModel;
using LiubaiMfa.Windows.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace LiubaiMfa.Windows.Views;

public sealed partial class AuthenticatorView : UserControl
{
    private MainViewModel? _viewModel;
    private bool _isSynchronizingGroups;
    private bool _isSynchronizingSearch;

    public AuthenticatorView()
    {
        InitializeComponent();
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

        _viewModel = viewModel;
        _viewModel.PropertyChanged += ViewModel_PropertyChanged;
        _viewModel.Groups.CollectionChanged += Groups_CollectionChanged;
        DataContext = viewModel;
        GroupsList.ItemsSource = viewModel.Groups;
        AccountsList.ItemsSource = viewModel.VisibleAccounts;
        SynchronizeSearchText();
        SynchronizeGroupSelection();
        RefreshState();
    }

    public void Detach()
    {
        var detachedViewModel = _viewModel;
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

    private void CopyButton_Click(object sender, RoutedEventArgs e)
    {
        if (TryGetAccountId(sender, out var id))
        {
            CopyRequested?.Invoke(this, new AccountRequestedEventArgs(id));
        }
    }

    private void EditButton_Click(object sender, RoutedEventArgs e)
    {
        if (TryGetAccountId(sender, out var id))
        {
            EditRequested?.Invoke(this, new AccountRequestedEventArgs(id));
        }
    }

    private void ShowSecretButton_Click(object sender, RoutedEventArgs e)
    {
        if (TryGetAccountId(sender, out var id))
        {
            ShowSecretRequested?.Invoke(this, new AccountRequestedEventArgs(id));
        }
    }

    private void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        if (TryGetAccountId(sender, out var id))
        {
            DeleteRequested?.Invoke(this, new AccountRequestedEventArgs(id));
        }
    }

    private static bool TryGetAccountId(object sender, out string id)
    {
        if (sender is Button { Tag: string accountId })
        {
            id = accountId;
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
