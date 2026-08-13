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
        new(ServiceIconKind.FallbackText, text, null, backgroundColor);

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
