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
    private long _lifecycleEpoch;
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

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        var operationEpoch = Volatile.Read(ref _lifecycleEpoch);
        await _writeGate.WaitAsync(cancellationToken);
        try
        {
            lock (_stateGate)
            {
                if (!IsOperationCurrent(operationEpoch))
                {
                    return;
                }

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
                    if (!IsOperationCurrent(operationEpoch))
                    {
                        return;
                    }

                    _accounts.Clear();
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
                    if (IsOperationCurrent(operationEpoch))
                    {
                        _accounts.Clear();
                        IsLoaded = false;
                        HasLoadFailure = true;
                        RefreshDerivedState();
                    }
                }

                throw;
            }
        }
        finally
        {
            _writeGate.Release();
        }
    }

    public Task AddAsync(MfaAccount account, CancellationToken cancellationToken = default)
    {
        var operationEpoch = Volatile.Read(ref _lifecycleEpoch);
        return ExecuteWriteAsync(async () =>
        {
            MfaAccount[] candidate;
            lock (_stateGate)
            {
                if (!CanWrite(operationEpoch))
                {
                    return;
                }

                if (_accounts.Any(item => item.Secret == account.Secret))
                {
                    throw new InvalidOperationException("该 MFA 密钥已经存在。");
                }

                candidate = _accounts.Append(account).ToArray();
            }

            await CommitAsync(candidate, operationEpoch, cancellationToken);
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
                if (!CanWrite(operationEpoch))
                {
                    return;
                }

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
                if (!CanWrite(operationEpoch))
                {
                    return;
                }

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
                if (!CanWrite(operationEpoch))
                {
                    return (0, 0);
                }

                candidate = _accounts.ToList();
            }

            var existingSecrets = candidate.Select(item => item.Secret).ToHashSet(StringComparer.Ordinal);
            var imported = 0;
            var skipped = 0;
            foreach (var account in incoming)
            {
                if (string.IsNullOrWhiteSpace(account.Secret) || !existingSecrets.Add(account.Secret))
                {
                    skipped++;
                    continue;
                }

                candidate.Add(account with
                {
                    Id = string.IsNullOrWhiteSpace(account.Id) ? Guid.NewGuid().ToString("N") : account.Id,
                    CreatedAt = account.CreatedAt == 0 ? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() : account.CreatedAt,
                });
                imported++;
            }

            if (imported > 0)
            {
                if (!await CommitAsync(candidate, operationEpoch, cancellationToken))
                {
                    return (0, 0);
                }
            }
            else
            {
                lock (_stateGate)
                {
                    if (!IsOperationCurrent(operationEpoch))
                    {
                        return (0, 0);
                    }

                    RefreshDerivedState();
                }
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
            VisibleAccounts.Clear();
            Groups.Clear();
            if (_selectedGroup is not null)
            {
                _selectedGroup = null;
                OnPropertyChanged(nameof(SelectedGroup));
            }

            IsLoaded = false;
            OnPropertyChanged(nameof(HasAccounts));
            OnPropertyChanged(nameof(HasVisibleAccounts));
        }
    }

    private async Task<bool> CommitAsync(
        IReadOnlyCollection<MfaAccount> candidate,
        long operationEpoch,
        CancellationToken cancellationToken)
    {
        lock (_stateGate)
        {
            if (!IsOperationCurrent(operationEpoch))
            {
                return false;
            }
        }

        await _repository.SaveAsync(candidate, cancellationToken);
        lock (_stateGate)
        {
            if (!IsOperationCurrent(operationEpoch))
            {
                return false;
            }

            _accounts.Clear();
            _accounts.AddRange(candidate);
            RefreshDerivedState();
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

    private bool CanWrite(long operationEpoch)
    {
        if (!IsOperationCurrent(operationEpoch))
        {
            return false;
        }

        if (_hasLoadFailure)
        {
            throw new InvalidOperationException("本地账户数据加载失败，已禁止写入以避免覆盖数据。");
        }

        return _isLoaded;
    }

    private bool IsOperationCurrent(long operationEpoch) =>
        Volatile.Read(ref _lifecycleEpoch) == operationEpoch;

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
