namespace LiubaiMfa.Windows.Services;

public sealed class SessionCancellationGuard : IDisposable
{
    private readonly object _gate = new();
    private SessionState? _activeState;
    private int _liveStateCount;
    private bool _isDisposed;

    internal int LiveStateCount => Volatile.Read(ref _liveStateCount);

    public void Begin(long sessionToken)
    {
        if (sessionToken == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sessionToken));
        }

        SessionState? replacedState;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_isDisposed, this);
            replacedState = DetachActiveUnsafe();
            _activeState = new SessionState(sessionToken);
            Interlocked.Increment(ref _liveStateCount);
        }

        DeliverCancellation(replacedState);
    }

    public void Invalidate()
    {
        SessionState? invalidatedState;
        lock (_gate)
        {
            if (_isDisposed)
            {
                return;
            }

            invalidatedState = DetachActiveUnsafe();
        }

        DeliverCancellation(invalidatedState);
    }

    public bool TryAcquire(long sessionToken, out SessionCancellationLease lease)
    {
        lock (_gate)
        {
            if (!_isDisposed
                && _activeState is { IsActive: true, IsDisposed: false } state
                && state.SessionToken == sessionToken)
            {
                state.LeaseCount++;
                lease = new SessionCancellationLease(this, state);
                return true;
            }

            lease = null!;
            return false;
        }
    }

    public void Dispose()
    {
        SessionState? invalidatedState;
        lock (_gate)
        {
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;
            invalidatedState = DetachActiveUnsafe();
        }

        DeliverCancellation(invalidatedState);
    }

    private SessionState? DetachActiveUnsafe()
    {
        var state = _activeState;
        _activeState = null;
        if (state is null)
        {
            return null;
        }

        state.IsActive = false;
        return state;
    }

    private void DeliverCancellation(SessionState? state)
    {
        if (state is null)
        {
            return;
        }

        try
        {
            state.Cancellation.Cancel();
        }
        catch (Exception)
        {
            // 取消回调不得阻止锁定流程或资源清理。
        }
        finally
        {
            lock (_gate)
            {
                state.CancellationDelivered = true;
                TryDisposeStateUnsafe(state);
            }
        }
    }

    private void Release(SessionState state)
    {
        lock (_gate)
        {
            if (state.LeaseCount > 0)
            {
                state.LeaseCount--;
            }

            TryDisposeStateUnsafe(state);
        }
    }

    private void TryDisposeStateUnsafe(SessionState state)
    {
        if (state.IsActive || !state.CancellationDelivered || state.LeaseCount != 0 || state.IsDisposed)
        {
            return;
        }

        state.IsDisposed = true;
        state.Cancellation.Dispose();
        Interlocked.Decrement(ref _liveStateCount);
    }

    internal sealed class SessionState
    {
        public SessionState(long sessionToken)
        {
            SessionToken = sessionToken;
        }

        public long SessionToken { get; }
        public CancellationTokenSource Cancellation { get; } = new();
        public int LeaseCount { get; set; }
        public bool IsActive { get; set; } = true;
        public bool CancellationDelivered { get; set; }
        public bool IsDisposed { get; set; }
    }

    public sealed class SessionCancellationLease : IDisposable
    {
        private SessionCancellationGuard? _owner;
        private SessionState? _state;

        internal SessionCancellationLease(SessionCancellationGuard owner, SessionState state)
        {
            _owner = owner;
            _state = state;
        }

        public CancellationToken Token => _state?.Cancellation.Token ?? default;

        public void Dispose()
        {
            var owner = Interlocked.Exchange(ref _owner, null);
            var state = Interlocked.Exchange(ref _state, null);
            if (owner is not null && state is not null)
            {
                owner.Release(state);
            }
        }
    }
}
