namespace LiubaiMfa.Windows.Services;

internal sealed class DuplicateMfaSecretException : InvalidOperationException
{
    public DuplicateMfaSecretException()
        : base("该 MFA 密钥已经存在。")
    {
    }
}
