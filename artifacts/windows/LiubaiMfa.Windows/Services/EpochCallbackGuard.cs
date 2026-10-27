namespace LiubaiMfa.Windows.Services;

public static class EpochCallbackGuard
{
    public static bool IsCurrent(
        long currentEpoch,
        long capturedEpoch,
        long currentAttachEpoch,
        long capturedAttachEpoch,
        string? currentAccountId,
        string? capturedAccountId) =>
        currentEpoch == capturedEpoch
        && currentAttachEpoch == capturedAttachEpoch
        && string.Equals(currentAccountId, capturedAccountId, StringComparison.Ordinal);
}
