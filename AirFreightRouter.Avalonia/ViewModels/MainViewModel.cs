using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AirFreightRouter.Models;
using AirFreightRouter.Services;
using AirFreightRouter.Views;

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

    private readonly RouteSolver        _solver        = new();
    private readonly GeneticRouteSolver _gaSolver      = new();
    private readonly City               _defaultOrigin = CityDataService.GetAlbany();
    private          City               _origin;
    private readonly TopLevel           _topLevel;

    private CancellationTokenSource? _cts;
    private DispatcherTimer?          _elapsedTimer;
    private DateTime                  _computationStartedAt;

    private DateTime _lastMapRouteUpdateAt = DateTime.MinValue;
    private const double MapRouteThrottleMs = 500.0;

    // -------------------------------------------------------------------------
    // Collections
    // -------------------------------------------------------------------------

    public ObservableCollection<SelectableCityViewModel> AvailableCities { get; } = [];
    public ObservableCollection<City> SelectedCities { get; } = [];

    // -------------------------------------------------------------------------
    // Observable properties — solver state
    // -------------------------------------------------------------------------

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasResult))]
    [NotifyPropertyChangedFor(nameof(HasCostResult))]
    [NotifyPropertyChangedFor(nameof(RouteDisplay))]
    [NotifyPropertyChangedFor(nameof(ResultDistanceDisplay))]
    [NotifyPropertyChangedFor(nameof(ResultCostDisplay))]
    [NotifyPropertyChangedFor(nameof(ResultCostBreakdownDisplay))]
    [NotifyPropertyChangedFor(nameof(ResultPermutationsDisplay))]
    [NotifyPropertyChangedFor(nameof(ResultElapsedDisplay))]
    private RouteResult? _currentResult;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PermutationsDisplay))]
    private RouteProgressInfo? _currentProgress;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LoadDataCommand))]
    [NotifyCanExecuteChangedFor(nameof(StartComputationCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelComputationCommand))]
    [NotifyCanExecuteChangedFor(nameof(SelectAllCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeselectAllCommand))]
    [NotifyPropertyChangedFor(nameof(ComputeButtonTooltip))]
    private bool _isComputing;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartComputationCommand))]
    [NotifyCanExecuteChangedFor(nameof(SelectAllCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeselectAllCommand))]
    [NotifyPropertyChangedFor(nameof(ComputeButtonTooltip))]
    private bool _isDataLoaded;

    [ObservableProperty]
    private bool _isProgressIndeterminate;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBruteForceSelected))]
    [NotifyPropertyChangedFor(nameof(IsGeneticAlgorithmSelected))]
    [NotifyPropertyChangedFor(nameof(ComputeButtonTooltip))]
    [NotifyPropertyChangedFor(nameof(PermutationsDisplay))]
    [NotifyPropertyChangedFor(nameof(ResultPermutationsDisplay))]
    private SolverMode _selectedSolver = SolverMode.BruteForce;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDistanceObjectiveSelected))]
    [NotifyPropertyChangedFor(nameof(IsCostObjectiveSelected))]
    [NotifyPropertyChangedFor(nameof(ComputeButtonTooltip))]
    private RouteObjective _selectedObjective = RouteObjective.ShortestDistance;

    [ObservableProperty]
    private string _statusMessage = "Load a city CSV file to begin.";

    [ObservableProperty]
    private double _progressPercent;

    [ObservableProperty]
    private string _elapsedTimeDisplay = "00:00:00";

    // -------------------------------------------------------------------------
    // Observable properties — map control bindings
    // -------------------------------------------------------------------------

    [ObservableProperty]
    private IList<City>? _mapRoute;

    [ObservableProperty]
    private string _mapRouteStatusText = string.Empty;

    [ObservableProperty]
    private string _mapBestDistanceText = string.Empty;

    // -------------------------------------------------------------------------
    // Observable properties — results panel
    // -------------------------------------------------------------------------

    [ObservableProperty]
    private bool _hasResultPanel;

    [ObservableProperty]
    private string _resultPanelTitle = string.Empty;

    [ObservableProperty]
    private string _resultTotalDistanceText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasResultPanelCost))]
    private string _resultTotalCostText = string.Empty;

    [ObservableProperty]
    private string _resultPermutationsText = string.Empty;

    [ObservableProperty]
    private string _resultElapsedText = string.Empty;

    [ObservableProperty]
    private IReadOnlyList<RouteLeg> _resultLegs = [];

    [ObservableProperty]
    private string _resultAverageLegText = string.Empty;

    [ObservableProperty]
    private string _resultLongestLegText = string.Empty;

    [ObservableProperty]
    private string _resultShortestLegText = string.Empty;

    // -------------------------------------------------------------------------
    // Computed display properties
    // -------------------------------------------------------------------------

    public bool HasResult => CurrentResult is not null;

    /// <summary>True when the completed run was priced, i.e. it optimised operating cost.</summary>
    public bool HasCostResult => CurrentResult?.Cost is not null;

    /// <summary>
    /// True when the results panel has a cost figure to show — including the partial
    /// figure shown after a cancelled cost run, where <see cref="HasCostResult"/> is false.
    /// </summary>
    public bool HasResultPanelCost => !string.IsNullOrEmpty(ResultTotalCostText);

    public bool IsBruteForceSelected
    {
        get => SelectedSolver == SolverMode.BruteForce;
        set { if (value) SelectedSolver = SolverMode.BruteForce; }
    }

    public bool IsGeneticAlgorithmSelected
    {
        get => SelectedSolver == SolverMode.GeneticAlgorithm;
        set { if (value) SelectedSolver = SolverMode.GeneticAlgorithm; }
    }

    public bool IsDistanceObjectiveSelected
    {
        get => SelectedObjective == RouteObjective.ShortestDistance;
        set { if (value) SelectedObjective = RouteObjective.ShortestDistance; }
    }

    public bool IsCostObjectiveSelected
    {
        get => SelectedObjective == RouteObjective.LowestOperatingCost;
        set { if (value) SelectedObjective = RouteObjective.LowestOperatingCost; }
    }

    public string RouteDisplay =>
        CurrentResult is null
            ? string.Empty
            : string.Join(" → ", CurrentResult.Route.Select(c => c.Name));

    public string ResultDistanceDisplay =>
        CurrentResult is null
            ? string.Empty
            : $"{CurrentResult.TotalDistance:F4}° (degree-distance)";

    public string ResultCostDisplay =>
        CurrentResult?.Cost is not { } cost
            ? string.Empty
            : $"{cost.TotalCost:C0} total operating cost";

    public string ResultCostBreakdownDisplay =>
        CurrentResult?.Cost is not { } cost
            ? string.Empty
            : $"Fuel {cost.FuelCost:C0}  ·  Handling {cost.HandlingFees:C0}  ·  " +
              $"Late {cost.LatePenalty:C0}  ·  " +
              $"{cost.CurfewViolations} curfew ({cost.CurfewPenalty:C0})";

    public string ResultPermutationsDisplay =>
        CurrentResult is null
            ? string.Empty
            : SelectedSolver == SolverMode.GeneticAlgorithm
                ? $"{CurrentResult.PermutationsEvaluated:N0} generations"
                : $"{CurrentResult.PermutationsEvaluated:N0} permutations evaluated";

    public string ResultElapsedDisplay =>
        CurrentResult is null
            ? string.Empty
            : $"Completed in {CurrentResult.ElapsedTime:mm\\:ss\\.ff}";

    public string PermutationsDisplay =>
        CurrentProgress is null
            ? string.Empty
            : SelectedSolver == SolverMode.GeneticAlgorithm
                ? $"Generation {CurrentProgress.PermutationsEvaluated:N0} of {CurrentProgress.TotalPermutations:N0}"
                : $"Evaluated {CurrentProgress.PermutationsEvaluated:N0} of {CurrentProgress.TotalPermutations:N0}";

    public string ComputeButtonTooltip
    {
        get
        {
            if (IsComputing)
                return "Computation in progress — press Escape or click Cancel to stop.";
            if (!IsDataLoaded)
                return "Load a CSV file first (Ctrl+O).";
            if (SelectedCities.Count < 2)
                return "Select at least 2 delivery cities.";

            string goal = SelectedObjective == RouteObjective.LowestOperatingCost
                ? "cheapest-to-operate"
                : "shortest";

            return SelectedSolver == SolverMode.GeneticAlgorithm
                ? $"Find a near-optimal {goal} route through {SelectedCities.Count} cities using Genetic Algorithm (Ctrl+Enter)."
                : $"Find the {goal} route through {SelectedCities.Count} cities (Ctrl+Enter).";
        }
    }

    public string SelectionSummary =>
        $"{SelectedCities.Count} of {AvailableCities.Count} cities selected";

    public IList<City> MapCities
    {
        get
        {
            var list = new List<City> { _origin };
            list.AddRange(AvailableCities.Select(w => w.City));
            return list;
        }
    }

    public City MapOrigin => _origin;

    // -------------------------------------------------------------------------
    // Constructor
    // -------------------------------------------------------------------------

    public MainViewModel(TopLevel topLevel)
    {
        _origin   = _defaultOrigin;
        _topLevel = topLevel;
    }

    // -------------------------------------------------------------------------
    // LoadDataCommand
    // -------------------------------------------------------------------------

    [RelayCommand(CanExecute = nameof(CanLoadData))]
    private async Task LoadDataAsync()
    {
        var files = await _topLevel.StorageProvider.OpenFilePickerAsync(
            new FilePickerOpenOptions
            {
                Title           = "Select City CSV File",
                AllowMultiple   = false,
                FileTypeFilter  =
                [
                    new FilePickerFileType("CSV Files") { Patterns = ["*.csv"] },
                    new FilePickerFileType("All Files") { Patterns = ["*.*"]   }
                ]
            });

        var file = files.FirstOrDefault();
        if (file is null)
            return;

        var filePath = file.TryGetLocalPath();
        var fileName = file.Name;

        try
        {
            StatusMessage = "Loading city data…";

            List<City> allCities;
            try
            {
                if (filePath is not null)
                {
                    allCities = await Task.Run(() => CityDataService.LoadCitiesFromCsv(filePath));
                }
                else
                {
                    // macOS sandbox / security-scoped bookmarks: TryGetLocalPath() returns null.
                    // Use the stream API instead.
                    await using var stream = await file.OpenReadAsync();
                    allCities = await Task.Run(() => CityDataService.LoadCitiesFromStream(stream));
                }
            }
            catch (FileNotFoundException)
            {
                await ShowErrorAsync(
                    "File Not Found",
                    "The selected file could not be found.\n\n" +
                    "It may have been moved or deleted since you opened the dialog.");
                StatusMessage = "File not found.";
                return;
            }
            catch (UnauthorizedAccessException)
            {
                await ShowErrorAsync(
                    "Access Denied",
                    "You do not have permission to read this file.\n\n" +
                    "Try running the application as an administrator, or choose a different file.");
                StatusMessage = "Access denied.";
                return;
            }
            catch (IOException ex)
            {
                await ShowErrorAsync(
                    "File Read Error",
                    $"A file I/O error occurred while reading the CSV:\n\n{ex.Message}");
                StatusMessage = $"I/O error: {ex.Message}";
                return;
            }

            foreach (var old in AvailableCities)
                old.PropertyChanged -= OnCitySelectionChanged;

            AvailableCities.Clear();
            SelectedCities.Clear();

            var albanyInCsv = allCities.FirstOrDefault(c =>
                string.Equals(c.Name,  "Albany", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(c.State, "NY",     StringComparison.OrdinalIgnoreCase));
            _origin = albanyInCsv ?? _defaultOrigin;

            foreach (var city in allCities)
            {
                if (string.Equals(city.Name,  _origin.Name,  StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(city.State, _origin.State, StringComparison.OrdinalIgnoreCase))
                    continue;

                var wrapper = new SelectableCityViewModel(city);
                wrapper.PropertyChanged += OnCitySelectionChanged;
                AvailableCities.Add(wrapper);
            }

            if (AvailableCities.Count < 2)
            {
                await ShowWarningAsync(
                    "Too Few Cities",
                    $"The selected file contains only {AvailableCities.Count} " +
                    (AvailableCities.Count == 1 ? "city" : "cities") +
                    " suitable for delivery (Albany is excluded as the fixed origin).\n\n" +
                    "Please choose a file with at least 2 delivery cities.");
                StatusMessage = "File must contain at least 2 delivery cities.";
                return;
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
            OnPropertyChanged(nameof(MapOrigin));
            OnPropertyChanged(nameof(MapCities));
            RefreshResultPanel(null, false, null);

            StatusMessage =
                $"Loaded {AvailableCities.Count} cities from \"{fileName}\". " +
                (albanyInCsv is not null ? "Using Albany coordinates from file. " : string.Empty) +
                "Select 2 or more delivery destinations, then click Compute Route.";
        }
        catch (Exception ex)
        {
            await ShowErrorAsync("Unexpected Error",
                $"An unexpected error occurred while loading the city data:\n\n{ex.Message}");
            StatusMessage = $"Error loading file: {ex.Message}";
        }
    }

    private bool CanLoadData() => !IsComputing;

    // -------------------------------------------------------------------------
    // StartComputationCommand
    // -------------------------------------------------------------------------

    [RelayCommand(CanExecute = nameof(CanStartComputation))]
    private async Task StartComputationAsync()
    {
        if (SelectedCities.Count < 2)
        {
            StatusMessage = "Please select at least 2 delivery cities.";
            return;
        }

        if (SelectedSolver == SolverMode.BruteForce && SelectedCities.Count > 12)
        {
            long   perms    = RouteSolver.Factorial(SelectedCities.Count);
            double estSecs  = await RouteSolver.EstimateSecondsAsync(SelectedCities.ToList(), _origin);
            string duration = FormatEstimatedDuration(estSecs);

            bool proceed = await ShowYesNoAsync(
                "Warning: Factorial Explosion",
                $"You have selected {SelectedCities.Count} cities.\n\n" +
                $"The brute-force solver must evaluate {perms:N0} permutations.\n" +
                $"Estimated time on this machine: {duration}.\n\n" +
                "Do you want to proceed?");

            if (!proceed)
                return;
        }

        IsComputing             = true;
        IsProgressIndeterminate = true;
        CurrentResult           = null;
        CurrentProgress         = null;
        ProgressPercent         = 0;
        MapRoute                = null;
        MapRouteStatusText      = "Computing…";
        MapBestDistanceText     = string.Empty;
        _lastMapRouteUpdateAt   = DateTime.MinValue;
        StatusMessage           = "Computing optimal route…";
        RefreshResultPanel(null, false, null);

        _cts                  = new CancellationTokenSource();
        _computationStartedAt = DateTime.Now;

        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        timer.Tick += OnElapsedTimerTick;
        timer.Start();
        _elapsedTimer = timer;

        var progress = new Progress<RouteProgressInfo>(OnProgressReceived);
        var deliveryCities = SelectedCities.ToList();

        try
        {
            var result = SelectedSolver == SolverMode.GeneticAlgorithm
                ? await _gaSolver.FindRouteAsync(deliveryCities, _origin, SelectedObjective, progress, _cts.Token)
                : await _solver.FindShortestRouteAsync(deliveryCities, _origin, SelectedObjective, progress, _cts.Token);

            if (result is not null)
            {
                CurrentResult       = result;
                ProgressPercent     = 100;
                MapRoute            = result.Route;
                MapRouteStatusText  = "Complete";
                MapBestDistanceText = result.Cost is { } finalCost
                    ? $"{finalCost.TotalCost:C0}"
                    : $"{result.TotalDistance:F4}°";
                StatusMessage       =
                    $"Optimal route found!  " +
                    (result.Cost is { } c ? $"Cost: {c.TotalCost:C0}  |  " : string.Empty) +
                    $"Distance: {result.TotalDistance:F4}°  |  " +
                    $"{result.PermutationsEvaluated:N0} permutations evaluated  |  " +
                    $"Elapsed: {result.ElapsedTime:mm\\:ss\\.ff}";
                RefreshResultPanel(result.Route, false, result);
            }
            else
            {
                var partialRoute    = MapRoute;
                ProgressPercent     = 0;
                MapRouteStatusText  = "Cancelled (partial)";
                StatusMessage       = "Computation cancelled.";
                RefreshResultPanel(partialRoute, true, null);
            }
        }
        catch (Exception ex)
        {
            MapRouteStatusText = string.Empty;
            StatusMessage      = $"Unexpected error during computation: {ex.Message}";
            RefreshResultPanel(null, false, null);
        }
        finally
        {
            _elapsedTimer!.Stop();
            _elapsedTimer.Tick -= OnElapsedTimerTick;
            _elapsedTimer = null;

            _cts!.Dispose();
            _cts = null;

            IsProgressIndeterminate = false;
            CurrentProgress         = null;
            IsComputing             = false;
        }
    }

    private bool CanStartComputation() =>
        IsDataLoaded && !IsComputing && SelectedCities.Count >= 2;

    // -------------------------------------------------------------------------
    // CancelComputationCommand
    // -------------------------------------------------------------------------

    [RelayCommand(CanExecute = nameof(CanCancelComputation))]
    private void CancelComputation()
    {
        _cts?.Cancel();
        StatusMessage = "Cancellation requested — waiting for solver to stop…";
    }

    private bool CanCancelComputation() => IsComputing;

    // -------------------------------------------------------------------------
    // SelectAllCommand / DeselectAllCommand
    // -------------------------------------------------------------------------

    [RelayCommand(CanExecute = nameof(CanBulkSelect))]
    private void SelectAll()
    {
        foreach (var wrapper in AvailableCities)
            wrapper.IsSelected = true;
    }

    [RelayCommand(CanExecute = nameof(CanBulkSelect))]
    private void DeselectAll()
    {
        foreach (var wrapper in AvailableCities)
            wrapper.IsSelected = false;
    }

    private bool CanBulkSelect() => IsDataLoaded && !IsComputing;

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    private void OnCitySelectionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(SelectableCityViewModel.IsSelected))
            return;

        SelectedCities.Clear();
        foreach (var wrapper in AvailableCities.Where(w => w.IsSelected))
            SelectedCities.Add(wrapper.City);

        StartComputationCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(SelectionSummary));
        OnPropertyChanged(nameof(ComputeButtonTooltip));
    }

    private void OnProgressReceived(RouteProgressInfo info)
    {
        if (IsProgressIndeterminate)
            IsProgressIndeterminate = false;
        CurrentProgress = info;
        ProgressPercent = info.PercentComplete;

        string bestSoFar = info.CurrentBestCost is { } cost
            ? $"{cost:C0}"
            : $"{info.CurrentBestDistance:F4}°";

        StatusMessage = SelectedSolver == SolverMode.GeneticAlgorithm
            ? $"Computing…  {info.PercentComplete:F1}%  |  " +
              $"Generation {info.PermutationsEvaluated:N0} / {info.TotalPermutations:N0}  |  " +
              $"Best so far: {bestSoFar}"
            : $"Computing…  {info.PercentComplete:F1}%  |  " +
              $"{info.PermutationsEvaluated:N0} / {info.TotalPermutations:N0} permutations  |  " +
              $"Best so far: {bestSoFar}";

        var now = DateTime.Now;
        if ((now - _lastMapRouteUpdateAt).TotalMilliseconds >= MapRouteThrottleMs
            && info.CurrentBestRoute is { Count: > 0 })
        {
            var liveRoute = new List<City>(info.CurrentBestRoute.Count + 2) { _origin };
            liveRoute.AddRange(info.CurrentBestRoute);
            liveRoute.Add(_origin);

            MapRoute              = liveRoute;
            MapBestDistanceText   = bestSoFar;
            _lastMapRouteUpdateAt = now;
        }
    }

    private void OnElapsedTimerTick(object? sender, EventArgs e)
    {
        ElapsedTimeDisplay = (DateTime.Now - _computationStartedAt).ToString(@"hh\:mm\:ss");
    }

    private void RefreshResultPanel(IList<City>? route, bool cancelled, RouteResult? result)
    {
        if (route is null || route.Count < 3)
        {
            HasResultPanel          = false;
            ResultPanelTitle        = string.Empty;
            ResultTotalDistanceText = string.Empty;
            ResultTotalCostText     = string.Empty;
            ResultPermutationsText  = string.Empty;
            ResultElapsedText       = string.Empty;
            ResultLegs              = [];
            ResultAverageLegText    = string.Empty;
            ResultLongestLegText    = string.Empty;
            ResultShortestLegText   = string.Empty;
            return;
        }

        bool costMode = SelectedObjective == RouteObjective.LowestOperatingCost;

        HasResultPanel   = true;
        ResultPanelTitle = cancelled
            ? "Computation Cancelled – Best Route So Far"
            : SelectedSolver == SolverMode.GeneticAlgorithm
                ? costMode
                    ? "Near-Optimal Lowest-Cost Route Found (Genetic Algorithm)"
                    : "Near-Optimal Route Found (Genetic Algorithm)"
                : costMode
                    ? "Lowest-Cost Route Found"
                    : "Optimal Route Found";

        if (result is not null)
        {
            ResultTotalDistanceText = $"{result.TotalDistance:F4}°";
            ResultTotalCostText     = result.Cost is { } cost ? $"{cost.TotalCost:C0}" : string.Empty;
            ResultPermutationsText  = SelectedSolver == SolverMode.GeneticAlgorithm
                ? $"{result.PermutationsEvaluated:N0} generations"
                : $"{result.PermutationsEvaluated:N0} permutations";
            ResultElapsedText       = $"{result.ElapsedTime:mm\\:ss\\.ff}";
        }
        else
        {
            double total = 0;
            for (int i = 0; i < route.Count - 1; i++)
                total += route[i].DistanceTo(route[i + 1]);
            ResultTotalDistanceText = $"{total:F4}° (partial)";
            var partialRoute = route as IReadOnlyList<City> ?? route.ToList();
            ResultTotalCostText     = costMode
                ? $"{new RouteCostModel(partialRoute).TotalCost(partialRoute):C0} (partial)"
                : string.Empty;
            ResultPermutationsText  = string.Empty;
            ResultElapsedText       = string.Empty;
        }

        var legs    = new List<RouteLeg>(route.Count - 1);
        double cumDist = 0;
        for (int i = 0; i < route.Count - 1; i++)
        {
            double legDist = route[i].DistanceTo(route[i + 1]);
            cumDist += legDist;
            legs.Add(new RouteLeg
            {
                LegNumber          = i + 1,
                FromCity           = route[i],
                ToCity             = route[i + 1],
                LegDistance        = legDist,
                CumulativeDistance = cumDist,
                IsReturn           = (i == route.Count - 2),
                IsAlternate        = (i % 2 == 1)
            });
        }
        ResultLegs = legs;

        var longestLeg  = legs.MaxBy(l => l.LegDistance)!;
        var shortestLeg = legs.MinBy(l => l.LegDistance)!;
        double avg      = legs.Average(l => l.LegDistance);

        ResultAverageLegText  = $"{avg:F4}°";
        ResultLongestLegText  =
            $"{longestLeg.FromCity.Name} → {longestLeg.ToCity.Name}  ({longestLeg.LegDistance:F4}°)";
        ResultShortestLegText =
            $"{shortestLeg.FromCity.Name} → {shortestLeg.ToCity.Name}  ({shortestLeg.LegDistance:F4}°)";
    }

    private static string FormatEstimatedDuration(double seconds)
    {
        if (seconds == double.MaxValue) return "extremely long (overflow)";
        if (seconds >= 86_400) return $"{seconds / 86_400:F1} days";
        if (seconds >=  3_600) return $"{seconds /  3_600:F1} hours";
        if (seconds >=     60) return $"{seconds /     60:F1} minutes";
        return $"{seconds:F1} seconds";
    }

    // -------------------------------------------------------------------------
    // Dialog helpers
    // -------------------------------------------------------------------------

    private Task ShowErrorAsync(string title, string message) =>
        AppMessageBox.ShowErrorAsync(GetOwnerWindow(), title, message);

    private Task ShowWarningAsync(string title, string message) =>
        AppMessageBox.ShowWarningAsync(GetOwnerWindow(), title, message);

    private async Task<bool> ShowYesNoAsync(string title, string message)
    {
        var result = await AppMessageBox.ShowYesNoAsync(GetOwnerWindow(), title, message);
        return result == MessageBoxResult.Yes;
    }

    private Window GetOwnerWindow() =>
        (_topLevel as Window) ?? throw new InvalidOperationException("TopLevel is not a Window");
}
