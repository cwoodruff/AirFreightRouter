using System.Collections.ObjectModel;
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

    private readonly RouteSolver _solver  = new();
    private readonly City        _origin  = CityDataService.GetAlbany();

    private CancellationTokenSource? _cts;
    private DispatcherTimer?          _elapsedTimer;
    private DateTime                  _computationStartedAt;

    // -------------------------------------------------------------------------
    // Collections (read-only references; contents change via Add/Clear)
    // -------------------------------------------------------------------------

    /// <summary>
    /// All cities parsed from the last loaded CSV file, with Albany excluded
    /// because it is always the fixed origin/destination.
    /// </summary>
    public ObservableCollection<City> AvailableCities { get; } = [];

    /// <summary>
    /// Delivery cities chosen by the user for the current route computation.
    /// Must contain at least 2 entries before <see cref="StartComputationCommand"/>
    /// becomes enabled.
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
    private RouteResult? _currentResult;

    /// <summary>
    /// Gets the most recent progress snapshot received from the solver,
    /// or <see langword="null"/> when no computation is active.
    /// </summary>
    [ObservableProperty]
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

    // -------------------------------------------------------------------------
    // Constructor
    // -------------------------------------------------------------------------

    /// <summary>Initializes a new <see cref="MainViewModel"/>.</summary>
    public MainViewModel()
    {
        // SelectedCities.Count is part of CanStartComputation's guard, so we must
        // raise NotifyCanExecuteChanged whenever the collection changes.
        SelectedCities.CollectionChanged +=
            (_, _) => StartComputationCommand.NotifyCanExecuteChanged();
    }

    // -------------------------------------------------------------------------
    // LoadDataCommand
    // -------------------------------------------------------------------------

    /// <summary>
    /// Opens a file-picker dialog, parses the chosen CSV with
    /// <see cref="CityDataService.LoadCitiesFromCsv"/>, and populates
    /// <see cref="AvailableCities"/>. Albany is automatically excluded
    /// because it is always the fixed origin.
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

            AvailableCities.Clear();
            SelectedCities.Clear();

            foreach (var city in allCities)
            {
                // Skip Albany — it is always the route origin and destination.
                if (string.Equals(city.Name,  _origin.Name,  StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(city.State, _origin.State, StringComparison.OrdinalIgnoreCase))
                    continue;

                AvailableCities.Add(city);
            }

            IsDataLoaded       = true;
            CurrentResult      = null;
            CurrentProgress    = null;
            ProgressPercent    = 0;
            ElapsedTimeDisplay = "00:00:00";
            StatusMessage      =
                $"Loaded {AvailableCities.Count} cities from \"{System.IO.Path.GetFileName(dialog.FileName)}\". " +
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
        IsComputing     = true;
        CurrentResult   = null;
        CurrentProgress = null;
        ProgressPercent = 0;
        StatusMessage   = "Computing optimal route…";

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
                CurrentResult   = result;
                ProgressPercent = 100;
                StatusMessage   =
                    $"Optimal route found!  " +
                    $"Distance: {result.TotalDistance:F4}°  |  " +
                    $"{result.PermutationsEvaluated:N0} permutations evaluated  |  " +
                    $"Elapsed: {result.ElapsedTime:mm\\:ss\\.ff}";
            }
            else
            {
                ProgressPercent = 0;
                StatusMessage   = "Computation cancelled.";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Unexpected error during computation: {ex.Message}";
        }
        finally
        {
            // Always clean up — even if an exception was thrown or the task was cancelled.
            _elapsedTimer.Stop();
            _elapsedTimer.Tick -= OnElapsedTimerTick;
            _elapsedTimer = null;

            _cts.Dispose();
            _cts = null;

            CurrentProgress = null;
            IsComputing     = false;   // re-enables LoadData / SelectAll / DeselectAll
        }
    }

    /// <summary>
    /// Returns <see langword="true"/> when data is loaded, no computation is active,
    /// and at least 2 delivery cities have been selected.
    /// </summary>
    private bool CanStartComputation() =>
        IsDataLoaded && !IsComputing && SelectedCities.Count >= 2;

    // -------------------------------------------------------------------------
    // CancelComputationCommand
    // -------------------------------------------------------------------------

    /// <summary>
    /// Requests cooperative cancellation of the running solver via the
    /// <see cref="CancellationTokenSource"/>. The solver will detect the
    /// signal at the next permutation boundary and return <see langword="null"/>.
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
    /// Moves all entries from <see cref="AvailableCities"/> into
    /// <see cref="SelectedCities"/>.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanBulkSelect))]
    private void SelectAll()
    {
        SelectedCities.Clear();
        foreach (var city in AvailableCities)
            SelectedCities.Add(city);
    }

    /// <summary>Clears <see cref="SelectedCities"/>.</summary>
    [RelayCommand(CanExecute = nameof(CanBulkSelect))]
    private void DeselectAll() => SelectedCities.Clear();

    /// <summary>
    /// Returns <see langword="true"/> when data has been loaded and no
    /// computation is active.
    /// </summary>
    private bool CanBulkSelect() => IsDataLoaded && !IsComputing;

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Handles a <see cref="RouteProgressInfo"/> snapshot delivered by the solver.
    /// Always invoked on the UI thread because <see cref="Progress{T}"/> captures
    /// the UI synchronisation context at construction time.
    /// </summary>
    private void OnProgressReceived(RouteProgressInfo info)
    {
        CurrentProgress = info;
        ProgressPercent = info.PercentComplete;
        StatusMessage   =
            $"Computing…  {info.PercentComplete:F1}%  |  " +
            $"{info.PermutationsEvaluated:N0} / {info.TotalPermutations:N0} permutations  |  " +
            $"Best so far: {info.CurrentBestDistance:F4}°";
    }

    /// <summary>
    /// Fires every 100 ms while the solver is running to keep
    /// <see cref="ElapsedTimeDisplay"/> current.
    /// </summary>
    private void OnElapsedTimerTick(object? sender, EventArgs e)
    {
        var elapsed = DateTime.Now - _computationStartedAt;
        ElapsedTimeDisplay = elapsed.ToString(@"hh\:mm\:ss");
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
