using Microsoft.UI.Xaml.Controls;

namespace LiubaiMfa.Windows.Services;

public readonly record struct ClipboardStatusFeedback(string Message, InfoBarSeverity Severity);

public readonly record struct RecoveryKeyCopyFeedback(string ButtonText, bool CanContinue);

public static class ClipboardResultPolicy
{
    public static ClipboardStatusFeedback CreateStatus(bool copied, string successMessage)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(successMessage);
        return copied
            ? new ClipboardStatusFeedback(successMessage, InfoBarSeverity.Success)
            : new ClipboardStatusFeedback("剪贴板暂时不可用，请稍后重试。", InfoBarSeverity.Warning);
    }

    public static RecoveryKeyCopyFeedback CreateRecoveryKey(bool copied) => copied
        ? new RecoveryKeyCopyFeedback("恢复密钥已复制", true)
        : new RecoveryKeyCopyFeedback("复制失败，请重试", false);
}
