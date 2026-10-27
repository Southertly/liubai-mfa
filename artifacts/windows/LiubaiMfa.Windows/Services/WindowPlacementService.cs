using Microsoft.UI.Windowing;
using Windows.Graphics;
using Windows.Storage;

namespace LiubaiMfa.Windows.Services;

internal readonly record struct PixelRect(int X, int Y, int Width, int Height);

public sealed class WindowPlacementService
{
    internal const int DefaultEffectiveWidth = 560;
    internal const int DefaultEffectiveHeight = 780;
    internal const int MinimumEffectiveWidth = 480;
    internal const int MinimumEffectiveHeight = 620;

    private const uint DefaultDpi = 96;
    private const string XKey = "WindowPlacement.X";
    private const string YKey = "WindowPlacement.Y";
    private const string WidthKey = "WindowPlacement.Width";
    private const string HeightKey = "WindowPlacement.Height";

    private readonly ApplicationDataContainer _settings;
    private uint _dpi = DefaultDpi;
    private bool _isApplyingPlacement;

    public WindowPlacementService(ApplicationDataContainer? settings = null)
    {
        _settings = settings ?? ApplicationData.Current.LocalSettings;
    }

    public void Configure(AppWindow appWindow, uint dpi)
    {
        _dpi = NormalizeDpi(dpi);
        if (appWindow.Presenter is not OverlappedPresenter presenter)
        {
            return;
        }

        presenter.IsMaximizable = false;
        presenter.IsMinimizable = true;
        presenter.IsResizable = true;
    }

    public void RestoreOrApplyDefault(AppWindow appWindow, Func<uint> getCurrentDpi)
    {
        if (appWindow.Presenter is OverlappedPresenter { State: not OverlappedPresenterState.Restored } presenter)
        {
            presenter.Restore();
        }

        var hasSavedPlacement = TryReadPlacement(out var saved);
        var displayArea = hasSavedPlacement ? GetDisplayArea(saved) : DisplayArea.Primary;
        var workArea = GetScreenWorkArea(displayArea);
        var preliminary = hasSavedPlacement
            ? Clamp(saved, workArea, 1, 1)
            : CreateDefaultPlacement(workArea);
        ApplyPlacement(appWindow, preliminary);

        _dpi = ReadDpi(getCurrentDpi);
        var placement = hasSavedPlacement
            ? Clamp(saved, workArea, MinimumPhysicalWidth, MinimumPhysicalHeight)
            : CreateDefaultPlacement(workArea);
        if (placement != preliminary)
        {
            ApplyPlacement(appWindow, placement);
        }
    }

    public void SaveNormalPlacement(AppWindow appWindow, AppWindowChangedEventArgs args, uint dpi)
    {
        _dpi = NormalizeDpi(dpi);
        var isRestored = appWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Restored };
        if (_isApplyingPlacement || !ShouldSave(args.DidPositionChange, args.DidSizeChange, isRestored))
        {
            return;
        }

        var current = FromAppWindow(appWindow);
        var needsMinimumSize = current.Width < MinimumPhysicalWidth || current.Height < MinimumPhysicalHeight;
        var placement = args.DidSizeChange || needsMinimumSize ? EnforceMinimumSize(current) : current;
        if (placement != current)
        {
            ApplyPlacement(appWindow, placement);
        }

        Save(placement);
    }

    internal static PixelRect Clamp(
        PixelRect desired,
        PixelRect workArea,
        int minimumWidth,
        int minimumHeight)
    {
        var availableWidth = Math.Max(1, workArea.Width);
        var availableHeight = Math.Max(1, workArea.Height);
        var safeMinimumWidth = Math.Clamp(minimumWidth, 1, availableWidth);
        var safeMinimumHeight = Math.Clamp(minimumHeight, 1, availableHeight);
        var width = desired.Width <= 0
            ? safeMinimumWidth
            : Math.Clamp(desired.Width, safeMinimumWidth, availableWidth);
        var height = desired.Height <= 0
            ? safeMinimumHeight
            : Math.Clamp(desired.Height, safeMinimumHeight, availableHeight);

        var maximumX = (long)workArea.X + availableWidth - width;
        var maximumY = (long)workArea.Y + availableHeight - height;
        var x = ClampCoordinate(desired.X, workArea.X, maximumX);
        var y = ClampCoordinate(desired.Y, workArea.Y, maximumY);
        return new PixelRect(x, y, width, height);
    }

    internal static int EffectiveToPhysical(int effectivePixels, uint dpi)
    {
        var safeEffectivePixels = (ulong)Math.Max(1, effectivePixels);
        var safeDpi = NormalizeDpi(dpi);
        var physicalPixels = ((safeEffectivePixels * safeDpi) + (DefaultDpi / 2)) / DefaultDpi;
        return physicalPixels >= int.MaxValue ? int.MaxValue : (int)Math.Max(1UL, physicalPixels);
    }

    internal static bool ShouldSave(bool didPositionChange, bool didSizeChange, bool isRestored) =>
        isRestored && (didPositionChange || didSizeChange);

    internal static PixelRect ToScreenWorkArea(PixelRect outerBounds, PixelRect relativeWorkArea)
    {
        var x = ClampCoordinate((long)outerBounds.X + relativeWorkArea.X, int.MinValue, int.MaxValue);
        var y = ClampCoordinate((long)outerBounds.Y + relativeWorkArea.Y, int.MinValue, int.MaxValue);
        return new PixelRect(x, y, relativeWorkArea.Width, relativeWorkArea.Height);
    }

    private PixelRect CreateDefaultPlacement(PixelRect workArea)
    {
        var width = Math.Min(EffectiveToPhysical(DefaultEffectiveWidth, _dpi), Math.Max(1, workArea.Width));
        var height = Math.Min(EffectiveToPhysical(DefaultEffectiveHeight, _dpi), Math.Max(1, workArea.Height));
        var x = ClampCoordinate(
            (long)workArea.X + ((long)workArea.Width - width) / 2,
            workArea.X,
            (long)workArea.X + Math.Max(1, workArea.Width) - width);
        var y = ClampCoordinate(
            (long)workArea.Y + ((long)workArea.Height - height) / 2,
            workArea.Y,
            (long)workArea.Y + Math.Max(1, workArea.Height) - height);
        return Clamp(
            new PixelRect(x, y, width, height),
            workArea,
            MinimumPhysicalWidth,
            MinimumPhysicalHeight);
    }

    private PixelRect EnforceMinimumSize(PixelRect current)
    {
        var displayArea = GetDisplayArea(current);
        return Clamp(current, GetScreenWorkArea(displayArea), MinimumPhysicalWidth, MinimumPhysicalHeight);
    }

    private static PixelRect GetScreenWorkArea(DisplayArea displayArea) => ToScreenWorkArea(
        ToPixelRect(displayArea.OuterBounds),
        ToPixelRect(displayArea.WorkArea));

    private static DisplayArea GetDisplayArea(PixelRect placement)
    {
        var x = placement.X == int.MaxValue ? int.MaxValue - 1 : placement.X;
        var y = placement.Y == int.MaxValue ? int.MaxValue - 1 : placement.Y;
        var maximumWidth = Math.Max(1L, (long)int.MaxValue - x);
        var maximumHeight = Math.Max(1L, (long)int.MaxValue - y);
        var width = (int)Math.Clamp((long)placement.Width, 1, maximumWidth);
        var height = (int)Math.Clamp((long)placement.Height, 1, maximumHeight);
        var probe = new RectInt32(x, y, width, height);
        return DisplayArea.GetFromRect(probe, DisplayAreaFallback.Primary) ?? DisplayArea.Primary;
    }

    private void ApplyPlacement(AppWindow appWindow, PixelRect placement)
    {
        _isApplyingPlacement = true;
        try
        {
            appWindow.MoveAndResize(new RectInt32(
                placement.X,
                placement.Y,
                placement.Width,
                placement.Height));
        }
        finally
        {
            _isApplyingPlacement = false;
        }
    }

    private bool TryReadPlacement(out PixelRect placement)
    {
        placement = default;
        if (!TryReadInt32(XKey, out var x) ||
            !TryReadInt32(YKey, out var y) ||
            !TryReadInt32(WidthKey, out var width) ||
            !TryReadInt32(HeightKey, out var height) ||
            width <= 0 ||
            height <= 0)
        {
            return false;
        }

        placement = new PixelRect(x, y, width, height);
        return true;
    }

    private bool TryReadInt32(string key, out int value)
    {
        if (_settings.Values.TryGetValue(key, out var stored) && stored is int intValue)
        {
            value = intValue;
            return true;
        }

        value = default;
        return false;
    }

    private void Save(PixelRect placement)
    {
        _settings.Values[XKey] = placement.X;
        _settings.Values[YKey] = placement.Y;
        _settings.Values[WidthKey] = placement.Width;
        _settings.Values[HeightKey] = placement.Height;
    }

    private int MinimumPhysicalWidth => EffectiveToPhysical(MinimumEffectiveWidth, _dpi);

    private int MinimumPhysicalHeight => EffectiveToPhysical(MinimumEffectiveHeight, _dpi);

    private static PixelRect FromAppWindow(AppWindow appWindow) => new(
        appWindow.Position.X,
        appWindow.Position.Y,
        appWindow.Size.Width,
        appWindow.Size.Height);

    private static PixelRect ToPixelRect(RectInt32 rectangle) => new(
        rectangle.X,
        rectangle.Y,
        rectangle.Width,
        rectangle.Height);

    private static uint NormalizeDpi(uint dpi) => dpi == 0 ? DefaultDpi : dpi;

    private static uint ReadDpi(Func<uint> getCurrentDpi)
    {
        try
        {
            return NormalizeDpi(getCurrentDpi());
        }
        catch (Exception)
        {
            return DefaultDpi;
        }
    }

    private static int ClampCoordinate(long value, long minimum, long maximum)
    {
        var clamped = Math.Clamp(value, minimum, Math.Max(minimum, maximum));
        return (int)Math.Clamp(clamped, int.MinValue, int.MaxValue);
    }
}
