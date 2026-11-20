namespace LiubaiMfa.CoreHost;
internal sealed class AccountInputException(string code,string message) : ArgumentException(message)
{ public string Code {get;}=code; }
