using LiubaiMfa.Core.Models;

namespace LiubaiMfa.Windows.ViewModels;

internal static class AccountQuery
{
    public static IReadOnlyList<MfaAccount> FilterAndSort(
        IEnumerable<MfaAccount> accounts,
        string? searchText,
        string? selectedGroup)
    {
        var normalizedSearch = searchText?.Trim();
        var normalizedGroup = NormalizeGroup(selectedGroup);

        return accounts
            .Select((account, index) => new { Account = account, Index = index })
            .Where(item => normalizedGroup is null || string.Equals(
                NormalizeGroup(item.Account.Group),
                normalizedGroup,
                StringComparison.CurrentCultureIgnoreCase))
            .Where(item => string.IsNullOrEmpty(normalizedSearch) || MatchesSearch(item.Account, normalizedSearch))
            .OrderBy(item => item.Account.Issuer, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(item => item.Account.Account, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(item => item.Index)
            .Select(item => item.Account)
            .ToArray();
    }

    public static IReadOnlyList<GroupCount> BuildGroupCounts(IEnumerable<MfaAccount> accounts)
    {
        var accountArray = accounts.ToArray();
        var groups = accountArray
            .Select(account => NormalizeGroup(account.Group))
            .Where(group => group is not null)
            .GroupBy(group => group!, StringComparer.CurrentCultureIgnoreCase)
            .Select(group => new GroupCount(group.Key, group.Key, group.Count()))
            .OrderBy(group => group.Label, StringComparer.CurrentCultureIgnoreCase);

        return new[] { new GroupCount(null, "全部", accountArray.Length) }
            .Concat(groups)
            .ToArray();
    }

    private static bool MatchesSearch(MfaAccount account, string searchText) =>
        account.Issuer.Contains(searchText, StringComparison.CurrentCultureIgnoreCase)
        || account.Account.Contains(searchText, StringComparison.CurrentCultureIgnoreCase)
        || (NormalizeGroup(account.Group)?.Contains(searchText, StringComparison.CurrentCultureIgnoreCase) ?? false);

    private static string? NormalizeGroup(string? group) =>
        string.IsNullOrWhiteSpace(group) ? null : group.Trim();
}

internal sealed record GroupCount(string? Value, string Label, int Count);
