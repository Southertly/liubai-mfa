using LiubaiMfa.Core.Models;

namespace LiubaiMfa.Windows.Services;

internal interface IAccountRepository
{
    Task<IReadOnlyList<MfaAccount>> LoadAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(IReadOnlyCollection<MfaAccount> accounts, CancellationToken cancellationToken = default);
}
