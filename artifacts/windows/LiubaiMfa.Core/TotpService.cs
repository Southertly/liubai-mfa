using System.Security.Cryptography;

namespace LiubaiMfa.Core;

public static class TotpService
{
    private const string Base32Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    public static string Generate(
        string secret,
        DateTimeOffset? timestamp = null,
        int period = 30,
        int digits = 6)
    {
        if (period <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(period));
        }

        if (digits is < 6 or > 8)
        {
            throw new ArgumentOutOfRangeException(nameof(digits));
        }

        var secretBytes = DecodeBase32(secret);
        var unixSeconds = (timestamp ?? DateTimeOffset.UtcNow).ToUnixTimeSeconds();
        var counter = unixSeconds / period;
        Span<byte> counterBytes = stackalloc byte[8];

        for (var index = 7; index >= 0; index--)
        {
            counterBytes[index] = (byte)(counter & 0xff);
            counter >>= 8;
        }

        using var hmac = new HMACSHA1(secretBytes);
        var hash = hmac.ComputeHash(counterBytes.ToArray());
        var offset = hash[^1] & 0x0f;
        var binaryCode =
            ((hash[offset] & 0x7f) << 24) |
            ((hash[offset + 1] & 0xff) << 16) |
            ((hash[offset + 2] & 0xff) << 8) |
            (hash[offset + 3] & 0xff);
        var modulus = (int)Math.Pow(10, digits);
        return (binaryCode % modulus).ToString($"D{digits}");
    }

    public static int GetTimeRemaining(DateTimeOffset? timestamp = null, int period = 30)
    {
        if (period <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(period));
        }

        var seconds = (timestamp ?? DateTimeOffset.UtcNow).ToUnixTimeSeconds();
        return period - (int)(seconds % period);
    }

    public static string NormalizeSecret(string secret) =>
        new(secret
            .Where(character => !char.IsWhiteSpace(character) && character != '=')
            .Select(char.ToUpperInvariant)
            .ToArray());

    private static byte[] DecodeBase32(string secret)
    {
        var normalized = NormalizeSecret(secret);
        if (normalized.Length == 0)
        {
            throw new FormatException("MFA 密钥不能为空。");
        }

        var output = new List<byte>(normalized.Length * 5 / 8);
        var buffer = 0;
        var bitsInBuffer = 0;

        foreach (var character in normalized)
        {
            var value = Base32Alphabet.IndexOf(character);
            if (value < 0)
            {
                throw new FormatException("MFA 密钥不是有效的 Base32 字符串。");
            }

            buffer = (buffer << 5) | value;
            bitsInBuffer += 5;
            if (bitsInBuffer < 8)
            {
                continue;
            }

            output.Add((byte)((buffer >> (bitsInBuffer - 8)) & 0xff));
            bitsInBuffer -= 8;
        }

        if (output.Count == 0)
        {
            throw new FormatException("MFA 密钥长度不足。");
        }

        return output.ToArray();
    }
}
