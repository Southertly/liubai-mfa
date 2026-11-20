namespace LiubaiMfa.CoreHost;
internal static class SecretValidation
{
    public static string Normalize(string input)
    {
        // 手动输入只接收 Base32；URI 导入留待独立、严格的导入流程。
        var text = input.Trim().ToUpperInvariant();
        if (text.Length is < 2 or > 1024) throw new AccountInputException("INVALID_SECRET","密钥长度无效，请填写 Base32 密钥。");
        var raw = text.TrimEnd('=');
        if (raw.Length < 2) throw new AccountInputException("INVALID_SECRET","密钥不能为空或仅有填充字符。");
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        if (raw.Any(c => !alphabet.Contains(c))) throw new AccountInputException("INVALID_SECRET","密钥只能包含 Base32 字母 A–Z 和数字 2–7，不支持 URI。");
        var remainder = raw.Length % 8;
        if (remainder is 1 or 3 or 6 || (text.Length != raw.Length && (text.Length % 8 != 0 || text.Length - raw.Length != (8 - remainder) % 8))) throw new AccountInputException("INVALID_SECRET","Base32 密钥长度或填充无效。");
        var trailingBits = raw.Length * 5 % 8;
        if (trailingBits != 0 && (alphabet.IndexOf(raw[^1]) & ((1 << trailingBits) - 1)) != 0) throw new AccountInputException("INVALID_SECRET","Base32 密钥尾部编码无效。");
        return raw;
    }
}
