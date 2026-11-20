using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace LiubaiMfa.Windows.Native;

// 消息专用窗口拥有剪贴板，所有 Win32 调用在同一原生工作线程串行执行。
public sealed class CodeClipboard : IDisposable, ICodeClipboardBackend
{
    private const uint UnicodeText = 13;
    private readonly BlockingCollection<Action> jobs = new();
    private readonly Thread worker;
    private readonly ManualResetEventSlim ready = new();
    private ClipboardSession? session;
    private IntPtr owner;
    private bool disposed;

    public CodeClipboard()
    {
        worker = new Thread(Run) { IsBackground = true, Name = "MFA 剪贴板" };
        worker.Start(); ready.Wait();
    }
    private void Run()
    {
        if (OperatingSystem.IsWindows()) owner = CreateWindowEx(0, "STATIC", "", 0, 0, 0, 0, 0, new IntPtr(-3), IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        session = new ClipboardSession(this, callback => new Timer(_ => {
            try { jobs.Add(callback); }
            catch (InvalidOperationException) { /* 宿主正常退出已关闭队列。 */ }
        }, null, TimeSpan.FromSeconds(30), Timeout.InfiniteTimeSpan));
        ready.Set();
        while (!jobs.IsCompleted)
        {
            if (jobs.TryTake(out var action, 25)) action();
            if (owner != IntPtr.Zero)
                while (PeekMessage(out var message, IntPtr.Zero, 0, 0, 1)) { TranslateMessage(ref message); DispatchMessage(ref message); }
        }
        if (owner != IntPtr.Zero) DestroyWindow(owner);
    }
    public bool Copy(string code)
    {
        if (disposed || code.Length != 6 || !code.All(char.IsAsciiDigit)) return false;
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        jobs.Add(() => { try { completion.SetResult(session!.Copy(code)); } catch { completion.SetResult(false); } });
        return completion.Task.GetAwaiter().GetResult();
    }
    bool ICodeClipboardBackend.Write(string code)
    {
        if (owner == IntPtr.Zero || !OpenClipboard(owner)) return false;
        IntPtr memory = IntPtr.Zero;
        try
        {
            memory = GlobalAlloc(0x0042, (UIntPtr)((code.Length + 1) * 2));
            if (memory == IntPtr.Zero) return false;
            var pointer = GlobalLock(memory);
            if (pointer == IntPtr.Zero) return false;
            try { Marshal.Copy((code + '\0').ToCharArray(), 0, pointer, code.Length + 1); }
            finally { GlobalUnlock(memory); }
            if (!EmptyClipboard() || SetClipboardData(UnicodeText, memory) == IntPtr.Zero) return false;
            memory = IntPtr.Zero;
        }
        finally { if (memory != IntPtr.Zero) GlobalFree(memory); CloseClipboard(); }
        return true;
    }
    void ICodeClipboardBackend.ClearIfOwned()
    {
        if (owner == IntPtr.Zero || !OpenClipboard(owner)) return;
        try
        {
            // 专属 HWND 所有权不受 Windows 格式补全影响，其他窗口同文本复制也会换所有者。
            if (GetClipboardOwner() == owner) EmptyClipboard();
        }
        finally { CloseClipboard(); }
    }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        jobs.Add(() => session?.Dispose());
        jobs.CompleteAdding(); worker.Join(1500); ready.Dispose();
    }
    [StructLayout(LayoutKind.Sequential)] private struct Point { public int X; public int Y; }
    [StructLayout(LayoutKind.Sequential)] private struct Message { public IntPtr Window; public uint Id; public UIntPtr WParam; public IntPtr LParam; public uint Time; public Point Position; public uint Private; }
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr CreateWindowEx(uint exStyle, string className, string name, uint style, int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr parameter);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DestroyWindow(IntPtr window);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool PeekMessage(out Message message, IntPtr window, uint minimum, uint maximum, uint remove);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool TranslateMessage(ref Message message);
    [DllImport("user32.dll")] private static extern IntPtr DispatchMessage(ref Message message);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool OpenClipboard(IntPtr window);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CloseClipboard();
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool EmptyClipboard();
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetClipboardData(uint format, IntPtr data);
    [DllImport("user32.dll")] private static extern IntPtr GetClipboardOwner();
    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr GlobalAlloc(uint flags, UIntPtr bytes);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr GlobalLock(IntPtr memory);
    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GlobalUnlock(IntPtr memory);
    [DllImport("kernel32.dll")] private static extern IntPtr GlobalFree(IntPtr memory);
}
