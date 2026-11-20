namespace LiubaiMfa.Windows.Native;

public interface ICodeClipboardBackend
{
    bool Write(string code);
    // 实现必须在同一次剪贴板锁内校验所有权并清理。
    void ClearIfOwned();
}

// 只在原生线程调用；可控后端和调度器使所有权/迟到回调测试不触碰用户剪贴板。
public sealed class ClipboardSession(ICodeClipboardBackend backend, Func<Action, IDisposable> schedule) : IDisposable
{
    private long generation;
    private IDisposable? pending;
    private bool disposed;
    public bool Copy(string code)
    {
        if (disposed || !backend.Write(code)) return false;
        var ticket = ++generation;
        pending?.Dispose();
        pending = schedule(() => { if (!disposed && ticket == generation) backend.ClearIfOwned(); });
        return true;
    }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true; ++generation; pending?.Dispose(); backend.ClearIfOwned();
    }
}
