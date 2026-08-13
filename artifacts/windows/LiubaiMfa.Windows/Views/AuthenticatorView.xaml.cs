using System.ComponentModel;
using LiubaiMfa.Windows.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace LiubaiMfa.Windows.Views;

public sealed partial class AuthenticatorView : UserControl
{
    private MainViewModel? _viewModel;

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

    public void Initialize(MainViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        if (ReferenceEquals(_viewModel, viewModel))
        {
            return;
        }

        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= ViewModel_PropertyChanged;
        }

        _viewModel = viewModel;
        DataContext = viewModel;
        _viewModel.PropertyChanged += ViewModel_PropertyChanged;
        RefreshState();
    }

    public void ShowStatus(string message, InfoBarSeverity severity)
    {
        StatusBar.Message = message;
        StatusBar.Severity = severity;
        StatusBar.IsOpen = true;
    }

    public void RefreshState()
    {
        var hasAccounts = _viewModel?.HasAccounts == true;
        var hasVisibleAccounts = _viewModel?.HasVisibleAccounts == true;
        EmptyState.Visibility = hasAccounts ? Visibility.Collapsed : Visibility.Visible;
        NoResultsState.Visibility = hasAccounts && !hasVisibleAccounts ? Visibility.Visible : Visibility.Collapsed;
        AccountsList.Visibility = hasVisibleAccounts ? Visibility.Visible : Visibility.Collapsed;
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
        if (e.PropertyName is nameof(MainViewModel.HasAccounts) or nameof(MainViewModel.HasVisibleAccounts))
        {
            RefreshState();
        }
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
