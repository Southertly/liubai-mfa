using LiubaiMfa.Windows.Native;
namespace LiubaiMfa.CoreHost;
internal sealed class ProtectionSession
{
    public object Gate { get; } = new();
    private readonly AccountStore store;
    private readonly IWindowsAuthentication authentication;
    private readonly TimeSpan authenticationTimeout;
    private CancellationTokenSource? cancellation;
    public bool Enabled { get; private set; }
    public bool Locked { get; private set; }
    public bool Pending => cancellation is not null;
    public long Generation { get; private set; }
    public event Action? Invalidated;
    public void ApplyMigrationProtection() { lock(Gate) { Enabled=true; Locked=false; Generation++; Invalidated?.Invoke(); } }
    public ProtectionSession(AccountStore store, IWindowsAuthentication authentication, TimeSpan? authenticationTimeout = null)
    { this.store = store; this.authentication = authentication; this.authenticationTimeout = authenticationTimeout ?? TimeSpan.FromSeconds(120); Enabled = store.ReadProtection(); Locked = Enabled; }
    public void Lock() { lock(Gate) { Generation++; Locked = true; Invalidated?.Invoke(); cancellation?.Cancel(); } }
    public async Task<bool> VerifyAsync(bool? enabled)
    {
        long ticket;
        using var source = new CancellationTokenSource(authenticationTimeout);
        lock(Gate) { if(Pending)return false; ticket=Generation; cancellation=source; }
        try {
            var success = await authentication.VerifyAsync(source.Token).WaitAsync(source.Token);
            lock(Gate) {
                if (!success || source.IsCancellationRequested || ticket != Generation) return false;
                if (enabled.HasValue) {
                    try {store.WriteProtection(enabled.Value);} catch {Locked=true;Generation++;throw;}
                    Enabled = enabled.Value;
                }
                Locked = false; Generation++; return true;
            }
        }
        catch (OperationCanceledException) { return false; }
        finally { lock(Gate) { if (ReferenceEquals(cancellation,source)) cancellation = null; } }
    }
}
