namespace LiubaiMfa.CoreHost;

internal sealed class BackupOperationException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
