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

internal enum QrAccountSaveOutcome
{
    Imported,
    Conflict,
}

internal sealed class QrAccountImportCoordinator
{
    private readonly Func<StorageFile, Task<string?>> _decodeAsync;
    private readonly Func<long, AccountDraft, CancellationToken, Task<AccountDraft?>> _confirmAsync;
    private readonly Func<long, AccountDraft, CancellationToken, Task<QrAccountSaveOutcome>> _saveAsync;
    private readonly Func<long, bool> _isSessionCurrent;

    internal QrAccountImportCoordinator(
        Func<StorageFile, Task<string?>> decodeAsync,
        Func<long, AccountDraft, CancellationToken, Task<AccountDraft?>> confirmAsync,
        Func<long, AccountDraft, CancellationToken, Task<QrAccountSaveOutcome>> saveAsync,
        Func<long, bool> isSessionCurrent)
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

    public async Task<QrAccountImportOutcome> ImportAsync(
        StorageFile file,
        long sessionToken,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(file);
        if (sessionToken == 0)
        {
            throw new ArgumentException("解锁会话标识不能为空。", nameof(sessionToken));
        }

        if (!IsContextCurrent(sessionToken, cancellationToken))
        {
            return QrAccountImportOutcome.Cancelled;
        }

        string? decoded;
        try
        {
            decoded = await _decodeAsync(file);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return QrAccountImportOutcome.Cancelled;
        }

        if (!IsContextCurrent(sessionToken, cancellationToken))
        {
            return QrAccountImportOutcome.Cancelled;
        }

        if (string.IsNullOrWhiteSpace(decoded)
            || !OtpAuthUriParser.TryParse(decoded, out var parsed)
            || parsed is null)
        {
            return QrAccountImportOutcome.InvalidQr;
        }

        var draft = new AccountDraft(parsed.Issuer, parsed.Account, parsed.Secret, null);
        AccountDraft? confirmed;
        try
        {
            confirmed = await _confirmAsync(sessionToken, draft, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return QrAccountImportOutcome.Cancelled;
        }

        if (!IsContextCurrent(sessionToken, cancellationToken) || confirmed is null)
        {
            return QrAccountImportOutcome.Cancelled;
        }

        QrAccountSaveOutcome saveOutcome;
        try
        {
            saveOutcome = await _saveAsync(sessionToken, confirmed, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return QrAccountImportOutcome.Cancelled;
        }

        if (!IsContextCurrent(sessionToken, cancellationToken))
        {
            return QrAccountImportOutcome.Cancelled;
        }

        return saveOutcome switch
        {
            QrAccountSaveOutcome.Imported => QrAccountImportOutcome.Imported,
            QrAccountSaveOutcome.Conflict => QrAccountImportOutcome.Conflict,
            _ => throw new InvalidOperationException("二维码账户保存返回了未知结果。"),
        };
    }

    private bool IsContextCurrent(long sessionToken, CancellationToken cancellationToken) =>
        !cancellationToken.IsCancellationRequested && _isSessionCurrent(sessionToken);
}
