namespace LiubaiMfa.CoreHost;
internal sealed class QrOperationException(string code, string message) : ArgumentException(message)
{ public string Code { get; } = code; }
