using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using LiubaiMfa.Core.Models;
using LiubaiMfa.Windows.Services;

namespace LiubaiMfa.Windows.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged
{
    private readonly IAccountRepository _repository;
    private readonly List<MfaAccount> _accounts = [];
    private string _searchText = string.Empty;
    private string? _selectedGroup;
    private bool _isLoaded;
    private bool _hasLoadFailure;

    public MainViewModel(EncryptedAccountRepository repository)
        : this((IAccountRepository)repository)
    {
    }

    internal MainViewModel(IAccountRepository repository)
    {
        _repository = repository;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<AccountItemViewModel> VisibleAccounts { get; } = new();

    public ObservableCollection<GroupFilterItemViewModel> Groups { get; } = new();

    public ObservableCollection<AccountItemViewModel> Accounts => VisibleAccounts;

    public string SearchText
    {
        get => _searchText;
        set
        {
            var normalized = value?.Trim() ?? string.Empty;
            if (SetField(ref _searchText, normalized))
            {
                RefreshDerivedState();
            }
        }
    }

    public string? SelectedGroup
    {
        get => _selectedGroup;
        set
        {
            var normalized = NormalizeGroup(value);
            if (SetField(ref _selectedGroup, normalized))
            {
                RefreshDerivedState();
            }
        }
    }

    public bool IsLoaded
    {
        get => _isLoaded;
        private set => SetField(ref _isLoaded, value);
    }

    public bool HasAccounts => _accounts.Count > 0;

    public bool HasVisibleAccounts => VisibleAccounts.Count > 0;

    public bool HasLoadFailure
    {
        get => _hasLoadFailure;
        private set => SetField(ref _hasLoadFailure, value);
    }

    internal IReadOnlyList<MfaAccount> AccountsSnapshot => _accounts.ToArray();

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        if (IsLoaded)
        {
            return;
        }

        try
        {
            var accounts = await _repository.LoadAsync(cancellationToken);
            _accounts.Clear();
            _accounts.AddRange(accounts);
            HasLoadFailure = false;
            IsLoaded = true;
            RefreshDerivedState();
        }
        catch
        {
            _accounts.Clear();
            IsLoaded = false;
            HasLoadFailure = true;
            RefreshDerivedState();
            throw;
        }
    }

    public async Task AddAsync(MfaAccount account, CancellationToken cancellationToken = default)
    {
        EnsureWritesAllowed();
        if (_accounts.Any(item => item.Secret == account.Secret))
        {
            throw new InvalidOperationException("该 MFA 密钥已经存在。");
        }

        _accounts.Add(account);
        await SaveAsync(cancellationToken);
        RefreshDerivedState();
    }

    public async Task UpdateAsync(MfaAccount account, CancellationToken cancellationToken = default)
    {
        EnsureWritesAllowed();
        var existingIndex = _accounts.FindIndex(item => item.Id == account.Id);
        if (existingIndex < 0)
        {
            throw new InvalidOperationException("账户不存在。");
        }

        _accounts[existingIndex] = account;
        await SaveAsync(cancellationToken);
        RefreshDerivedState();
    }

    public async Task DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        EnsureWritesAllowed();
        var existingIndex = _accounts.FindIndex(item => item.Id == id);
        if (existingIndex < 0)
        {
            RefreshDerivedState();
            return;
        }

        _accounts.RemoveAt(existingIndex);
        await SaveAsync(cancellationToken);
        RefreshDerivedState();
    }

    public async Task<(int Imported, int Skipped)> ImportAsync(
        IEnumerable<MfaAccount> incoming,
        CancellationToken cancellationToken = default)
    {
        EnsureWritesAllowed();
        var existingSecrets = _accounts.Select(item => item.Secret).ToHashSet(StringComparer.Ordinal);
        var imported = 0;
        var skipped = 0;
        foreach (var account in incoming)
        {
            if (string.IsNullOrWhiteSpace(account.Secret) || !existingSecrets.Add(account.Secret))
            {
                skipped++;
                continue;
            }

            _accounts.Add(account with
            {
                Id = string.IsNullOrWhiteSpace(account.Id) ? Guid.NewGuid().ToString("N") : account.Id,
                CreatedAt = account.CreatedAt == 0 ? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() : account.CreatedAt,
            });
            imported++;
        }

        await SaveAsync(cancellationToken);
        RefreshDerivedState();
        return (imported, skipped);
    }

    public void UpdateCodes()
    {
        foreach (var account in VisibleAccounts)
        {
            account.UpdateCode();
        }
    }

    public IReadOnlyList<MfaAccount> Snapshot() => AccountsSnapshot;

    public void Unload()
    {
        _accounts.Clear();
        IsLoaded = false;
        RefreshDerivedState();
    }

    private async Task SaveAsync(CancellationToken cancellationToken) =>
        await _repository.SaveAsync(_accounts.ToArray(), cancellationToken);

    private void RefreshDerivedState()
    {
        var groupCounts = AccountQuery.BuildGroupCounts(_accounts);
        if (_selectedGroup is not null && !groupCounts.Any(group =>
            group.Value is not null
            && string.Equals(group.Value, _selectedGroup, StringComparison.CurrentCultureIgnoreCase)))
        {
            _selectedGroup = null;
            OnPropertyChanged(nameof(SelectedGroup));
        }

        VisibleAccounts.Clear();
        foreach (var account in AccountQuery.FilterAndSort(_accounts, SearchText, SelectedGroup))
        {
            VisibleAccounts.Add(new AccountItemViewModel(account));
        }

        Groups.Clear();
        foreach (var group in groupCounts)
        {
            Groups.Add(new GroupFilterItemViewModel(group.Value, group.Label, group.Count));
        }

        OnPropertyChanged(nameof(HasAccounts));
        OnPropertyChanged(nameof(HasVisibleAccounts));
    }

    private void EnsureWritesAllowed()
    {
        if (HasLoadFailure)
        {
            throw new InvalidOperationException("本地账户数据加载失败，已禁止写入以避免覆盖数据。");
        }
    }

    private static string? NormalizeGroup(string? group) =>
        string.IsNullOrWhiteSpace(group) ? null : group.Trim();

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
