namespace LiubaiMfa.Windows.Services;

public enum BackupWriteResult
{
    NotReplaced,
    Replaced,
    ReplacedAfterSessionInvalid,
}

public sealed class BackupWriteCoordinator
{
    public async Task<BackupWriteResult> WriteAndReplaceAsync(
        Func<CancellationToken, Task> writeTemporaryAsync,
        Func<Task> replaceAsync,
        Func<bool> isSessionCurrent,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(writeTemporaryAsync);
        ArgumentNullException.ThrowIfNull(replaceAsync);
        ArgumentNullException.ThrowIfNull(isSessionCurrent);

        if (cancellationToken.IsCancellationRequested || !isSessionCurrent())
        {
            return BackupWriteResult.NotReplaced;
        }

        await writeTemporaryAsync(cancellationToken);
        if (cancellationToken.IsCancellationRequested || !isSessionCurrent())
        {
            return BackupWriteResult.NotReplaced;
        }

        await replaceAsync();
        return cancellationToken.IsCancellationRequested || !isSessionCurrent()
            ? BackupWriteResult.ReplacedAfterSessionInvalid
            : BackupWriteResult.Replaced;
    }
}
