namespace LiubaiMfa.Windows.ViewModels;

public static class StartupProtectionPolicy
{
    public static bool ShouldStartLocked(bool enabled) => enabled;

    public static bool ShouldShowManualLock(bool enabled) => enabled;

    public static bool CanCommitSettingChange(
        bool current,
        bool requested,
        bool verificationSucceeded,
        bool sessionCurrent) =>
        requested != current && verificationSucceeded && sessionCurrent;

    public static bool ShouldScheduleAutoLock(bool enabled, bool isUnlocked) => enabled && isUnlocked;

    public static bool ShouldLockAfterDelay(
        bool enabled,
        bool isUnlocked,
        TimeSpan elapsed,
        TimeSpan delay) =>
        enabled && isUnlocked && elapsed >= delay;
}
