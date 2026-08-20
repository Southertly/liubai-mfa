using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using LiubaiMfa.Core.Models;
using LiubaiMfa.Windows.Services;

namespace LiubaiMfa.Windows.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged
{
    private readonly IAccountRepository _repository;
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly object _stateGate = new();
    private readonly List<MfaAccount> _accounts = [];
    private readonly Dictionary<string, AccountItemViewModel> _accountItemCache = new(StringComparer.Ordinal);
    private long _lifecycleEpoch;
    private string _searchText = string.Empty;
    private string? _selectedGroup;
    private string? _pendingRevealAccountId;
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
        get
        {
            lock (_stateGate)
            {
                return _searchText;
            }
        }
        set
        {
            lock (_stateGate)
            {
                var normalized = value?.Trim() ?? string.Empty;
                if (SetField(ref _searchText, normalized))
                {
                    RefreshDerivedState();
                }
            }
        }
    }

    public string? SelectedGroup
    {
        get
        {
            lock (_stateGate)
            {
                return _selectedGroup;
            }
        }
        set
        {
            lock (_stateGate)
            {
                var normalized = NormalizeGroup(value);
                if (SetField(ref _selectedGroup, normalized))
                {
                    RefreshDerivedState();
                }
            }
        }
    }

    public bool IsLoaded
    {
        get
        {
            lock (_stateGate)
            {
                return _isLoaded;
            }
        }
        private set => SetField(ref _isLoaded, value);
    }

    public bool HasAccounts
    {
        get
        {
            lock (_stateGate)
            {
                return _accounts.Count > 0;
            }
        }
    }

    public bool HasVisibleAccounts
    {
        get
        {
            lock (_stateGate)
            {
                return VisibleAccounts.Count > 0;
            }
        }
    }

    public bool HasLoadFailure
    {
        get
        {
            lock (_stateGate)
            {
                return _hasLoadFailure;
            }
        }
        private set => SetField(ref _hasLoadFailure, value);
    }

    public string? PendingRevealAccountId
    {
        get
        {
            lock (_stateGate)
            {
                return _pendingRevealAccountId;
            }
        }
    }

    internal IReadOnlyList<MfaAccount> AccountsSnapshot
    {
        get
        {
            lock (_stateGate)
            {
                return _accounts.ToArray();
            }
        }
    }

    internal int AccountItemCacheCount
    {
        get
        {
            lock (_stateGate)
            {
                return _accountItemCache.Count;
            }
        }
    }

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        var operationEpoch = Volatile.Read(ref _lifecycleEpoch);
        await _writeGate.WaitAsync(cancellationToken);
        try
        {
            lock (_stateGate)
            {
                ThrowIfLifecycleInvalid(operationEpoch);

                if (_isLoaded)
                {
                    return;
                }
            }

            try
            {
                var accounts = await _repository.LoadAsync(cancellationToken);
                lock (_stateGate)
                {
                    ThrowIfLifecycleInvalid(operationEpoch);

                    _accounts.Clear();
                    _accountItemCache.Clear();
                    _accounts.AddRange(accounts);
                    HasLoadFailure = false;
                    IsLoaded = true;
                    RefreshDerivedState();
                }
            }
            catch
            {
                lock (_stateGate)
                {
                    ThrowIfLifecycleInvalid(operationEpoch);
                    _accounts.Clear();
                    IsLoaded = false;
                    HasLoadFailure = true;
                    RefreshDerivedState();
                }

                throw;
            }
        }
        finally
        {
            _writeGate.Release();
        }
    }

    public Task<MfaAccount> AddAsync(MfaAccount account, CancellationToken cancellationToken = default)
    {
        var operationEpoch = Volatile.Read(ref _lifecycleEpoch);
        return ExecuteWriteAsync(async () =>
        {
            MfaAccount[] candidate;
            lock (_stateGate)
            {
                EnsureWritesAllowed(operationEpoch);

                if (_accounts.Any(item => item.Secret == account.Secret))
                {
                    throw new DuplicateMfaSecretException();
                }

                var actual = account with { Id = CreateUniqueId(account.Id, _accounts.Select(item => item.Id)) };
                candidate = _accounts.Append(actual).ToArray();
                account = actual;
            }

            await CommitAsync(candidate, operationEpoch, cancellationToken, account.Id);
            return account;
        }, cancellationToken);
    }

    public Task UpdateAsync(MfaAccount account, CancellationToken cancellationToken = default)
    {
        var operationEpoch = Volatile.Read(ref _lifecycleEpoch);
        return ExecuteWriteAsync(async () =>
        {
            MfaAccount[] candidate;
            lock (_stateGate)
            {
                EnsureWritesAllowed(operationEpoch);

                var existingIndex = _accounts.FindIndex(item => item.Id == account.Id);
                if (existingIndex < 0)
                {
                    throw new InvalidOperationException("账户不存在。");
                }

                candidate = _accounts.ToArray();
                candidate[existingIndex] = account;
            }

            await CommitAsync(candidate, operationEpoch, cancellationToken);
        }, cancellationToken);
    }

    public Task DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        var operationEpoch = Volatile.Read(ref _lifecycleEpoch);
        return ExecuteWriteAsync(async () =>
        {
            List<MfaAccount>? candidate;
            lock (_stateGate)
            {
                EnsureWritesAllowed(operationEpoch);

                var existingIndex = _accounts.FindIndex(item => item.Id == id);
                if (existingIndex < 0)
                {
                    RefreshDerivedState();
                    return;
                }

                candidate = _accounts.ToList();
                candidate.RemoveAt(existingIndex);
            }

            await CommitAsync(candidate, operationEpoch, cancellationToken);
        }, cancellationToken);
    }

    public Task<(int Imported, int Skipped)> ImportAsync(
        IEnumerable<MfaAccount> incoming,
        CancellationToken cancellationToken = default)
    {
        var operationEpoch = Volatile.Read(ref _lifecycleEpoch);
        return ExecuteWriteAsync(async () =>
        {
            List<MfaAccount> candidate;
            lock (_stateGate)
            {
                EnsureWritesAllowed(operationEpoch);

                candidate = _accounts.ToList();
            }

            var existingSecrets = candidate.Select(item => item.Secret).ToHashSet(StringComparer.Ordinal);
            var usedIds = candidate.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
            var imported = 0;
            var skipped = 0;
            string? firstImportedId = null;
            foreach (var account in incoming)
            {
                if (string.IsNullOrWhiteSpace(account.Secret) || !existingSecrets.Add(account.Secret))
                {
                    skipped++;
                    continue;
                }

                var importedAccount = account with
                {
                    Id = CreateUniqueId(account.Id, usedIds),
                    CreatedAt = account.CreatedAt == 0 ? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() : account.CreatedAt,
                };
                usedIds.Add(importedAccount.Id);
                candidate.Add(importedAccount);
                firstImportedId ??= importedAccount.Id;
                imported++;
            }

            if (imported > 0)
            {
                await CommitAsync(candidate, operationEpoch, cancellationToken, firstImportedId);
            }

            return (imported, skipped);
        }, cancellationToken);
    }

    public void UpdateCodes()
    {
        AccountItemViewModel[] accounts;
        lock (_stateGate)
        {
            accounts = VisibleAccounts.ToArray();
        }

        foreach (var account in accounts)
        {
            account.UpdateCode();
        }
    }

    public IReadOnlyList<MfaAccount> Snapshot() => AccountsSnapshot;

    public void Unload()
    {
        Interlocked.Increment(ref _lifecycleEpoch);
        lock (_stateGate)
        {
            _accounts.Clear();
            _accountItemCache.Clear();
            VisibleAccounts.Clear();
            Groups.Clear();
            if (_selectedGroup is not null)
            {
                _selectedGroup = null;
                OnPropertyChanged(nameof(SelectedGroup));
            }

            if (_pendingRevealAccountId is not null)
            {
                _pendingRevealAccountId = null;
                OnPropertyChanged(nameof(PendingRevealAccountId));
            }

            IsLoaded = false;
            OnPropertyChanged(nameof(HasAccounts));
            OnPropertyChanged(nameof(HasVisibleAccounts));
        }
    }

    private async Task CommitAsync(
        IReadOnlyCollection<MfaAccount> candidate,
        long operationEpoch,
        CancellationToken cancellationToken,
        string? pendingRevealAccountId = null)
    {
        lock (_stateGate)
        {
            ThrowIfLifecycleInvalid(operationEpoch);
        }

        await _repository.SaveAsync(candidate, cancellationToken);
        lock (_stateGate)
        {
            ThrowIfLifecycleInvalid(operationEpoch);
            _accounts.Clear();
            _accounts.AddRange(candidate);
            if (pendingRevealAccountId is not null)
            {
                if (_searchText.Length != 0)
                {
                    _searchText = string.Empty;
                    OnPropertyChanged(nameof(SearchText));
                }

                if (_selectedGroup is not null)
                {
                    _selectedGroup = null;
                    OnPropertyChanged(nameof(SelectedGroup));
                }

            }

            RefreshDerivedState();
            if (pendingRevealAccountId is not null && _pendingRevealAccountId != pendingRevealAccountId)
            {
                _pendingRevealAccountId = pendingRevealAccountId;
                OnPropertyChanged(nameof(PendingRevealAccountId));
            }
        }
    }

    public bool ConsumePendingReveal(string expectedAccountId)
    {
        lock (_stateGate)
        {
            if (string.IsNullOrWhiteSpace(expectedAccountId)
                || !string.Equals(_pendingRevealAccountId, expectedAccountId, StringComparison.Ordinal))
            {
                return false;
            }

            _pendingRevealAccountId = null;
            OnPropertyChanged(nameof(PendingRevealAccountId));
            return true;
        }
    }

    private async Task ExecuteWriteAsync(Func<Task> write, CancellationToken cancellationToken)
    {
        await _writeGate.WaitAsync(cancellationToken);
        try
        {
            await write();
        }
        finally
        {
            _writeGate.Release();
        }
    }

    private async Task<T> ExecuteWriteAsync<T>(Func<Task<T>> write, CancellationToken cancellationToken)
    {
        await _writeGate.WaitAsync(cancellationToken);
        try
        {
            return await write();
        }
        finally
        {
            _writeGate.Release();
        }
    }

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

        var liveIds = _accounts.Select(account => account.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var staleId in _accountItemCache.Keys.Where(id => !liveIds.Contains(id)).ToArray())
        {
            _accountItemCache.Remove(staleId);
        }

        var visibleItems = new List<AccountItemViewModel>();
        foreach (var account in AccountQuery.FilterAndSort(_accounts, SearchText, SelectedGroup))
        {
            if (_accountItemCache.TryGetValue(account.Id, out var item))
            {
                item.ReplaceModel(account);
            }
            else
            {
                item = new AccountItemViewModel(account);
                _accountItemCache.Add(account.Id, item);
            }

            visibleItems.Add(item);
        }

        VisibleAccounts.Clear();
        foreach (var item in visibleItems)
        {
            VisibleAccounts.Add(item);
        }

        Groups.Clear();
        foreach (var group in groupCounts)
        {
            Groups.Add(new GroupFilterItemViewModel(group.Value, group.Label, group.Count));
        }

        OnPropertyChanged(nameof(HasAccounts));
        OnPropertyChanged(nameof(HasVisibleAccounts));
    }

    private void EnsureWritesAllowed(long operationEpoch)
    {
        ThrowIfLifecycleInvalid(operationEpoch);
        if (_hasLoadFailure)
        {
            throw new InvalidOperationException("本地账户数据加载失败，已禁止写入以避免覆盖数据。");
        }

        if (!_isLoaded)
        {
            throw CreateLifecycleCanceledException();
        }
    }

    private void ThrowIfLifecycleInvalid(long operationEpoch)
    {
        if (Volatile.Read(ref _lifecycleEpoch) != operationEpoch)
        {
            throw CreateLifecycleCanceledException();
        }
    }

    private static OperationCanceledException CreateLifecycleCanceledException() =>
        new("账户操作已因应用锁定而取消。");

    private static string? NormalizeGroup(string? group) =>
        string.IsNullOrWhiteSpace(group) ? null : group.Trim();

    private static string CreateUniqueId(string? preferredId, IEnumerable<string> existingIds)
    {
        var ids = existingIds as ISet<string> ?? existingIds.ToHashSet(StringComparer.Ordinal);
        if (!string.IsNullOrWhiteSpace(preferredId) && !ids.Contains(preferredId))
        {
            return preferredId;
        }

        string id;
        do
        {
            id = Guid.NewGuid().ToString("N");
        }
        while (ids.Contains(id));

        return id;
    }

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
