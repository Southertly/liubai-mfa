namespace LiubaiMfa.Windows.ViewModels;

internal enum AutoLockDecisionKind
{
    None,
    Schedule,
    LockNow,
}

internal readonly record struct AutoLockDecision(AutoLockDecisionKind Kind, TimeSpan RemainingDelay)
{
    internal static AutoLockDecision None => new(AutoLockDecisionKind.None, TimeSpan.Zero);

    internal static AutoLockDecision Schedule(TimeSpan remainingDelay) =>
        new(AutoLockDecisionKind.Schedule, remainingDelay);

    internal static AutoLockDecision LockNow => new(AutoLockDecisionKind.LockNow, TimeSpan.Zero);
}

internal sealed class AutoLockActivationState
{
    private DateTimeOffset? _deactivatedAt;

    internal void RecordDeactivated(bool enabled, DateTimeOffset now)
    {
        if (enabled)
        {
            _deactivatedAt ??= now;
        }
    }

    internal void Clear() => _deactivatedAt = null;

    internal AutoLockDecision Evaluate(
        bool enabled,
        bool isUnlocked,
        DateTimeOffset now,
        TimeSpan delay)
    {
        if (_deactivatedAt is not { } deactivatedAt ||
            !StartupProtectionPolicy.ShouldScheduleAutoLock(enabled, isUnlocked))
        {
            return AutoLockDecision.None;
        }

        var elapsed = now - deactivatedAt;
        if (elapsed < TimeSpan.Zero)
        {
            elapsed = TimeSpan.Zero;
        }

        if (StartupProtectionPolicy.ShouldLockAfterDelay(enabled, isUnlocked, elapsed, delay))
        {
            return AutoLockDecision.LockNow;
        }

        var remainingDelay = delay - elapsed;
        if (remainingDelay > delay)
        {
            remainingDelay = delay;
        }

        return AutoLockDecision.Schedule(remainingDelay);
    }

    internal AutoLockDecision Activate(
        bool enabled,
        bool isUnlocked,
        DateTimeOffset now,
        TimeSpan delay)
    {
        var decision = Evaluate(enabled, isUnlocked, now, delay);
        _deactivatedAt = null;
        return decision.Kind == AutoLockDecisionKind.LockNow
            ? decision
            : AutoLockDecision.None;
    }
}
