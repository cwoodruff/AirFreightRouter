using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Collections;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using AirFreightRouter.Models;
using AirFreightRouter.Services;

namespace AirFreightRouter.Views;

/// <summary>
/// Canvas-based UserControl that renders a 2-D air-freight route map with
/// live in-progress updates, a marching-ants animation while computing, a
/// completion flash, and a semi-transparent info overlay.
/// </summary>
public partial class RouteMapControl : UserControl
{
    // ------------------------------------------------------------------
    // Static brushes (allocated once)
    // ------------------------------------------------------------------

    private static readonly SolidColorBrush BrushGrid =
        new(Color.FromArgb(0x28, 0x55, 0x55, 0x88));

    private static readonly SolidColorBrush BrushRouteGlow =
        new(Color.FromArgb(0x44, 0x7B, 0xBC, 0xFF));

    private static readonly SolidColorBrush BrushRoute =
        new(Color.FromRgb(0x7B, 0xBC, 0xFF));

    private static readonly SolidColorBrush BrushDash =
        new(Color.FromArgb(0xCC, 0x7B, 0xBC, 0xFF));

    private static readonly SolidColorBrush BrushOriginGlow =
        new(Color.FromArgb(0x55, 0xFF, 0xD7, 0x00));

    private static readonly SolidColorBrush BrushOrigin =
        new(Color.FromRgb(0xFF, 0xD7, 0x00));

    private static readonly SolidColorBrush BrushCityGlow =
        new(Color.FromArgb(0x55, 0x4A, 0x9D, 0xFF));

    private static readonly SolidColorBrush BrushCity =
        new(Color.FromRgb(0x4A, 0x9D, 0xFF));

    private static readonly SolidColorBrush BrushLabel =
        new(Colors.White);

    private static readonly SolidColorBrush BrushLabelOrigin =
        new(Color.FromRgb(0xFF, 0xE0, 0x60));

    // Geometry constants
    private const double DotRadius     = 8.0;
    private const double DotGlowRadius = 14.0;
    private const double LabelOffset   = 12.0;
    private const double LabelFontSize = 10.5;
    private const int    GridLines     = 6;

    // Animation constants
    private const double MarchingAntsPeriodPx = 16.0;
    private const double MarchingAntsDuration = 0.7;
    private const double FlashFromOpacity     = 0.35;
    private const double FlashDurationMs      = 300.0;

    // ------------------------------------------------------------------
    // Styled Properties
    // ------------------------------------------------------------------

    public static readonly StyledProperty<IList<City>?> CitiesProperty =
        AvaloniaProperty.Register<RouteMapControl, IList<City>?>(nameof(Cities));

    public static readonly StyledProperty<IList<City>?> RouteProperty =
        AvaloniaProperty.Register<RouteMapControl, IList<City>?>(nameof(Route));

    public static readonly StyledProperty<City?> OriginProperty =
        AvaloniaProperty.Register<RouteMapControl, City?>(nameof(Origin));

    public static readonly StyledProperty<bool> IsRouteInProgressProperty =
        AvaloniaProperty.Register<RouteMapControl, bool>(nameof(IsRouteInProgress));

    public static readonly StyledProperty<string> RouteStatusTextProperty =
        AvaloniaProperty.Register<RouteMapControl, string>(nameof(RouteStatusText), string.Empty);

    public static readonly StyledProperty<string> BestDistanceTextProperty =
        AvaloniaProperty.Register<RouteMapControl, string>(nameof(BestDistanceText), string.Empty);

    // CLR wrappers
    public IList<City>? Cities
    {
        get => GetValue(CitiesProperty);
        set => SetValue(CitiesProperty, value);
    }

    public IList<City>? Route
    {
        get => GetValue(RouteProperty);
        set => SetValue(RouteProperty, value);
    }

    public City? Origin
    {
        get => GetValue(OriginProperty);
        set => SetValue(OriginProperty, value);
    }

    public bool IsRouteInProgress
    {
        get => GetValue(IsRouteInProgressProperty);
        set => SetValue(IsRouteInProgressProperty, value);
    }

    public string RouteStatusText
    {
        get => GetValue(RouteStatusTextProperty);
        set => SetValue(RouteStatusTextProperty, value);
    }

    public string BestDistanceText
    {
        get => GetValue(BestDistanceTextProperty);
        set => SetValue(BestDistanceTextProperty, value);
    }

    // ------------------------------------------------------------------
    // Constructor
    // ------------------------------------------------------------------

    public RouteMapControl()
    {
        InitializeComponent();
        MapCanvas.SizeChanged += MapCanvas_SizeChanged;
    }

    // ------------------------------------------------------------------
    // Property change handler
    // ------------------------------------------------------------------

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == CitiesProperty
            || change.Property == RouteProperty
            || change.Property == OriginProperty)
        {
            Redraw();
            UpdateOverlay();
        }
        else if (change.Property == IsRouteInProgressProperty)
        {
            bool wasInProgress = change.GetOldValue<bool>();
            bool nowInProgress = change.GetNewValue<bool>();

            Redraw();
            UpdateOverlay();

            if (wasInProgress && !nowInProgress
                && _routeElements.Count > 0
                && RouteStatusText == "Complete")
            {
                PlayCompletionFlash();
            }
        }
        else if (change.Property == RouteStatusTextProperty
              || change.Property == BestDistanceTextProperty)
        {
            UpdateOverlay();
        }
    }

    // ------------------------------------------------------------------
    // Size changed
    // ------------------------------------------------------------------

    private void MapCanvas_SizeChanged(object? sender, SizeChangedEventArgs e)
    {
        Redraw();
    }

    // ------------------------------------------------------------------
    // Core drawing state
    // ------------------------------------------------------------------

    private readonly MapRenderer    _renderer        = new();
    private readonly List<Control>  _routeElements   = [];
    private readonly List<Ellipse>  _cityDotElements = [];
    private          bool           _hadCities;

    private void Redraw()
    {
        StopAnimations();
        MapCanvas.Children.Clear();
        _routeElements.Clear();
        _cityDotElements.Clear();

        double w = MapCanvas.Bounds.Width;
        double h = MapCanvas.Bounds.Height;

        if (w < 10 || h < 10) return;

        var cities = Cities;
        if (cities == null || cities.Count == 0)
        {
            _hadCities = false;
            DrawEmptyPlaceholder(w, h);
            return;
        }

        bool isFirstLoad = !_hadCities;
        _hadCities = true;

        _renderer.Initialise(cities, w, h);

        DrawGrid(w, h);
        DrawRoute();
        DrawCityDots(cities);
        DrawLabels(cities, w, h);

        if (isFirstLoad)
            PlayFadeIn();
        if (IsRouteInProgress)
            StartAnimations();
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
                StartPoint      = new Point(x, 0),
                EndPoint        = new Point(x, h),
                Stroke          = BrushGrid,
                StrokeThickness = 1
            });
        }

        double yStep = h / (GridLines + 1);
        for (int i = 1; i <= GridLines; i++)
        {
            double y = i * yStep;
            MapCanvas.Children.Add(new Line
            {
                StartPoint      = new Point(0, y),
                EndPoint        = new Point(w, y),
                Stroke          = BrushGrid,
                StrokeThickness = 1
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

        int  n          = route.Count;
        bool inProgress = IsRouteInProgress;

        // --- glow layer ---
        var glowPoints = new List<Point>();
        for (int k = 0; k < n - 1; k++)
            glowPoints.Add(_renderer.Project(route[k]));

        var glowLine = new Polyline
        {
            Points          = new AvaloniaList<Point>(glowPoints),
            Stroke          = BrushRouteGlow,
            StrokeThickness = inProgress ? 10 : 8,
            StrokeJoin      = PenLineJoin.Round,
            StrokeLineCap   = PenLineCap.Round,
            Opacity         = inProgress ? 0.55 : 1.0
        };
        MapCanvas.Children.Add(glowLine);
        _routeElements.Add(glowLine);

        // --- main outbound polyline ---
        var mainLine = new Polyline
        {
            Points          = new AvaloniaList<Point>(glowPoints),
            Stroke          = BrushRoute,
            StrokeThickness = 2,
            StrokeJoin      = PenLineJoin.Round,
            StrokeLineCap   = PenLineCap.Round
        };

        if (inProgress)
        {
            mainLine.StrokeDashArray = new AvaloniaList<double>([10, 6]);
            _marchingAntsTarget = mainLine;
        }

        MapCanvas.Children.Add(mainLine);
        _routeElements.Add(mainLine);

        // --- return leg (always dashed, static) ---
        var p1 = _renderer.Project(route[n - 2]);
        var p2 = _renderer.Project(route[n - 1]);
        var returnLeg = new Line
        {
            StartPoint      = p1,
            EndPoint        = p2,
            Stroke          = BrushDash,
            StrokeThickness = inProgress ? 1.5 : 2,
            StrokeDashArray = new AvaloniaList<double>([10, 6]),
            StrokeLineCap   = PenLineCap.Round,
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
            AddEllipse(pt, DotRadius,     isOrigin ? BrushOrigin     : BrushCity, trackAsDot: true);
        }
    }

    private void AddEllipse(Point centre, double radius, IBrush fill, bool trackAsDot = false)
    {
        var e = new Ellipse { Width = radius * 2, Height = radius * 2, Fill = fill };

        if (trackAsDot)
        {
            e.RenderTransformOrigin = RelativePoint.Center;
            e.RenderTransform       = new ScaleTransform(1.0, 1.0);
            _cityDotElements.Add(e);
        }

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

            double xOffset = pt.X > canvasWidth  - 80 ? -(LabelOffset + 50) : LabelOffset;
            double yOffset = pt.Y > canvasHeight - 30 ? -(LabelOffset + 14) : LabelOffset;

            var tb = new TextBlock
            {
                Text       = city.Name,
                FontSize   = LabelFontSize,
                FontWeight = isOrigin ? FontWeight.SemiBold : FontWeight.Normal,
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

        if (string.IsNullOrEmpty(status) && string.IsNullOrEmpty(distance))
        {
            InfoOverlay.IsVisible = false;
            return;
        }

        TbStatus.Text   = status;
        TbDistance.Text = string.IsNullOrEmpty(distance)
            ? string.Empty
            : $"Best distance: {distance}";
        TbCities.Text   = route is { Count: > 2 }
            ? $"Cities in route: {route.Count - 2}"
            : string.Empty;

        InfoOverlay.IsVisible = true;
    }

    // ------------------------------------------------------------------
    // Animations
    // ------------------------------------------------------------------

    private DispatcherTimer? _animTimer;
    private double           _pulsePhase;
    private double           _dashOffset;
    private Polyline?        _marchingAntsTarget;

    private void StartAnimations()
    {
        _pulsePhase = 0;
        _dashOffset = 0;

        _animTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _animTimer.Tick += OnAnimTick;
        _animTimer.Start();
    }

    private void StopAnimations()
    {
        _animTimer?.Stop();
        _animTimer = null;
        _marchingAntsTarget = null;

        foreach (var dot in _cityDotElements)
        {
            if (dot.RenderTransform is ScaleTransform st)
            {
                st.ScaleX = 1.0;
                st.ScaleY = 1.0;
            }
        }
    }

    private void OnAnimTick(object? sender, EventArgs e)
    {
        const double pulsePeriodMs  = 3000.0; // full auto-reverse period (1500*2)
        const double dt             = 16.0;

        // Dot pulse: smooth sine from 1.0 to 1.18
        _pulsePhase += dt / pulsePeriodMs * 2.0 * Math.PI;
        double scale = 1.0 + 0.09 * (1.0 - Math.Cos(_pulsePhase));

        foreach (var dot in _cityDotElements)
        {
            if (dot.RenderTransform is ScaleTransform st)
            {
                st.ScaleX = scale;
                st.ScaleY = scale;
            }
        }

        // Marching ants: advance dash offset
        _dashOffset += MarchingAntsPeriodPx * dt / (MarchingAntsDuration * 1000.0);
        if (_dashOffset >= MarchingAntsPeriodPx)
            _dashOffset -= MarchingAntsPeriodPx;

        if (_marchingAntsTarget is not null)
            _marchingAntsTarget.StrokeDashOffset = _dashOffset;
    }

    /// <summary>Fades the map canvas from transparent to opaque (400ms).</summary>
    private async void PlayFadeIn()
    {
        MapCanvas.Opacity = 0;
        var anim = new Animation
        {
            Duration = TimeSpan.FromMilliseconds(400),
            Easing   = new CubicEaseOut(),
            FillMode = FillMode.Forward,
            Children =
            {
                new KeyFrame
                {
                    Cue = new Cue(0.0),
                    Setters = { new Setter(OpacityProperty, 0.0) }
                },
                new KeyFrame
                {
                    Cue = new Cue(1.0),
                    Setters = { new Setter(OpacityProperty, 1.0) }
                }
            }
        };
        await anim.RunAsync(MapCanvas);
    }

    /// <summary>Flashes route elements from dim to full opacity (300ms).</summary>
    private async void PlayCompletionFlash()
    {
        var tasks = _routeElements.Select(elem =>
        {
            var anim = new Animation
            {
                Duration = TimeSpan.FromMilliseconds(FlashDurationMs),
                Easing   = new QuadraticEaseOut(),
                FillMode = FillMode.Forward,
                Children =
                {
                    new KeyFrame
                    {
                        Cue = new Cue(0.0),
                        Setters = { new Setter(OpacityProperty, FlashFromOpacity) }
                    },
                    new KeyFrame
                    {
                        Cue = new Cue(1.0),
                        Setters = { new Setter(OpacityProperty, 1.0) }
                    }
                }
            };
            return anim.RunAsync(elem);
        });
        await Task.WhenAll(tasks);
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

        // Measure to centre the text
        tb.Measure(new Size(w, h));
        double tw = tb.DesiredSize.Width;
        double th = tb.DesiredSize.Height;

        Canvas.SetLeft(tb, (w - tw) / 2);
        Canvas.SetTop (tb, (h - th) / 2);
        MapCanvas.Children.Add(tb);
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private static bool IsOriginCity(City city, City? origin) =>
        origin is not null &&
        string.Equals(city.Name,  origin.Name,  StringComparison.OrdinalIgnoreCase) &&
        string.Equals(city.State, origin.State, StringComparison.OrdinalIgnoreCase);
}
