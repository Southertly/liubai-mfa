namespace LiubaiMfa.Windows.Services;

public sealed class UnlockSessionGuard
{
    private long _issuedToken;
    private long _currentToken;

    public long BeginUnlockedSession()
    {
        long token;
        do
        {
            token = Interlocked.Increment(ref _issuedToken);
        }
        while (token == 0);

        Volatile.Write(ref _currentToken, token);
        return token;
    }

    public void Invalidate() => Interlocked.Exchange(ref _currentToken, 0);

    public bool IsCurrent(long token) =>
        token != 0 && Volatile.Read(ref _currentToken) == token;
}
