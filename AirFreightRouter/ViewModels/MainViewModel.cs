using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using AirFreightRouter.Models;
using AirFreightRouter.Services;

namespace AirFreightRouter.ViewModels;

/// <summary>
/// Primary ViewModel for the main window. Coordinates city loading,
/// route computation, progress reporting, and cancellation.
/// </summary>
public partial class MainViewModel : ObservableObject
{
    // -------------------------------------------------------------------------
    // Private state
    // -------------------------------------------------------------------------

    private readonly RouteSolver _solver = new();
    private readonly City        _origin = CityDataService.GetAlbany();

    private CancellationTokenSource? _cts;
    private DispatcherTimer?          _elapsedTimer;
    private DateTime                  _computationStartedAt;

    /// <summary>
    /// Tracks the last time a live map-route update was pushed to
    /// <see cref="MapRoute"/> so that we can throttle redraws.
    /// </summary>
    private DateTime _lastMapRouteUpdateAt = DateTime.MinValue;
    private const double MapRouteThrottleMs = 500.0;

    // -------------------------------------------------------------------------
    // Collections (read-only references; contents change via Add / Clear)
    // -------------------------------------------------------------------------

    /// <summary>
    /// All cities parsed from the last loaded CSV file, wrapped so the view can
    /// bind a <c>CheckBox.IsChecked</c> to each item. Albany is excluded because
    /// it is always the fixed origin/destination.
    /// </summary>
    public ObservableCollection<SelectableCityViewModel> AvailableCities { get; } = [];

    /// <summary>
    /// Delivery cities currently checked by the user. Rebuilt from
    /// <see cref="AvailableCities"/> whenever any <c>IsSelected</c> flag changes.
    /// Must contain ≥ 2 entries before <see cref="StartComputationCommand"/> enables.
    /// </summary>
    public ObservableCollection<City> SelectedCities { get; } = [];

    // -------------------------------------------------------------------------
    // Observable properties
    // -------------------------------------------------------------------------

    /// <summary>
    /// Gets the result of the most recently completed route search,
    /// or <see langword="null"/> if no search has finished yet.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasResult))]
    [NotifyPropertyChangedFor(nameof(RouteDisplay))]
    [NotifyPropertyChangedFor(nameof(ResultDistanceDisplay))]
    [NotifyPropertyChangedFor(nameof(ResultPermutationsDisplay))]
    [NotifyPropertyChangedFor(nameof(ResultElapsedDisplay))]
    private RouteResult? _currentResult;

    /// <summary>
    /// Gets the most recent progress snapshot received from the solver,
    /// or <see langword="null"/> when no computation is active.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PermutationsDisplay))]
    private RouteProgressInfo? _currentProgress;

    /// <summary>
    /// Gets whether the brute-force solver is currently running.
    /// Drives <c>CanExecute</c> for all commands.
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LoadDataCommand))]
    [NotifyCanExecuteChangedFor(nameof(StartComputationCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelComputationCommand))]
    [NotifyCanExecuteChangedFor(nameof(SelectAllCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeselectAllCommand))]
    private bool _isComputing;

    /// <summary>
    /// Gets whether city data has been successfully loaded from a CSV file.
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartComputationCommand))]
    [NotifyCanExecuteChangedFor(nameof(SelectAllCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeselectAllCommand))]
    private bool _isDataLoaded;

    /// <summary>Gets a human-readable description of the current application state.</summary>
    [ObservableProperty]
    private string _statusMessage = "Load a city CSV file to begin.";

    /// <summary>Gets the solver's completion percentage in the range [0, 100].</summary>
    [ObservableProperty]
    private double _progressPercent;

    /// <summary>
    /// Gets the wall-clock time elapsed since the current (or most recent)
    /// computation started, formatted as <c>hh:mm:ss</c>.
    /// </summary>
    [ObservableProperty]
    private string _elapsedTimeDisplay = "00:00:00";

    // ---- Map-specific observable properties ----------------------------------

    /// <summary>
    /// Gets the route currently displayed on the map.  Updated in real-time
    /// (at most every 500 ms) while a computation is running; set to the
    /// final optimal route on completion; retained as the last partial route
    /// when the user cancels.
    /// </summary>
    [ObservableProperty]
    private IList<City>? _mapRoute;

    /// <summary>
    /// Gets the one-line status label shown in the map overlay:
    /// "Computing…", "Complete", or "Cancelled (partial)".
    /// </summary>
    [ObservableProperty]
    private string _mapRouteStatusText = string.Empty;

    /// <summary>
    /// Gets the best distance found so far, formatted as "X.XXXX°", for
    /// display in the map overlay while a computation is running or after
    /// it completes.
    /// </summary>
    [ObservableProperty]
    private string _mapBestDistanceText = string.Empty;

    // -------------------------------------------------------------------------
    // Computed display properties (no backing field — raised manually)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Gets <see langword="true"/> when <see cref="CurrentResult"/> holds a
    /// completed route, enabling the result overlay in the view.
    /// </summary>
    public bool HasResult => CurrentResult is not null;

    /// <summary>
    /// Gets a human-readable route string such as
    /// <c>"Albany → Boston → New York → Albany"</c>.
    /// </summary>
    public string RouteDisplay =>
        CurrentResult is null
            ? string.Empty
            : string.Join(" → ", CurrentResult.Route.Select(c => c.Name));

    /// <summary>Gets the optimal distance formatted for display.</summary>
    public string ResultDistanceDisplay =>
        CurrentResult is null
            ? string.Empty
            : $"{CurrentResult.TotalDistance:F4}° (degree-distance)";

    /// <summary>Gets the permutation count formatted for display.</summary>
    public string ResultPermutationsDisplay =>
        CurrentResult is null
            ? string.Empty
            : $"{CurrentResult.PermutationsEvaluated:N0} permutations evaluated";

    /// <summary>Gets the elapsed solver time formatted for display.</summary>
    public string ResultElapsedDisplay =>
        CurrentResult is null
            ? string.Empty
            : $"Completed in {CurrentResult.ElapsedTime:mm\\:ss\\.ff}";

    /// <summary>
    /// Gets a status string showing how many permutations have been evaluated,
    /// or an empty string when no computation is active.
    /// </summary>
    public string PermutationsDisplay =>
        CurrentProgress is null
            ? string.Empty
            : $"Evaluated {CurrentProgress.PermutationsEvaluated:N0} of {CurrentProgress.TotalPermutations:N0}";

    /// <summary>
    /// Gets a summary such as <c>"3 of 11 cities selected"</c>
    /// for display below the city list.
    /// </summary>
    public string SelectionSummary =>
        $"{SelectedCities.Count} of {AvailableCities.Count} cities selected";

    /// <summary>
    /// Gets all cities for map display: the fixed origin followed by every
    /// city currently loaded from the CSV file.
    /// </summary>
    public IList<City> MapCities
    {
        get
        {
            var list = new List<City> { _origin };
            list.AddRange(AvailableCities.Select(w => w.City));
            return list;
        }
    }

    /// <summary>Gets the fixed origin city passed to the map control.</summary>
    public City MapOrigin => _origin;

    // -------------------------------------------------------------------------
    // Constructor
    // -------------------------------------------------------------------------

    /// <summary>Initialises a new <see cref="MainViewModel"/>.</summary>
    public MainViewModel() { }

    // -------------------------------------------------------------------------
    // LoadDataCommand
    // -------------------------------------------------------------------------

    /// <summary>
    /// Opens a file-picker dialog, parses the chosen CSV with
    /// <see cref="CityDataService.LoadCitiesFromCsv"/>, and populates
    /// <see cref="AvailableCities"/> with <see cref="SelectableCityViewModel"/>
    /// wrappers. Albany is automatically excluded because it is always the
    /// fixed origin. Each wrapper's <c>PropertyChanged</c> is wired to
    /// <see cref="OnCitySelectionChanged"/> so that <see cref="SelectedCities"/>
    /// stays in sync without any additional plumbing in the view.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanLoadData))]
    private async Task LoadDataAsync()
    {
        var dialog = new OpenFileDialog
        {
            Title      = "Select City CSV File",
            Filter     = "CSV Files (*.csv)|*.csv|All Files (*.*)|*.*",
            DefaultExt = ".csv"
        };

        if (dialog.ShowDialog() != true)
            return;

        try
        {
            StatusMessage = "Loading city data…";

            // Parse on a background thread so the UI stays responsive.
            var allCities = await Task.Run(
                () => CityDataService.LoadCitiesFromCsv(dialog.FileName));

            // Unsubscribe from old wrappers to avoid memory leaks.
            foreach (var old in AvailableCities)
                old.PropertyChanged -= OnCitySelectionChanged;

            AvailableCities.Clear();
            SelectedCities.Clear();

            foreach (var city in allCities)
            {
                // Skip Albany — it is always the route origin and destination.
                if (string.Equals(city.Name,  _origin.Name,  StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(city.State, _origin.State, StringComparison.OrdinalIgnoreCase))
                    continue;

                var wrapper = new SelectableCityViewModel(city);
                wrapper.PropertyChanged += OnCitySelectionChanged;
                AvailableCities.Add(wrapper);
            }

            IsDataLoaded          = true;
            CurrentResult         = null;
            CurrentProgress       = null;
            ProgressPercent       = 0;
            ElapsedTimeDisplay    = "00:00:00";
            MapRoute              = null;
            MapRouteStatusText    = string.Empty;
            MapBestDistanceText   = string.Empty;
            _lastMapRouteUpdateAt = DateTime.MinValue;

            OnPropertyChanged(nameof(SelectionSummary));
            OnPropertyChanged(nameof(MapCities));

            StatusMessage =
                $"Loaded {AvailableCities.Count} cities from \"{Path.GetFileName(dialog.FileName)}\". " +
                "Select 2 or more delivery destinations, then click Compute Route.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error loading file: {ex.Message}";
        }
    }

    /// <summary>Returns <see langword="true"/> when no computation is active.</summary>
    private bool CanLoadData() => !IsComputing;

    // -------------------------------------------------------------------------
    // StartComputationCommand
    // -------------------------------------------------------------------------

    /// <summary>
    /// Validates the selection, optionally warns the user about factorial
    /// explosion for large inputs, then starts the brute-force solver on a
    /// background thread. Progress is marshalled back to the UI thread via
    /// <see cref="Progress{T}"/>. A <see cref="DispatcherTimer"/> updates
    /// <see cref="ElapsedTimeDisplay"/> every 100 ms while the solver runs.
    /// Live map updates are throttled to at most one redraw every 500 ms.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanStartComputation))]
    private async Task StartComputationAsync()
    {
        // Defensive guard — CanExecute should prevent reaching this with < 2 cities.
        if (SelectedCities.Count < 2)
        {
            StatusMessage = "Please select at least 2 delivery cities.";
            return;
        }

        // Warn the user when the search space grows large enough to be slow.
        if (SelectedCities.Count > 12)
        {
            long perms = ComputeFactorial(SelectedCities.Count);
            var answer = MessageBox.Show(
                $"You have selected {SelectedCities.Count} cities.\n\n" +
                $"The brute-force solver must evaluate {perms:N0} permutations. " +
                "This may take a very long time.\n\n" +
                "Do you want to proceed?",
                "Warning: Factorial Explosion",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (answer != MessageBoxResult.Yes)
                return;
        }

        // --- Initialise state ---
        IsComputing           = true;
        CurrentResult         = null;
        CurrentProgress       = null;
        ProgressPercent       = 0;
        MapRoute              = null;
        MapRouteStatusText    = "Computing…";
        MapBestDistanceText   = string.Empty;
        _lastMapRouteUpdateAt = DateTime.MinValue;
        StatusMessage         = "Computing optimal route…";

        _cts                  = new CancellationTokenSource();
        _computationStartedAt = DateTime.Now;

        // DispatcherTimer ticks on the UI thread — safe to write observable properties.
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        timer.Tick += OnElapsedTimerTick;
        timer.Start();
        _elapsedTimer = timer;

        // Progress<T> captures the current SynchronizationContext (UI thread) at
        // construction time, so OnProgressReceived is always called on the UI thread.
        var progress = new Progress<RouteProgressInfo>(OnProgressReceived);

        // Snapshot the selection so UI changes mid-run don't affect the solver.
        var deliveryCities = SelectedCities.ToList();

        // --- Run solver ---
        try
        {
            var result = await _solver.FindShortestRouteAsync(
                deliveryCities, _origin, progress, _cts.Token);

            if (result is not null)
            {
                CurrentResult       = result;
                ProgressPercent     = 100;
                MapRoute            = result.Route;
                MapRouteStatusText  = "Complete";
                MapBestDistanceText = $"{result.TotalDistance:F4}°";
                StatusMessage       =
                    $"Optimal route found!  " +
                    $"Distance: {result.TotalDistance:F4}°  |  " +
                    $"{result.PermutationsEvaluated:N0} permutations evaluated  |  " +
                    $"Elapsed: {result.ElapsedTime:mm\\:ss\\.ff}";
            }
            else
            {
                // Cancelled — keep MapRoute as the last partial route found;
                // update status so the map overlay shows the "(partial)" label.
                ProgressPercent    = 0;
                MapRouteStatusText = "Cancelled (partial)";
                StatusMessage      = "Computation cancelled.";
            }
        }
        catch (Exception ex)
        {
            MapRouteStatusText = string.Empty;
            StatusMessage      = $"Unexpected error during computation: {ex.Message}";
        }
        finally
        {
            // Always clean up — even on exception or cancellation.
            _elapsedTimer.Stop();
            _elapsedTimer.Tick -= OnElapsedTimerTick;
            _elapsedTimer = null;

            _cts.Dispose();
            _cts = null;

            CurrentProgress = null;
            IsComputing     = false;
        }
    }

    /// <summary>
    /// Returns <see langword="true"/> when data is loaded, no computation is
    /// active, and at least 2 delivery cities have been selected.
    /// </summary>
    private bool CanStartComputation() =>
        IsDataLoaded && !IsComputing && SelectedCities.Count >= 2;

    // -------------------------------------------------------------------------
    // CancelComputationCommand
    // -------------------------------------------------------------------------

    /// <summary>
    /// Requests cooperative cancellation of the running solver via the
    /// <see cref="CancellationTokenSource"/>. The solver detects the signal at
    /// the next permutation boundary and returns <see langword="null"/>.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanCancelComputation))]
    private void CancelComputation()
    {
        _cts?.Cancel();
        StatusMessage = "Cancellation requested — waiting for solver to stop…";
    }

    /// <summary>Returns <see langword="true"/> only while a computation is active.</summary>
    private bool CanCancelComputation() => IsComputing;

    // -------------------------------------------------------------------------
    // SelectAllCommand / DeselectAllCommand
    // -------------------------------------------------------------------------

    /// <summary>
    /// Sets <c>IsSelected = true</c> on every wrapper in <see cref="AvailableCities"/>,
    /// which cascades via <see cref="OnCitySelectionChanged"/> to rebuild
    /// <see cref="SelectedCities"/>.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanBulkSelect))]
    private void SelectAll()
    {
        foreach (var wrapper in AvailableCities)
            wrapper.IsSelected = true;
    }

    /// <summary>
    /// Sets <c>IsSelected = false</c> on every wrapper, clearing
    /// <see cref="SelectedCities"/>.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanBulkSelect))]
    private void DeselectAll()
    {
        foreach (var wrapper in AvailableCities)
            wrapper.IsSelected = false;
    }

    /// <summary>
    /// Returns <see langword="true"/> when data has been loaded and no
    /// computation is active.
    /// </summary>
    private bool CanBulkSelect() => IsDataLoaded && !IsComputing;

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Called whenever any <see cref="SelectableCityViewModel.IsSelected"/>
    /// property changes. Rebuilds <see cref="SelectedCities"/> from the checked
    /// wrappers, then refreshes all dependent UI state.
    /// </summary>
    private void OnCitySelectionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(SelectableCityViewModel.IsSelected))
            return;

        SelectedCities.Clear();
        foreach (var wrapper in AvailableCities.Where(w => w.IsSelected))
            SelectedCities.Add(wrapper.City);

        StartComputationCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(SelectionSummary));
    }

    /// <summary>
    /// Handles a <see cref="RouteProgressInfo"/> snapshot delivered by the solver.
    /// Always invoked on the UI thread because <see cref="Progress{T}"/> captures
    /// the UI synchronisation context at construction time.
    /// Map-route updates are throttled: at most one live redraw every 500 ms.
    /// </summary>
    private void OnProgressReceived(RouteProgressInfo info)
    {
        CurrentProgress = info;
        ProgressPercent = info.PercentComplete;
        StatusMessage   =
            $"Computing…  {info.PercentComplete:F1}%  |  " +
            $"{info.PermutationsEvaluated:N0} / {info.TotalPermutations:N0} permutations  |  " +
            $"Best so far: {info.CurrentBestDistance:F4}°";

        // Throttle live map updates to ≤ one redraw per 500 ms.
        var now = DateTime.Now;
        if ((now - _lastMapRouteUpdateAt).TotalMilliseconds >= MapRouteThrottleMs
            && info.CurrentBestRoute is { Count: > 0 })
        {
            // Wrap the delivery-city permutation with the fixed origin at both ends
            // to produce the same [origin, c1, …, cN, origin] shape the map expects.
            var liveRoute = new List<City>(info.CurrentBestRoute.Count + 2) { _origin };
            liveRoute.AddRange(info.CurrentBestRoute);
            liveRoute.Add(_origin);

            MapRoute            = liveRoute;
            MapBestDistanceText = $"{info.CurrentBestDistance:F4}°";
            _lastMapRouteUpdateAt = now;
        }
    }

    /// <summary>
    /// Fires every 100 ms while the solver is running to keep
    /// <see cref="ElapsedTimeDisplay"/> current.
    /// </summary>
    private void OnElapsedTimerTick(object? sender, EventArgs e)
    {
        ElapsedTimeDisplay = (DateTime.Now - _computationStartedAt).ToString(@"hh\:mm\:ss");
    }

    /// <summary>
    /// Computes n! as a <see langword="long"/>, used only for the
    /// "factorial explosion" warning message.
    /// </summary>
    private static long ComputeFactorial(int n)
    {
        if (n <= 1) return 1L;
        long result = 1L;
        for (int k = 2; k <= n; k++) result *= k;
        return result;
    }
}
