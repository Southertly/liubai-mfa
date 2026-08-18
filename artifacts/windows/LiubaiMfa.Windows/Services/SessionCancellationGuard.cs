namespace LiubaiMfa.Windows.Services;

public sealed class SessionCancellationGuard : IDisposable
{
    private readonly object _gate = new();
    private readonly List<CancellationTokenSource> _ownedCancellations = [];
    private long _activeSession;
    private CancellationTokenSource? _activeCancellation;
    private bool _isDisposed;

    public void Begin(long sessionToken)
    {
        if (sessionToken == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sessionToken));
        }

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_isDisposed, this);
            CancelActiveUnsafe();
            _activeSession = sessionToken;
            _activeCancellation = new CancellationTokenSource();
            _ownedCancellations.Add(_activeCancellation);
        }
    }

    public void Invalidate()
    {
        lock (_gate)
        {
            if (_isDisposed)
            {
                return;
            }

            _activeSession = 0;
            CancelActiveUnsafe();
            _activeCancellation = null;
        }
    }

    public bool TryGetCancellationToken(long sessionToken, out CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (_isDisposed)
            {
                cancellationToken = default;
                return false;
            }

            if (sessionToken != 0
                && _activeSession == sessionToken
                && _activeCancellation is { IsCancellationRequested: false } cancellation)
            {
                cancellationToken = cancellation.Token;
                return true;
            }

            cancellationToken = default;
            return false;
        }
    }

    private void CancelActiveUnsafe()
    {
        if (_activeCancellation is { IsCancellationRequested: false } cancellation)
        {
            cancellation.Cancel();
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;
            _activeSession = 0;
            CancelActiveUnsafe();
            _activeCancellation = null;
            foreach (var cancellation in _ownedCancellations)
            {
                cancellation.Dispose();
            }

            _ownedCancellations.Clear();
        }
    }
}
