using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Windows.Security.Credentials.UI;
using WinRT.Interop;

namespace LiubaiMfa.Windows.Native;
public sealed class WindowsAuthenticationException : Exception
{
    public WindowsAuthenticationException() : base("系统身份验证暂不可用，请稍后重试。") { }
}
public interface IWindowsAuthentication { Task<bool> VerifyAsync(CancellationToken cancellationToken); }
public sealed class WindowsAuthentication(nint window) : IWindowsAuthentication
{
    private int pending;
    public async Task<bool> VerifyAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.CompareExchange(ref pending,1,0)!=0)return false;
        try { return await VerifyCoreAsync(cancellationToken); }
        catch (OperationCanceledException) { throw; }
        catch { throw new WindowsAuthenticationException(); }
        finally { Volatile.Write(ref pending,0); }
    }
    private async Task<bool> VerifyCoreAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!TrustedWindow.IsParentWindow(window))return false;
        var available = await UserConsentVerifier.CheckAvailabilityAsync();
        cancellationToken.ThrowIfCancellationRequested();
        if (available == UserConsentVerifierAvailability.Available) {
            var result = await UserConsentVerifierInterop.RequestVerificationForWindowAsync(window, "验证你的身份以访问留白 MFA");
            cancellationToken.ThrowIfCancellationRequested();
            // 用户取消或验证失败不自动换用密码，以免连续弹窗。
            return result == UserConsentVerificationResult.Verified;
        }
        return await Task.Run(() => VerifyPassword(cancellationToken), cancellationToken);
    }
    private bool VerifyPassword(CancellationToken cancellationToken)
    {
        var info = new CredUiInfo { Size = Marshal.SizeOf<CredUiInfo>(), Parent = window, Caption = "留白 MFA", Message = "请验证当前 Windows 用户的电脑密码" };
        uint package=0; bool save=false;
        nint packed=0; uint packedSize=0;
        nint user=0, domain=0, password=0; int passwordBytes=0;
        try {
            var status=CredUIPromptForWindowsCredentials(ref info,0,ref package,0,0,out packed,out packedSize,ref save,0);
            if(status!=0 || cancellationToken.IsCancellationRequested)return false;
            uint userSize=0, domainSize=0, passwordSize=0;
            CredUnPackAuthenticationBuffer(0,packed,packedSize,0,ref userSize,0,ref domainSize,0,ref passwordSize);
            if(userSize is 0 or > 1024 || domainSize>1024 || passwordSize is 0 or > 1024)return false;
            user=Marshal.AllocHGlobal(checked((int)userSize*2)); domain=Marshal.AllocHGlobal(checked((int)Math.Max(domainSize,1)*2)); passwordBytes=checked((int)passwordSize*2); password=Marshal.AllocHGlobal(passwordBytes);
            if(!CredUnPackAuthenticationBuffer(0,packed,packedSize,user,ref userSize,domain,ref domainSize,password,ref passwordSize))return false;
            if(!LogonUser(user,domain,password,2,0,out var token))return false;
            using(token) using(var identity=new WindowsIdentity(token.DangerousGetHandle())) using(var current=WindowsIdentity.GetCurrent())
                return !cancellationToken.IsCancellationRequested && identity.User is not null && current.User is not null && identity.User.Equals(current.User);
        } finally {
            // 凭据仅存于本地原生内存，不创建托管密码字符串。
            if(password!=0){Zero(password,passwordBytes);Marshal.FreeHGlobal(password);}
            if(user!=0)Marshal.FreeHGlobal(user);
            if(domain!=0)Marshal.FreeHGlobal(domain);
            if(packed!=0){Zero(packed,checked((int)packedSize));Marshal.FreeCoTaskMem(packed);}
        }
    }
    private static void Zero(nint ptr,int size) { for(var i=0;i<size;i++)Marshal.WriteByte(ptr,i,0); }
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] private struct CredUiInfo {public int Size;public nint Parent; public string Message;public string Caption;public nint Banner;}
    [DllImport("credui.dll",CharSet=CharSet.Unicode)] private static extern uint CredUIPromptForWindowsCredentials(ref CredUiInfo info,uint error,ref uint package,nint input,uint inputSize,out nint output,out uint outputSize,ref bool save,uint flags);
    [DllImport("credui.dll",CharSet=CharSet.Unicode,SetLastError=true)] [return:MarshalAs(UnmanagedType.Bool)] private static extern bool CredUnPackAuthenticationBuffer(uint flags,nint buffer,uint size,nint user,ref uint userSize,nint domain,ref uint domainSize,nint password,ref uint passwordSize);
    [DllImport("advapi32.dll",EntryPoint="LogonUserW",SetLastError=true)] [return:MarshalAs(UnmanagedType.Bool)] private static extern bool LogonUser(nint user,nint domain,nint password,int type,int provider,out Microsoft.Win32.SafeHandles.SafeAccessTokenHandle token);
}
public static class TrustedWindow
{
    public static bool IsParentWindow(nint window)
    {
        if(window==0 || !IsWindow(window))return false;
        GetWindowThreadProcessId(window,out var owner);
        var basic=new ProcessBasicInformation();
        if(NtQueryInformationProcess(Process.GetCurrentProcess().Handle,0,ref basic,Marshal.SizeOf<ProcessBasicInformation>(),out _)!=0)return false;
        return owner!=0 && (long)owner==basic.Parent.ToInt64();
    }
    [StructLayout(LayoutKind.Sequential)] private struct ProcessBasicInformation {public nint Reserved1,Peb,Reserved2,Reserved3,Id,Parent;}
    [DllImport("ntdll.dll")] private static extern int NtQueryInformationProcess(nint process,int kind,ref ProcessBasicInformation info,int length,out int returned);
    [DllImport("user32.dll")] [return:MarshalAs(UnmanagedType.Bool)] private static extern bool IsWindow(nint window);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window,out uint processId);
}
