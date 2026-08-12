namespace LiubaiMfa.Core;

public sealed record OtpAuthAccount(string Issuer, string Account, string Secret);

public static class OtpAuthUriParser
{
    public static bool TryParse(string value, out OtpAuthAccount? result)
    {
        result = null;
        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri) ||
            !string.Equals(uri.Scheme, "otpauth", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(uri.Host, "totp", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var query = ParseQuery(uri.Query);
        if (!query.TryGetValue("secret", out var rawSecret))
        {
            return false;
        }

        var secret = TotpService.NormalizeSecret(rawSecret);
        try
        {
            _ = TotpService.Generate(secret);
        }
        catch (FormatException)
        {
            return false;
        }

        var label = Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/'));
        query.TryGetValue("issuer", out var issuer);
        var account = label;
        var separatorIndex = label.IndexOf(':');
        if (separatorIndex >= 0)
        {
            issuer = string.IsNullOrWhiteSpace(issuer) ? label[..separatorIndex].Trim() : issuer;
            account = label[(separatorIndex + 1)..].Trim();
        }

        result = new OtpAuthAccount(issuer?.Trim() ?? string.Empty, account.Trim(), secret);
        return true;
    }

    private static Dictionary<string, string> ParseQuery(string query)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            var key = Uri.UnescapeDataString(parts[0].Replace('+', ' '));
            var value = parts.Length == 2
                ? Uri.UnescapeDataString(parts[1].Replace('+', ' '))
                : string.Empty;
            values[key] = value;
        }

        return values;
    }
}
