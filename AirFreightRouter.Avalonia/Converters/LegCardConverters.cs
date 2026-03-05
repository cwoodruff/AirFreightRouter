using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;
using AirFreightRouter.Models;

namespace AirFreightRouter.Avalonia.Converters;

/// <summary>Returns 0.45 opacity when the bound bool is true (computing), 1.0 otherwise.</summary>
public class OpacityFromBoolConverter : IValueConverter
{
    public static readonly OpacityFromBoolConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? 0.45 : 1.0;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Returns the card background brush based on IsReturn / IsAlternate flags.</summary>
public class LegCardBackgroundConverter : IValueConverter
{
    public static readonly LegCardBackgroundConverter Instance = new();

    private static readonly IBrush BgDefault   = new SolidColorBrush(Color.Parse("#1E1E30"));
    private static readonly IBrush BgAlternate = new SolidColorBrush(Color.Parse("#161625"));
    private static readonly IBrush BgReturn    = new SolidColorBrush(Color.Parse("#15202E"));

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is RouteLeg leg)
        {
            if (leg.IsReturn)    return BgReturn;
            if (leg.IsAlternate) return BgAlternate;
        }
        return BgDefault;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Returns the card border brush: AccentBrush for return legs, subtle otherwise.</summary>
public class LegCardBorderBrushConverter : IValueConverter
{
    public static readonly LegCardBorderBrushConverter Instance = new();

    private static readonly IBrush Subtle = new SolidColorBrush(Color.Parse("#28283E"));
    private static readonly IBrush Accent = new SolidColorBrush(Color.Parse("#4A9DFF"));

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? Accent : Subtle;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Returns the card border thickness: left=2 for return legs, uniform 1 otherwise.</summary>
public class LegCardBorderThicknessConverter : IValueConverter
{
    public static readonly LegCardBorderThicknessConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true
            ? new Thickness(2, 1, 1, 1)
            : new Thickness(1);

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
