namespace LiubaiMfa.Windows.Services;

internal enum SnippingCaptureDecisionKind
{
    Ignore,
    RedeemNow,
    DeferUntilUnlock,
    Cancelled,
    BadRequest,
    Timeout,
    Failed,
}

internal readonly record struct SnippingCaptureDecision(
    SnippingCaptureDecisionKind Kind,
    string? FileAccessToken = null);

internal sealed class SnippingCaptureState
{
    private readonly object _gate = new();
    private Guid? _activeCorrelationId;
    private SnippingCaptureDecision? _pendingDecision;

    public Guid Begin(Guid correlationId)
    {
        lock (_gate)
        {
            if (correlationId == Guid.Empty)
            {
                throw new ArgumentException("截图请求关联 ID 不能为空。", nameof(correlationId));
            }

            _activeCorrelationId = correlationId;
            _pendingDecision = null;
            return correlationId;
        }
    }

    public SnippingCaptureDecision Receive(
        Guid correlationId,
        int statusCode,
        string? fileAccessToken,
        bool sessionCurrent)
    {
        lock (_gate)
        {
            if (_activeCorrelationId != correlationId)
            {
                return new SnippingCaptureDecision(SnippingCaptureDecisionKind.Ignore);
            }

            _activeCorrelationId = null;
            var decision = statusCode switch
            {
                200 => ReceiveSuccess(fileAccessToken),
                400 => new SnippingCaptureDecision(SnippingCaptureDecisionKind.BadRequest),
                408 => new SnippingCaptureDecision(SnippingCaptureDecisionKind.Timeout),
                499 => new SnippingCaptureDecision(SnippingCaptureDecisionKind.Cancelled),
                _ => new SnippingCaptureDecision(SnippingCaptureDecisionKind.Failed),
            };

            if (sessionCurrent)
            {
                return decision;
            }

            _pendingDecision = decision;
            return new SnippingCaptureDecision(SnippingCaptureDecisionKind.DeferUntilUnlock);
        }
    }

    public SnippingCaptureDecision ResumeAfterUnlock(bool sessionCurrent)
    {
        lock (_gate)
        {
            if (_pendingDecision is not SnippingCaptureDecision pendingDecision)
            {
                return new SnippingCaptureDecision(SnippingCaptureDecisionKind.Ignore);
            }

            if (!sessionCurrent)
            {
                return new SnippingCaptureDecision(SnippingCaptureDecisionKind.DeferUntilUnlock);
            }

            _pendingDecision = null;
            return pendingDecision;
        }
    }

    public bool Abandon(Guid correlationId)
    {
        if (correlationId == Guid.Empty)
        {
            throw new ArgumentException("截图请求关联 ID 不能为空。", nameof(correlationId));
        }

        lock (_gate)
        {
            if (_activeCorrelationId != correlationId)
            {
                return false;
            }

            _activeCorrelationId = null;
            _pendingDecision = null;
            return true;
        }
    }

    public void Abandon()
    {
        lock (_gate)
        {
            _activeCorrelationId = null;
            _pendingDecision = null;
        }
    }

    private static SnippingCaptureDecision ReceiveSuccess(string? fileAccessToken)
    {
        if (string.IsNullOrWhiteSpace(fileAccessToken))
        {
            return new SnippingCaptureDecision(SnippingCaptureDecisionKind.Failed);
        }

        return new SnippingCaptureDecision(SnippingCaptureDecisionKind.RedeemNow, fileAccessToken);
    }
}
