using System.Runtime.InteropServices;
using Windows.Security.Credentials.UI;

namespace LiubaiMfa.Windows.Services;

/// <summary>
/// 使用 Windows 系统凭据对话框验证当前电脑账户密码。
/// 凭据不保存到应用设置、日志或账户数据中。
/// </summary>
public sealed class WindowsCredentialService
{
    private readonly Func<string, string?, string, bool> _verifyCredential;

    public WindowsCredentialService()
        : this(VerifyWindowsCredential)
    {
    }

    internal WindowsCredentialService(Func<string, string?, string, bool> verifyCredential)
    {
        ArgumentNullException.ThrowIfNull(verifyCredential);
        _verifyCredential = verifyCredential;
    }

    public async Task<bool> VerifyAsync(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        var options = new CredentialPickerOptions
        {
            Caption = "留白 MFA",
            Message = reason,
            TargetName = Environment.MachineName,
            AlwaysDisplayDialog = true,
            CallerSavesCredential = false,
            CredentialSaveOption = CredentialSaveOption.Hidden,
        };

        var result = await CredentialPicker.PickAsync(options);
        if (result.ErrorCode != 0 ||
            string.IsNullOrWhiteSpace(result.CredentialUserName) ||
            string.IsNullOrEmpty(result.CredentialPassword))
        {
            return false;
        }

        var userName = result.CredentialUserName;
        var domain = string.IsNullOrWhiteSpace(result.CredentialDomainName)
            ? null
            : result.CredentialDomainName;
        var password = result.CredentialPassword;
        try
        {
            return _verifyCredential(userName, domain, password);
        }
        finally
        {
            // CredentialPicker owns the original buffer. Drop our managed reference
            // immediately after the single validation call and never persist it.
            password = string.Empty;
        }
    }

    private static bool VerifyWindowsCredential(string userName, string? domain, string password)
    {
        if (!LogonUser(
                userName,
                domain,
                password,
                LogonType.Interactive,
                LogonProvider.Default,
                out var token))
        {
            return false;
        }

        return CloseHandle(token);
    }

    private enum LogonType : uint
    {
        Interactive = 2,
    }

    private enum LogonProvider : uint
    {
        Default = 0,
    }

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool LogonUser(
        string userName,
        string? domain,
        string password,
        LogonType logonType,
        LogonProvider logonProvider,
        out IntPtr token);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);
}
