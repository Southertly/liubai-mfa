using LiubaiMfa.Core;
using Windows.Storage;

namespace LiubaiMfa.Windows.Services;

internal enum QrAccountImportOutcome
{
    Imported,
    Cancelled,
    InvalidQr,
    Conflict,
}

internal sealed class QrAccountImportCoordinator
{
    private readonly Func<StorageFile, Task<string?>> _decodeAsync;
    private readonly Func<AccountDraft, Task<AccountDraft?>> _confirmAsync;
    private readonly Func<AccountDraft, Task> _saveAsync;
    private readonly Func<bool> _isSessionCurrent;

    public QrAccountImportCoordinator(
        Func<StorageFile, Task<string?>> decodeAsync,
        Func<AccountDraft, Task<AccountDraft?>> confirmAsync,
        Func<AccountDraft, Task> saveAsync,
        Func<bool> isSessionCurrent)
    {
        ArgumentNullException.ThrowIfNull(decodeAsync);
        ArgumentNullException.ThrowIfNull(confirmAsync);
        ArgumentNullException.ThrowIfNull(saveAsync);
        ArgumentNullException.ThrowIfNull(isSessionCurrent);

        _decodeAsync = decodeAsync;
        _confirmAsync = confirmAsync;
        _saveAsync = saveAsync;
        _isSessionCurrent = isSessionCurrent;
    }

    public async Task<QrAccountImportOutcome> ImportAsync(StorageFile file)
    {
        ArgumentNullException.ThrowIfNull(file);

        var decoded = await _decodeAsync(file);
        if (string.IsNullOrWhiteSpace(decoded)
            || !OtpAuthUriParser.TryParse(decoded, out var parsed)
            || parsed is null)
        {
            return QrAccountImportOutcome.InvalidQr;
        }

        if (!_isSessionCurrent())
        {
            return QrAccountImportOutcome.Cancelled;
        }

        var draft = new AccountDraft(parsed.Issuer, parsed.Account, parsed.Secret, null);
        var confirmed = await _confirmAsync(draft);
        if (!_isSessionCurrent() || confirmed is null)
        {
            return QrAccountImportOutcome.Cancelled;
        }

        try
        {
            await _saveAsync(confirmed);
            return QrAccountImportOutcome.Imported;
        }
        catch (InvalidOperationException)
        {
            return QrAccountImportOutcome.Conflict;
        }
    }
}
