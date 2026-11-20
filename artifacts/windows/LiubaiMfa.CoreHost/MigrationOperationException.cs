namespace LiubaiMfa.CoreHost;
internal sealed class MigrationOperationException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
