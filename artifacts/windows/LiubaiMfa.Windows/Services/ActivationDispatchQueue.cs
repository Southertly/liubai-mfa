namespace LiubaiMfa.Windows.Services;

internal sealed class ActivationDispatchQueue<T>
{
    private readonly object _gate = new();
    private readonly Queue<T> _items = new();
    private bool _drainReserved;
    private bool _isDraining;
    private bool _stopping;

    internal bool TryEnqueue(T item)
    {
        lock (_gate)
        {
            if (_stopping)
            {
                return false;
            }

            _items.Enqueue(item);
            return true;
        }
    }

    internal bool TryReserveDrain()
    {
        lock (_gate)
        {
            if (_stopping || _items.Count == 0 || _drainReserved || _isDraining)
            {
                return false;
            }

            _drainReserved = true;
            return true;
        }
    }

    internal void ReleaseDrainReservation()
    {
        lock (_gate)
        {
            if (!_stopping && !_isDraining)
            {
                _drainReserved = false;
            }
        }
    }

    internal bool TryBeginDrain()
    {
        lock (_gate)
        {
            if (_stopping || !_drainReserved || _isDraining)
            {
                return false;
            }

            _drainReserved = false;
            _isDraining = true;
            return true;
        }
    }

    internal bool TryTake(out T item)
    {
        lock (_gate)
        {
            if (_stopping || !_isDraining || _items.Count == 0)
            {
                item = default!;
                return false;
            }

            item = _items.Dequeue();
            return true;
        }
    }

    internal bool CompleteDrain()
    {
        lock (_gate)
        {
            if (!_isDraining)
            {
                return false;
            }

            _isDraining = false;
            if (_stopping || _items.Count == 0)
            {
                return false;
            }

            _drainReserved = true;
            return true;
        }
    }

    internal void Stop()
    {
        lock (_gate)
        {
            _stopping = true;
            _drainReserved = false;
            _items.Clear();
        }
    }
}
