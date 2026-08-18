namespace LiubaiMfa.Windows.Services;

public sealed class DeferredNoticeGate
{
    private int _pending;

    public void MarkPending() => Interlocked.Exchange(ref _pending, 1);

    public bool TryConsume() => Interlocked.Exchange(ref _pending, 0) == 1;
}
