using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using AirFreightRouter.Models;
using AirFreightRouter.Services;

namespace AirFreightRouter.Views;

/// <summary>
/// Canvas-based UserControl that renders a 2-D air-freight route map with
/// live in-progress updates, a marching-ants animation while computing, a
/// completion flash, and a semi-transparent info overlay.
/// </summary>
/// <remarks>
/// Drawing layers (bottom-to-top):
/// <list type="number">
///   <item>Grid lines</item>
///   <item>Route polyline (glow + main) and dashed return leg</item>
///   <item>City dots (glow + solid fill)</item>
///   <item>City name labels</item>
/// </list>
/// References to route-layer elements are kept in <see cref="_routeElements"/>
/// so the completion flash can be applied without a full redraw.
/// </remarks>
public partial class RouteMapControl : UserControl
{
    // ------------------------------------------------------------------
    // Static frozen brushes (allocated once; frozen for rendering perf)
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

    /// <summary>
    /// Dash pattern used for the return-leg line and for in-progress routes.
    /// Total period = 10 + 6 = 16 px (used as the animation To value).
    /// </summary>
    private static readonly DoubleCollection DashPattern =
        Freeze(new DoubleCollection([10, 6]));

    // Geometry constants
    private const double DotRadius     = 8.0;
    private const double DotGlowRadius = 14.0;
    private const double LabelOffset   = 12.0;
    private const double LabelFontSize = 10.5;
    private const int    GridLines     = 6;

    // Animation constants
    private const double MarchingAntsPeriodPx = 16.0;   // dashLen + gapLen
    private const double MarchingAntsDuration = 0.7;    // seconds per period
    private const double FlashFromOpacity     = 0.35;
    private const double FlashDurationMs      = 300.0;

    // ------------------------------------------------------------------
    // Dependency Properties
    // ------------------------------------------------------------------

    /// <summary>All cities to display as dots (should include the origin).</summary>
    public static readonly DependencyProperty CitiesProperty =
        DependencyProperty.Register(
            nameof(Cities), typeof(IList<City>), typeof(RouteMapControl),
            new FrameworkPropertyMetadata(null, OnMapDataChanged));

    /// <summary>
    /// The route as an ordered list [origin, c1, …, cN, origin].
    /// Updated in real-time during computation and set to the final optimal
    /// route on completion.
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

    /// <summary>
    /// When <see langword="true"/> the route is rendered with a dashed,
    /// animated (marching-ants) style to indicate in-progress computation.
    /// Setting this to <see langword="false"/> switches to a solid style
    /// and, if a route exists, plays the completion flash.
    /// </summary>
    public static readonly DependencyProperty IsRouteInProgressProperty =
        DependencyProperty.Register(
            nameof(IsRouteInProgress), typeof(bool), typeof(RouteMapControl),
            new FrameworkPropertyMetadata(false, OnIsRouteInProgressChanged));

    /// <summary>
    /// One-line status shown in the map overlay:
    /// "Computing…", "Complete", or "Cancelled (partial)".
    /// </summary>
    public static readonly DependencyProperty RouteStatusTextProperty =
        DependencyProperty.Register(
            nameof(RouteStatusText), typeof(string), typeof(RouteMapControl),
            new FrameworkPropertyMetadata(string.Empty, OnOverlayDataChanged));

    /// <summary>
    /// Best distance found so far, e.g. "12.3456°".  Displayed in the
    /// map overlay during and after computation.
    /// </summary>
    public static readonly DependencyProperty BestDistanceTextProperty =
        DependencyProperty.Register(
            nameof(BestDistanceText), typeof(string), typeof(RouteMapControl),
            new FrameworkPropertyMetadata(string.Empty, OnOverlayDataChanged));

    // CLR wrappers
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

    public bool IsRouteInProgress
    {
        get => (bool)GetValue(IsRouteInProgressProperty);
        set => SetValue(IsRouteInProgressProperty, value);
    }

    public string RouteStatusText
    {
        get => (string)GetValue(RouteStatusTextProperty);
        set => SetValue(RouteStatusTextProperty, value);
    }

    public string BestDistanceText
    {
        get => (string)GetValue(BestDistanceTextProperty);
        set => SetValue(BestDistanceTextProperty, value);
    }

    // ------------------------------------------------------------------
    // Constructor
    // ------------------------------------------------------------------

    public RouteMapControl()
    {
        InitializeComponent();
    }

    // ------------------------------------------------------------------
    // Dependency-property callbacks
    // ------------------------------------------------------------------

    private static void OnMapDataChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var ctrl = (RouteMapControl)d;
        ctrl.Redraw();
        ctrl.UpdateOverlay();
    }

    private static void OnIsRouteInProgressChanged(
        DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var ctrl          = (RouteMapControl)d;
        bool wasInProgress = (bool)e.OldValue;
        bool nowInProgress = (bool)e.NewValue;

        ctrl.Redraw();
        ctrl.UpdateOverlay();

        // Play the completion flash when transitioning computing → complete
        // (but not when cancelled, which leaves a partial route).
        if (wasInProgress && !nowInProgress
            && ctrl._routeElements.Count > 0
            && ctrl.RouteStatusText == "Complete")
        {
            ctrl.PlayCompletionFlash();
        }
    }

    private static void OnOverlayDataChanged(
        DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((RouteMapControl)d).UpdateOverlay();
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

    private readonly MapRenderer      _renderer     = new();
    private readonly List<UIElement>  _routeElements = [];

    private void Redraw()
    {
        MapCanvas.Children.Clear();
        _routeElements.Clear();

        double w = MapCanvas.ActualWidth;
        double h = MapCanvas.ActualHeight;

        if (w < 10 || h < 10) return;

        var cities = Cities;
        if (cities == null || cities.Count == 0)
        {
            DrawEmptyPlaceholder(w, h);
            return;
        }

        _renderer.Initialise(cities, w, h);

        DrawGrid(w, h);
        DrawRoute();
        DrawCityDots(cities);
        DrawLabels(cities, w, h);
    }

    // ------------------------------------------------------------------
    // Layer 1 · Grid
    // ------------------------------------------------------------------

    private void DrawGrid(double w, double h)
    {
        double xStep = w / (GridLines + 1);
        for (int i = 1; i <= GridLines; i++)
        {
            double x = i * xStep;
            MapCanvas.Children.Add(new Line
            {
                X1 = x, Y1 = 0, X2 = x, Y2 = h,
                Stroke = BrushGrid, StrokeThickness = 1
            });
        }

        double yStep = h / (GridLines + 1);
        for (int i = 1; i <= GridLines; i++)
        {
            double y = i * yStep;
            MapCanvas.Children.Add(new Line
            {
                X1 = 0, Y1 = y, X2 = w, Y2 = y,
                Stroke = BrushGrid, StrokeThickness = 1
            });
        }
    }

    // ------------------------------------------------------------------
    // Layer 2 · Route
    // ------------------------------------------------------------------

    private void DrawRoute()
    {
        var route = Route;
        if (route == null || route.Count < 2) return;

        int  n           = route.Count;
        bool inProgress  = IsRouteInProgress;

        // Route = [origin, c1, …, cN, origin]
        // Outbound leg: points 0 … n-2  (all but the last)
        // Return leg:   point n-2 → n-1 (last city back to origin)

        // --- glow layer (always non-dashed) ---
        var glowPoints = new PointCollection();
        for (int k = 0; k < n - 1; k++)
            glowPoints.Add(_renderer.Project(route[k]));

        var glowLine = new Polyline
        {
            Points             = glowPoints,
            Stroke             = BrushRouteGlow,
            StrokeThickness    = inProgress ? 10 : 8,
            StrokeLineJoin     = PenLineJoin.Round,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap   = PenLineCap.Round,
            Opacity            = inProgress ? 0.55 : 1.0
        };
        MapCanvas.Children.Add(glowLine);
        _routeElements.Add(glowLine);

        // --- main outbound polyline ---
        var mainPoints = new PointCollection(glowPoints);
        var mainLine   = new Polyline
        {
            Points             = mainPoints,
            Stroke             = BrushRoute,
            StrokeThickness    = 2,
            StrokeLineJoin     = PenLineJoin.Round,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap   = PenLineCap.Round
        };

        if (inProgress)
        {
            // Apply dash pattern for the marching-ants animation.
            mainLine.StrokeDashArray = DashPattern;
            StartMarchingAnts(mainLine);
        }

        MapCanvas.Children.Add(mainLine);
        _routeElements.Add(mainLine);

        // --- return leg (always dashed, static) ---
        var p1       = _renderer.Project(route[n - 2]);
        var p2       = _renderer.Project(route[n - 1]);
        var returnLeg = new Line
        {
            X1 = p1.X, Y1 = p1.Y, X2 = p2.X, Y2 = p2.Y,
            Stroke          = BrushDash,
            StrokeThickness = inProgress ? 1.5 : 2,
            StrokeDashArray = DashPattern,
            StrokeDashCap   = PenLineCap.Round,
            Opacity         = inProgress ? 0.60 : 1.0
        };
        MapCanvas.Children.Add(returnLeg);
        _routeElements.Add(returnLeg);
    }

    // ------------------------------------------------------------------
    // Layer 3 · City dots
    // ------------------------------------------------------------------

    private void DrawCityDots(IList<City> cities)
    {
        var origin = Origin;

        foreach (var city in cities.Distinct())
        {
            var  pt       = _renderer.Project(city);
            bool isOrigin = IsOriginCity(city, origin);

            AddEllipse(pt, DotGlowRadius, isOrigin ? BrushOriginGlow : BrushCityGlow);
            AddEllipse(pt, DotRadius,     isOrigin ? BrushOrigin     : BrushCity);
        }
    }

    private void AddEllipse(Point centre, double radius, Brush fill)
    {
        var e = new Ellipse { Width = radius * 2, Height = radius * 2, Fill = fill };
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
            var  pt       = _renderer.Project(city);
            bool isOrigin = IsOriginCity(city, origin);

            // Flip label position near right/bottom canvas edges to avoid clipping.
            double xOffset = pt.X > canvasWidth  - 80 ? -(LabelOffset + 50) : LabelOffset;
            double yOffset = pt.Y > canvasHeight - 30 ? -(LabelOffset + 14) : LabelOffset;

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
    // Info overlay
    // ------------------------------------------------------------------

    private void UpdateOverlay()
    {
        string status   = RouteStatusText;
        string distance = BestDistanceText;
        var    route    = Route;

        // Hide the overlay when there is nothing to show.
        if (string.IsNullOrEmpty(status) && string.IsNullOrEmpty(distance))
        {
            InfoOverlay.Visibility = Visibility.Collapsed;
            return;
        }

        TbStatus.Text   = status;
        TbDistance.Text = string.IsNullOrEmpty(distance)
            ? string.Empty
            : $"Best distance: {distance}";
        TbCities.Text   = route is { Count: > 2 }
            ? $"Cities in route: {route.Count - 2}"
            : string.Empty;

        InfoOverlay.Visibility = Visibility.Visible;
    }

    // ------------------------------------------------------------------
    // Animations
    // ------------------------------------------------------------------

    /// <summary>
    /// Starts a repeating <c>StrokeDashOffset</c> animation on
    /// <paramref name="shape"/> to produce the marching-ants effect.
    /// </summary>
    private static void StartMarchingAnts(Shape shape)
    {
        var anim = new DoubleAnimation
        {
            From             = 0,
            To               = MarchingAntsPeriodPx,
            Duration         = TimeSpan.FromSeconds(MarchingAntsDuration),
            RepeatBehavior   = RepeatBehavior.Forever,
            EasingFunction   = null   // linear — constant apparent speed
        };
        shape.BeginAnimation(Shape.StrokeDashOffsetProperty, anim);
    }

    /// <summary>
    /// Plays a brief opacity flash on all route elements to celebrate a
    /// successful computation.
    /// </summary>
    private void PlayCompletionFlash()
    {
        var anim = new DoubleAnimation
        {
            From           = FlashFromOpacity,
            To             = 1.0,
            Duration       = TimeSpan.FromMilliseconds(FlashDurationMs),
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };

        foreach (var elem in _routeElements)
            elem.BeginAnimation(OpacityProperty, anim);
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
            Foreground = new SolidColorBrush(Color.FromArgb(0x60, 0xFF, 0xFF, 0xFF))
        };
        tb.Measure(new Size(w, h));
        Canvas.SetLeft(tb, (w - tb.DesiredSize.Width)  / 2);
        Canvas.SetTop (tb, (h - tb.DesiredSize.Height) / 2);
        MapCanvas.Children.Add(tb);
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private static bool IsOriginCity(City city, City? origin) =>
        origin is not null &&
        string.Equals(city.Name,  origin.Name,  StringComparison.OrdinalIgnoreCase) &&
        string.Equals(city.State, origin.State, StringComparison.OrdinalIgnoreCase);

    private static T Freeze<T>(T freezable) where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }
}
