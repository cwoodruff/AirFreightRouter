using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using AirFreightRouter.Models;
using AirFreightRouter.Services;

namespace AirFreightRouter.Views;

/// <summary>
/// A Canvas-based UserControl that renders a 2-D map of cities and the
/// computed air-freight route using an equirectangular projection.
/// </summary>
/// <remarks>
/// Drawing layers (bottom-to-top):
/// <list type="number">
///   <item>Grid lines</item>
///   <item>Route polyline (glow + solid) and dashed return leg</item>
///   <item>City dots (glow + solid fill)</item>
///   <item>City name labels</item>
/// </list>
/// All brushes are created once as static frozen resources to avoid
/// per-frame allocations.
/// </remarks>
public partial class RouteMapControl : UserControl
{
    // ------------------------------------------------------------------
    // Static brushes (frozen for rendering performance)
    // ------------------------------------------------------------------

    private static readonly SolidColorBrush BrushGrid =
        Freeze(new SolidColorBrush(Color.FromArgb(0x28, 0x55, 0x55, 0x88)));

    private static readonly SolidColorBrush BrushRouteGlow =
        Freeze(new SolidColorBrush(Color.FromArgb(0x44, 0x7B, 0xBC, 0xFF)));

    private static readonly SolidColorBrush BrushRoute =
        Freeze(new SolidColorBrush(Color.FromRgb(0x7B, 0xBC, 0xFF)));

    private static readonly SolidColorBrush BrushDash =
        Freeze(new SolidColorBrush(Color.FromArgb(0xCC, 0x7B, 0xBC, 0xFF)));

    private static readonly SolidColorBrush BrushOriginGlow =
        Freeze(new SolidColorBrush(Color.FromArgb(0x55, 0xFF, 0xD7, 0x00)));

    private static readonly SolidColorBrush BrushOrigin =
        Freeze(new SolidColorBrush(Color.FromRgb(0xFF, 0xD7, 0x00)));   // gold

    private static readonly SolidColorBrush BrushCityGlow =
        Freeze(new SolidColorBrush(Color.FromArgb(0x55, 0x4A, 0x9D, 0xFF)));

    private static readonly SolidColorBrush BrushCity =
        Freeze(new SolidColorBrush(Color.FromRgb(0x4A, 0x9D, 0xFF)));   // accent blue

    private static readonly SolidColorBrush BrushLabel =
        Freeze(new SolidColorBrush(Colors.White));

    private static readonly SolidColorBrush BrushLabelOrigin =
        Freeze(new SolidColorBrush(Color.FromRgb(0xFF, 0xE0, 0x60)));   // warm gold

    private static readonly DoubleCollection DashPattern =
        Freeze(new DoubleCollection([6, 4]));

    // Dot sizes
    private const double DotRadius      = 8.0;
    private const double DotGlowRadius  = 14.0;
    private const double LabelOffset    = 12.0;
    private const double LabelFontSize  = 10.5;

    // Grid divisions
    private const int GridLines = 6;

    // ------------------------------------------------------------------
    // Dependency Properties
    // ------------------------------------------------------------------

    /// <summary>All cities to display as dots (should include the origin).</summary>
    public static readonly DependencyProperty CitiesProperty =
        DependencyProperty.Register(
            nameof(Cities), typeof(IList<City>), typeof(RouteMapControl),
            new FrameworkPropertyMetadata(null, OnMapDataChanged));

    /// <summary>
    /// The computed route as an ordered list starting and ending with the
    /// origin city, e.g. [Albany, Boston, …, Albany].
    /// </summary>
    public static readonly DependencyProperty RouteProperty =
        DependencyProperty.Register(
            nameof(Route), typeof(IList<City>), typeof(RouteMapControl),
            new FrameworkPropertyMetadata(null, OnMapDataChanged));

    /// <summary>The fixed origin / destination city (always drawn in gold).</summary>
    public static readonly DependencyProperty OriginProperty =
        DependencyProperty.Register(
            nameof(Origin), typeof(City), typeof(RouteMapControl),
            new FrameworkPropertyMetadata(null, OnMapDataChanged));

    public IList<City>? Cities
    {
        get => (IList<City>?)GetValue(CitiesProperty);
        set => SetValue(CitiesProperty, value);
    }

    public IList<City>? Route
    {
        get => (IList<City>?)GetValue(RouteProperty);
        set => SetValue(RouteProperty, value);
    }

    public City? Origin
    {
        get => (City?)GetValue(OriginProperty);
        set => SetValue(OriginProperty, value);
    }

    // ------------------------------------------------------------------
    // Constructor
    // ------------------------------------------------------------------

    public RouteMapControl()
    {
        InitializeComponent();
    }

    // ------------------------------------------------------------------
    // Dependency-property change callback
    // ------------------------------------------------------------------

    private static void OnMapDataChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((RouteMapControl)d).Redraw();
    }

    // ------------------------------------------------------------------
    // SizeChanged handler
    // ------------------------------------------------------------------

    private void MapCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        Redraw();
    }

    // ------------------------------------------------------------------
    // Core drawing
    // ------------------------------------------------------------------

    private readonly MapRenderer _renderer = new();

    private void Redraw()
    {
        MapCanvas.Children.Clear();

        double w = MapCanvas.ActualWidth;
        double h = MapCanvas.ActualHeight;

        // Skip rendering if the canvas has no size yet.
        if (w < 10 || h < 10) return;

        var cities = Cities;
        if (cities == null || cities.Count == 0)
        {
            DrawEmptyPlaceholder(w, h);
            return;
        }

        _renderer.Initialise(cities, w, h);

        DrawGrid(w, h);
        DrawRoute(cities);
        DrawCityDots(cities);
        DrawLabels(cities, w, h);
    }

    // ------------------------------------------------------------------
    // Layer 1 · Grid
    // ------------------------------------------------------------------

    private void DrawGrid(double w, double h)
    {
        double step = w / (GridLines + 1);
        for (int i = 1; i <= GridLines; i++)
        {
            double x = i * step;
            MapCanvas.Children.Add(new Line
            {
                X1 = x, Y1 = 0, X2 = x, Y2 = h,
                Stroke = BrushGrid, StrokeThickness = 1
            });
        }

        step = h / (GridLines + 1);
        for (int i = 1; i <= GridLines; i++)
        {
            double y = i * step;
            MapCanvas.Children.Add(new Line
            {
                X1 = 0, Y1 = y, X2 = w, Y2 = y,
                Stroke = BrushGrid, StrokeThickness = 1
            });
        }
    }

    // ------------------------------------------------------------------
    // Layer 2 · Route polylines
    // ------------------------------------------------------------------

    private void DrawRoute(IList<City> cities)
    {
        var route = Route;
        if (route == null || route.Count < 2) return;

        // Route = [origin, c1, c2, …, cN, origin].
        // Solid leg: origin → c1 → … → cN  (all segments except the last).
        // Dashed leg: cN → origin (the return flight).

        int n = route.Count;

        // --- solid polyline (glow layer) ---
        var glowPoints = new PointCollection();
        for (int k = 0; k < n - 1; k++)
            glowPoints.Add(_renderer.Project(route[k]));

        MapCanvas.Children.Add(new Polyline
        {
            Points          = glowPoints,
            Stroke          = BrushRouteGlow,
            StrokeThickness = 8,
            StrokeLineJoin  = PenLineJoin.Round,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap   = PenLineCap.Round
        });

        // --- solid polyline (main layer) ---
        var solidPoints = new PointCollection(glowPoints);   // same points
        MapCanvas.Children.Add(new Polyline
        {
            Points          = solidPoints,
            Stroke          = BrushRoute,
            StrokeThickness = 2,
            StrokeLineJoin  = PenLineJoin.Round,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap   = PenLineCap.Round
        });

        // --- dashed return leg: route[n-2] → route[n-1] ---
        var p1 = _renderer.Project(route[n - 2]);
        var p2 = _renderer.Project(route[n - 1]);

        MapCanvas.Children.Add(new Line
        {
            X1 = p1.X, Y1 = p1.Y, X2 = p2.X, Y2 = p2.Y,
            Stroke = BrushDash,
            StrokeThickness = 2,
            StrokeDashArray = DashPattern,
            StrokeDashCap   = PenLineCap.Round
        });
    }

    // ------------------------------------------------------------------
    // Layer 3 · City dots
    // ------------------------------------------------------------------

    private void DrawCityDots(IList<City> cities)
    {
        var origin = Origin;

        foreach (var city in cities.Distinct())
        {
            var pt         = _renderer.Project(city);
            bool isOrigin  = origin != null &&
                             string.Equals(city.Name,  origin.Name,  StringComparison.OrdinalIgnoreCase) &&
                             string.Equals(city.State, origin.State, StringComparison.OrdinalIgnoreCase);

            var glowBrush  = isOrigin ? BrushOriginGlow : BrushCityGlow;
            var dotBrush   = isOrigin ? BrushOrigin     : BrushCity;

            // Glow ellipse
            AddEllipse(pt, DotGlowRadius, glowBrush);
            // Solid dot
            AddEllipse(pt, DotRadius, dotBrush);
        }
    }

    private void AddEllipse(System.Windows.Point centre, double radius, Brush fill)
    {
        var e = new Ellipse
        {
            Width  = radius * 2,
            Height = radius * 2,
            Fill   = fill
        };
        Canvas.SetLeft(e, centre.X - radius);
        Canvas.SetTop (e, centre.Y - radius);
        MapCanvas.Children.Add(e);
    }

    // ------------------------------------------------------------------
    // Layer 4 · Labels
    // ------------------------------------------------------------------

    private void DrawLabels(IList<City> cities, double canvasWidth, double canvasHeight)
    {
        var origin = Origin;

        foreach (var city in cities.Distinct())
        {
            var pt        = _renderer.Project(city);
            bool isOrigin = origin != null &&
                            string.Equals(city.Name,  origin.Name,  StringComparison.OrdinalIgnoreCase) &&
                            string.Equals(city.State, origin.State, StringComparison.OrdinalIgnoreCase);

            // Position label to avoid clipping: favour right/below but flip
            // when close to the right or bottom edge.
            double xOffset = pt.X > canvasWidth  - 80 ? -LabelOffset - 50 : LabelOffset;
            double yOffset = pt.Y > canvasHeight - 30 ? -LabelOffset - 14 : LabelOffset;

            var tb = new TextBlock
            {
                Text       = city.Name,
                FontSize   = LabelFontSize,
                FontWeight = isOrigin ? FontWeights.SemiBold : FontWeights.Normal,
                Foreground = isOrigin ? BrushLabelOrigin : BrushLabel,
                Opacity    = 0.90
            };

            Canvas.SetLeft(tb, pt.X + xOffset);
            Canvas.SetTop (tb, pt.Y + yOffset);
            MapCanvas.Children.Add(tb);
        }
    }

    // ------------------------------------------------------------------
    // Empty-state placeholder
    // ------------------------------------------------------------------

    private void DrawEmptyPlaceholder(double w, double h)
    {
        var tb = new TextBlock
        {
            Text       = "Load city data to see the map",
            FontSize   = 14,
            Foreground = new SolidColorBrush(Color.FromArgb(0x60, 0xFF, 0xFF, 0xFF)),
            HorizontalAlignment = HorizontalAlignment.Center
        };
        tb.Measure(new Size(w, h));
        Canvas.SetLeft(tb, (w - tb.DesiredSize.Width)  / 2);
        Canvas.SetTop (tb, (h - tb.DesiredSize.Height) / 2);
        MapCanvas.Children.Add(tb);
    }

    // ------------------------------------------------------------------
    // Helper
    // ------------------------------------------------------------------

    private static T Freeze<T>(T freezable) where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }
}
