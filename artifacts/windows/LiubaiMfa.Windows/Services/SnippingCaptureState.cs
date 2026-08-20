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
    private Guid? _activeCorrelationId;
    private string? _pendingFileAccessToken;

    public Guid Begin(Guid correlationId)
    {
        if (correlationId == Guid.Empty)
        {
            throw new ArgumentException("截图请求关联 ID 不能为空。", nameof(correlationId));
        }

        _activeCorrelationId = correlationId;
        _pendingFileAccessToken = null;
        return correlationId;
    }

    public SnippingCaptureDecision Receive(
        Guid correlationId,
        int statusCode,
        string? fileAccessToken,
        bool sessionCurrent)
    {
        if (_activeCorrelationId != correlationId)
        {
            return new SnippingCaptureDecision(SnippingCaptureDecisionKind.Ignore);
        }

        _activeCorrelationId = null;

        return statusCode switch
        {
            200 => ReceiveSuccess(fileAccessToken, sessionCurrent),
            400 => new SnippingCaptureDecision(SnippingCaptureDecisionKind.BadRequest),
            408 => new SnippingCaptureDecision(SnippingCaptureDecisionKind.Timeout),
            499 => new SnippingCaptureDecision(SnippingCaptureDecisionKind.Cancelled),
            _ => new SnippingCaptureDecision(SnippingCaptureDecisionKind.Failed),
        };
    }

    public SnippingCaptureDecision ResumeAfterUnlock(bool sessionCurrent)
    {
        if (_pendingFileAccessToken is null)
        {
            return new SnippingCaptureDecision(SnippingCaptureDecisionKind.Ignore);
        }

        if (!sessionCurrent)
        {
            return new SnippingCaptureDecision(SnippingCaptureDecisionKind.DeferUntilUnlock);
        }

        var fileAccessToken = _pendingFileAccessToken;
        _pendingFileAccessToken = null;
        return new SnippingCaptureDecision(SnippingCaptureDecisionKind.RedeemNow, fileAccessToken);
    }

    public void Abandon()
    {
        _activeCorrelationId = null;
        _pendingFileAccessToken = null;
    }

    private SnippingCaptureDecision ReceiveSuccess(string? fileAccessToken, bool sessionCurrent)
    {
        if (string.IsNullOrWhiteSpace(fileAccessToken))
        {
            return new SnippingCaptureDecision(SnippingCaptureDecisionKind.Failed);
        }

        if (sessionCurrent)
        {
            return new SnippingCaptureDecision(SnippingCaptureDecisionKind.RedeemNow, fileAccessToken);
        }

        _pendingFileAccessToken = fileAccessToken;
        return new SnippingCaptureDecision(SnippingCaptureDecisionKind.DeferUntilUnlock);
    }
}
