using System.Globalization;
using System.Text;

namespace LiubaiMfa.Windows.ViewModels;

public static class ServiceIconResolver
{
    private static readonly string[] Palette =
    [
        "#FF2563EB", "#FF7C3AED", "#FF0F766E", "#FFB45309",
        "#FFBE123C", "#FF0369A1", "#FF4F46E5", "#FF15803D",
    ];

    public static ServiceIconDescriptor Resolve(string? issuer)
    {
        var normalized = issuer?.Trim() ?? string.Empty;
        if (normalized.Length == 0)
        {
            return CreateFallback("?", "#FF64748B");
        }

        var canonicalIssuer = normalized.ToUpperInvariant();
        return canonicalIssuer switch
        {
            "GOOGLE" => CreateFallback("G", "#FF4285F4"),
            "MICROSOFT" => CreateFallback("M", "#FF0078D4"),
            "OPENAI" => CreateFallback("O", "#FF10A37F"),
            "META" => CreateFallback("M", "#FF0866FF"),
            "X" => CreateFallback("X", "#FF111827"),
            "VAULTWARDEN" => CreateFallback("V", "#FF175DDC"),
            _ => CreateFallback(GetFallbackText(normalized), Palette[GetStableIndex(canonicalIssuer, Palette.Length)]),
        };
    }

    private static ServiceIconDescriptor CreateFallback(string text, string backgroundColor) =>
        new(ServiceIconKind.FallbackText, text, null, backgroundColor, GetReadableForegroundColor(backgroundColor));

    private static string GetReadableForegroundColor(string backgroundColor)
    {
        var red = byte.Parse(backgroundColor.AsSpan(3, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255d;
        var green = byte.Parse(backgroundColor.AsSpan(5, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255d;
        var blue = byte.Parse(backgroundColor.AsSpan(7, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255d;
        var luminance = (0.2126 * ToLinear(red)) + (0.7152 * ToLinear(green)) + (0.0722 * ToLinear(blue));
        var blackContrast = (luminance + 0.05) / 0.05;
        var whiteContrast = 1.05 / (luminance + 0.05);

        return blackContrast >= whiteContrast ? "#FF000000" : "#FFFFFFFF";
    }

    private static double ToLinear(double component) => component <= 0.04045
        ? component / 12.92
        : Math.Pow((component + 0.055) / 1.055, 2.4);

    private static string GetFallbackText(string issuer) => StringInfo.GetNextTextElement(issuer).ToUpperInvariant();

    private static int GetStableIndex(string value, int count)
    {
        const uint offsetBasis = 2166136261;
        const uint prime = 16777619;
        var hash = offsetBasis;

        foreach (var valueByte in Encoding.UTF8.GetBytes(value))
        {
            hash ^= valueByte;
            hash *= prime;
        }

        return (int)(hash % count);
    }
}
