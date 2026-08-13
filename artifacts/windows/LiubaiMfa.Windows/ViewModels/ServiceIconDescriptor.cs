namespace LiubaiMfa.Windows.ViewModels;

public enum ServiceIconKind
{
    FallbackText,
    LocalGlyph,
}

public sealed record ServiceIconDescriptor(
    ServiceIconKind Kind,
    string FallbackText,
    string? Glyph,
    string BackgroundColor);
