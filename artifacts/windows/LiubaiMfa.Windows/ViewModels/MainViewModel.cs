using System.Collections.ObjectModel;
using LiubaiMfa.Core.Models;
using LiubaiMfa.Windows.Services;

namespace LiubaiMfa.Windows.ViewModels;

public sealed class MainViewModel
{
    private readonly EncryptedAccountRepository _repository;

    public MainViewModel(EncryptedAccountRepository repository)
    {
        _repository = repository;
    }

    public ObservableCollection<AccountItemViewModel> Accounts { get; } = new();
    public bool IsLoaded { get; private set; }

    public async Task LoadAsync()
    {
        if (IsLoaded)
        {
            return;
        }

        var accounts = await _repository.LoadAsync();
        Accounts.Clear();
        foreach (var account in accounts.OrderBy(item => item.Issuer).ThenBy(item => item.Account))
        {
            Accounts.Add(new AccountItemViewModel(account));
        }

        IsLoaded = true;
    }

    public async Task AddAsync(MfaAccount account)
    {
        if (Accounts.Any(item => item.Model.Secret == account.Secret))
        {
            throw new InvalidOperationException("该 MFA 密钥已经存在。");
        }

        Accounts.Add(new AccountItemViewModel(account));
        SortAccounts();
        await SaveAsync();
    }

    public async Task UpdateAsync(MfaAccount account)
    {
        var existing = Accounts.FirstOrDefault(item => item.Id == account.Id)
            ?? throw new InvalidOperationException("账户不存在。");
        existing.ReplaceModel(account);
        SortAccounts();
        await SaveAsync();
    }

    public async Task DeleteAsync(string id)
    {
        var existing = Accounts.FirstOrDefault(item => item.Id == id);
        if (existing is null)
        {
            return;
        }

        Accounts.Remove(existing);
        await SaveAsync();
    }

    public async Task<(int Imported, int Skipped)> ImportAsync(IEnumerable<MfaAccount> incoming)
    {
        var existingSecrets = Accounts.Select(item => item.Model.Secret).ToHashSet(StringComparer.Ordinal);
        var imported = 0;
        var skipped = 0;
        foreach (var account in incoming)
        {
            if (string.IsNullOrWhiteSpace(account.Secret) || !existingSecrets.Add(account.Secret))
            {
                skipped++;
                continue;
            }

            Accounts.Add(new AccountItemViewModel(account with
            {
                Id = string.IsNullOrWhiteSpace(account.Id) ? Guid.NewGuid().ToString("N") : account.Id,
                CreatedAt = account.CreatedAt == 0 ? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() : account.CreatedAt,
            }));
            imported++;
        }

        SortAccounts();
        await SaveAsync();
        return (imported, skipped);
    }

    public void UpdateCodes()
    {
        foreach (var account in Accounts)
        {
            account.UpdateCode();
        }
    }

    public IReadOnlyList<MfaAccount> Snapshot() => Accounts.Select(item => item.Model).ToList();

    public void Unload()
    {
        Accounts.Clear();
        IsLoaded = false;
    }

    private async Task SaveAsync() => await _repository.SaveAsync(Snapshot());

    private void SortAccounts()
    {
        var sorted = Accounts.OrderBy(item => item.Issuer).ThenBy(item => item.Account).ToList();
        for (var targetIndex = 0; targetIndex < sorted.Count; targetIndex++)
        {
            var currentIndex = Accounts.IndexOf(sorted[targetIndex]);
            if (currentIndex != targetIndex)
            {
                Accounts.Move(currentIndex, targetIndex);
            }
        }
    }
}
