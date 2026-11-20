using System.Text;

namespace LiubaiMfa.CoreHost;

internal sealed record QrDraft(string Issuer, string Account, string Secret);

internal static class StrictOtpAuthParser
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    public static QrDraft Parse(string text)
    {
        try { return ParseCore(text); }
        catch (QrOperationException) { throw; }
        catch (Exception error) when (error is ArgumentException or FormatException or OverflowException)
        { throw Invalid(); }
    }
    private static QrDraft ParseCore(string text)
    {
        if (text is null || text.Length > 8192 || text.Any(char.IsControl) || text.Contains('#') || text.Contains('\\') || !text.StartsWith("otpauth://", StringComparison.OrdinalIgnoreCase)) throw Invalid();
        var separator = text.IndexOf('/', 10);
        if (separator < 0) throw Invalid();
        var authority = text[10..separator];
        if (authority.Equals("hotp", StringComparison.OrdinalIgnoreCase)) throw Unsupported();
        if (!authority.Equals("totp", StringComparison.OrdinalIgnoreCase)) throw Invalid();
        var queryStart = text.IndexOf('?', separator + 1);
        if (queryStart < 0) throw Invalid();
        var path = text[(separator + 1)..queryStart];
        if (path.Contains('/')) throw Invalid();
        var label = Decode(path);
        var query = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var parameter in text[(queryStart + 1)..].Split('&'))
        {
            var equals = parameter.IndexOf('=');
            if (equals <= 0) throw Invalid();
            var key = Decode(parameter[..equals]);
            if (!query.TryAdd(key, Decode(parameter[(equals + 1)..]))) throw Invalid();
        }
        if (query.Keys.Any(key => !new[] { "secret", "issuer", "algorithm", "digits", "period" }.Contains(key, StringComparer.OrdinalIgnoreCase))) throw Unsupported();
        if ((query.TryGetValue("algorithm", out var algorithm) && !algorithm.Equals("SHA1", StringComparison.OrdinalIgnoreCase)) ||
            (query.TryGetValue("digits", out var digits) && digits != "6") ||
            (query.TryGetValue("period", out var period) && period != "30")) throw Unsupported();
        if (!query.TryGetValue("secret", out var secret)) throw Invalid();
        var colon = label.IndexOf(':');
        var labelIssuer = colon >= 0 ? Field(label[..colon], 80) : null;
        var account = Field(colon >= 0 ? label[(colon + 1)..] : label, 120);
        var queryIssuer = query.TryGetValue("issuer", out var issuer) ? Field(issuer, 80) : null;
        if (labelIssuer is not null && queryIssuer is not null && labelIssuer != queryIssuer) throw Invalid();
        return new(labelIssuer ?? queryIssuer ?? "未命名服务", account, SecretValidation.Normalize(secret));
    }
    private static string Field(string value, int maximum)
    {
        var text = value.Trim();
        if (text.Length == 0 || text.Length > maximum || value.Any(char.IsControl)) throw Invalid();
        return text;
    }
    private static string Decode(string value)
    {
        using var bytes = new MemoryStream();
        for (var index = 0; index < value.Length; index++)
        {
            if (value[index] == '%')
            {
                if (index + 2 >= value.Length || !byte.TryParse(value.AsSpan(index + 1, 2), System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out var decoded)) throw Invalid();
                bytes.WriteByte(decoded); index += 2;
            }
            else
            {
                var length = char.IsHighSurrogate(value[index]) ? 2 : 1;
                if (index + length > value.Length) throw Invalid();
                bytes.Write(StrictUtf8.GetBytes(value.Substring(index, length))); index += length - 1;
            }
        }
        var result = StrictUtf8.GetString(bytes.ToArray());
        if (result.Any(char.IsControl)) throw Invalid();
        return result;
    }
    private static QrOperationException Invalid() => new("QR_INVALID", "二维码内容无效，请使用身份验证器的 TOTP 二维码。");
    private static QrOperationException Unsupported() => new("QR_UNSUPPORTED", "此二维码使用了暂不支持的验证参数，请使用 SHA1、6 位、30 秒的 TOTP 二维码。");
}
