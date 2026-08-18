# SkyRoute Express – Air Freight Route Optimizer

A cross-platform desktop application that computes the optimal round-trip air-freight
delivery route from Albany, NY through a user-selected set of cities — either the
globally shortest route via a guaranteed brute-force permutation search, or a
near-optimal one via a genetic algorithm.

![Screenshot](docs/screenshot.png)

---

## Prerequisites

| Requirement | Version |
|---|---|
| .NET SDK | 10.0 or later |
| Operating System | Windows · macOS · Linux |
| IDE (optional) | Visual Studio 2022 · Rider · VS Code + C# Dev Kit |

> **Note:** The application targets `net10.0` and uses Avalonia, so it builds and runs
> on Windows, macOS, and Linux.

---

## Getting Started

### Clone

```bash
git clone <repository-url>
cd AirFreightRouter
```

### Build

```bash
dotnet build
```

### Run

```bash
dotnet run --project AirFreightRouter.Avalonia
```

### Run Tests

```bash
dotnet test
```

All 28 unit tests should pass with no failures.

---

## Usage

1. Click **Load City Data** (or press `Ctrl+O`) and select a CSV file.
2. Check the delivery cities you want to visit in the list on the left.
3. Click **Compute Shortest Route** (or press `Ctrl+Enter`).
4. The optimal route appears on the map and in the itinerary panel below.
5. Press `Escape` or click **Cancel Computation** to stop a running search early.
   A partial "best so far" result is retained for inspection.

A bundled sample file is provided at `AirFreightRouter.Avalonia/Data/TestCities.csv`
(12 north-eastern US cities including Albany).

---

## Architecture

### Pattern: MVVM

The application follows the Model-View-ViewModel pattern enforced by
**CommunityToolkit.Mvvm 8.4.0**.

```
┌─────────────────────────────────────────────────────────┐
│  View (Avalonia)                                         │
│  MainWindow.axaml       – main shell, custom title bar   │
│  RouteMapControl.axaml  – canvas-based interactive map   │
│  AboutWindow.axaml      – modal "About" dialog           │
└───────────────┬─────────────────────────────────────────┘
                │  Data-binding / Commands
┌───────────────▼─────────────────────────────────────────┐
│  ViewModel                                               │
│  MainViewModel          – city loading, computation,     │
│                           progress, cancellation         │
│  SelectableCityViewModel– thin wrapper adding IsSelected │
│                           to City for CheckBox binding   │
└───────────────┬─────────────────────────────────────────┘
                │  Plain C# calls
┌───────────────▼─────────────────────────────────────────┐
│  Model                                                   │
│  Coordinates            – lat/lon + DistanceTo()         │
│  City : Coordinates     – adds Name, State               │
│  RouteResult            – optimal route + statistics     │
│  RouteProgressInfo      – live progress snapshot         │
│  RouteLeg               – per-segment display data       │
└───────────────┬─────────────────────────────────────────┘
                │
┌───────────────▼─────────────────────────────────────────┐
│  Services                                                │
│  CityDataService        – CSV parsing, Albany factory    │
│  RouteSolver            – Heap's algorithm solver        │
│  GeneticRouteSolver     – population-based heuristic     │
│  RouteCostModel         – operating-cost fitness         │
│  MapRenderer            – geo → canvas projection        │
└─────────────────────────────────────────────────────────┘
```

### Class Inheritance

```
Coordinates
│   Latitude  : double
│   Longitude : double
│   DistanceTo(Coordinates) : double   ← Euclidean degree-distance
│   Equals / GetHashCode / ToString
│
└── City
        Name  : string
        State : string
        .ctor(name, state, lat, lon)
        ToString() → "Name, State"
```

`City` inherits geographic computation from `Coordinates` and adds the
human-readable identifiers required by the UI and the solver.

### Project Layout

```
AirFreightRouter.sln
├── AirFreightRouter.Avalonia/         Main application (Avalonia, net10.0)
│   ├── Data/
│   │   ├── TestCities.csv             Bundled sample dataset (12 US cities)
│   │   ├── tsp_cities_100.csv         Larger TSP dataset (100 cities)
│   │   └── tsp_cities_1104.csv        Larger TSP dataset (1,104 cities)
│   ├── Models/
│   │   ├── Coordinates.cs
│   │   ├── City.cs
│   │   ├── CityOperations.cs          Derived per-city fee/deadline/curfew
│   │   ├── RouteResult.cs
│   │   ├── RouteCostBreakdown.cs      Itemised operating cost
│   │   ├── RouteProgressInfo.cs
│   │   ├── RouteLeg.cs
│   │   ├── RouteObjective.cs          Distance vs. operating cost
│   │   └── SolverMode.cs              Brute force vs. genetic algorithm
│   ├── Services/
│   │   ├── CityDataService.cs
│   │   ├── RouteSolver.cs
│   │   ├── GeneticRouteSolver.cs
│   │   ├── RouteCostModel.cs
│   │   └── MapRenderer.cs
│   ├── ViewModels/
│   │   ├── MainViewModel.cs
│   │   └── SelectableCityViewModel.cs
│   ├── Views/
│   │   ├── RouteMapControl.axaml[.cs] Canvas map UserControl
│   │   ├── AboutWindow.axaml[.cs]     Modal About dialog
│   │   └── AppMessageBox.axaml[.cs]   Shared message-box dialog
│   ├── Converters/
│   │   └── LegCardConverters.cs
│   ├── App.axaml                      Global styles & resource dictionary
│   ├── MainWindow.axaml[.cs]          Main shell
│   └── InternalsVisibleTo.cs          Exposes internals to test project
└── AirFreightRouter.Tests/            xUnit test project (net10.0)
    ├── CoordinatesTests.cs            (3 tests)
    ├── CityDataServiceTests.cs        (7 tests)
    ├── RouteSolverTests.cs            (10 tests)
    └── ValidationTests.cs             (8 tests)
```

---

## Algorithm

### Brute-Force Permutation Search

The problem is a variant of the **Travelling Salesman Problem (TSP)** with a fixed
origin city (Albany, NY). For *n* delivery cities, the solver evaluates every possible
ordering — all *n!* permutations — and returns the ordering with the smallest total
Euclidean degree-distance.

Because every permutation is evaluated, the result is **guaranteed to be the global
optimum** with no approximation.

### Heap's Algorithm (1963)

Permutations are generated using the iterative form of **Heap's algorithm**:

> Heap, B. R. (1963). "Permutations by Interchanges."
> *The Computer Journal*, 6(3), 293–294.
> <https://doi.org/10.1093/comjnl/6.3.293>

The iterative variant is preferred over the recursive form for three reasons:

1. **Stack safety** — no risk of `StackOverflowException` for larger *n*.
2. **Natural cancellation** — the `CancellationToken` is checked at every permutation
   boundary (when the outer counter resets to `i = 0`) with no extra overhead.
3. **Cache efficiency** — exactly one swap is made per permutation, which is optimal
   for memory access patterns.

### Time Complexity

| Delivery cities (*n*) | Permutations (*n!*) | Typical wall time |
|---|---|---|
| 5 | 120 | < 1 ms |
| 8 | 40,320 | < 10 ms |
| 10 | 3,628,800 | ~0.1 – 1 s |
| 11 | 39,916,800 | ~1 – 10 s |
| 12 | 479,001,600 | ~10 – 120 s |
| 13 | 6,227,020,800 | minutes–hours |

> **Warning dialog**: the application displays an estimated completion time
> (benchmarked on the local machine at runtime) and asks for confirmation before
> starting any search with more than **12 delivery cities**.

---

## Threading Model

### Background execution with `Task.Run`

The solver runs on a thread-pool thread via `Task.Run`, keeping the UI thread free to
render progress updates, respond to the Cancel button, and update the elapsed-time clock.

### Live progress with `IProgress<T>`

`Progress<RouteProgressInfo>` is constructed on the UI thread, capturing its
`SynchronizationContext`. Whenever the solver calls `progress.Report(...)`, the callback
is automatically marshalled back to the UI thread — no explicit `Dispatcher.Invoke` is
needed inside the ViewModel.

Progress is throttled to at most one report per **10,000 permutations** or per
**100 ms**, whichever threshold is reached first. Map redraws are further throttled to
one per **500 ms** to avoid saturating the rendering pipeline.

### Cooperative cancellation with `CancellationToken`

`CancellationTokenSource` is created when computation starts and disposed in the
`finally` block. The solver checks `cancellationToken.IsCancellationRequested` at every
permutation boundary and returns `null` when cancellation is requested. No exceptions are
propagated to the UI; the ViewModel treats a `null` result as "cancelled" and displays
the best partial route found so far.

### Elapsed-time clock with `DispatcherTimer`

A `DispatcherTimer` ticking every 100 ms updates `ElapsedTimeDisplay` on the UI thread
while the solver runs. It is started alongside the solver and stopped in `finally`.

### Indeterminate progress bar

`IsProgressIndeterminate` is set `true` when computation starts and flipped to `false` on
the **first** `OnProgressReceived` callback, so the progress bar shows an animated
indeterminate state during the brief initialisation window before the first real
percentage arrives.

---

## Assumptions and Limitations

### Flat-Earth / Equal-Degree Distance Model

Distances are computed using **Euclidean geometry in decimal-degree space**:

```
distance = sqrt((lat₂ - lat₁)² + (lon₂ - lon₁)²)
```

This treats one degree of latitude as equal in length to one degree of longitude,
which is only true on the Equator. For a small cluster of north-eastern US cities the
relative ordering of routes is directionally correct, but the absolute values are **not
true geographic distances**. Real flight-planning software uses great-circle
(geodesic / Haversine) calculations that account for the Earth's curvature.

### Practical City Limit

The algorithm runs in **O(n!)** time. Practical upper bounds depend on hardware:

- Up to **10 cities**: typically completes in under a second.
- Up to **12 cities**: may take minutes; the application displays an estimated time.
- **13 or more cities**: computation time is measured in hours or days; a heuristic
  solver (e.g., nearest-neighbour, 2-opt, genetic algorithm) should be used instead.

### Albany as Fixed Origin

Albany, NY (42.6526°N, 73.7562°W) is always the route's start and end point and is
never included in the delivery set. If the loaded CSV contains a row for Albany,
its coordinates from the file are used; otherwise the hardcoded default coordinates
are used. Albany cannot be selected as a delivery destination.

### US Cities Only

The application is designed around US cities with state abbreviations. The CSV parser
accepts any name/state/lat/lon combination, but no localisation or coordinate validation
beyond numeric parsing is performed.

---

## CSV File Format

### Specification

```
<City>,<State>,<Latitude>,<Longitude>
```

| Field | Type | Description |
|---|---|---|
| City | string | City name (any non-empty value) |
| State | string | State abbreviation or full name |
| Latitude | decimal | Degrees North (positive) or South (negative) |
| Longitude | decimal | Degrees East (positive) or West (negative, for US cities) |

- **Header row** — optional; any line whose latitude or longitude field cannot be parsed
  as a number is silently skipped, so a `City,State,Latitude,Longitude` header is
  automatically ignored.
- **Blank lines** — silently skipped.
- **Malformed lines** — lines with fewer than four comma-separated fields, or with
  non-numeric latitude/longitude, or with an empty name or state, are skipped with a
  `Debug.WriteLine` warning and do not abort the load.
- **Decimal separator** — always `.` (invariant culture); locale-specific separators
  such as `,` are not supported.
- **At least 2 delivery cities** are required after Albany is excluded; files with fewer
  trigger a validation error dialog.

### Example

```csv
City,State,Latitude,Longitude
Albany,NY,42.6526,-73.7562
Concord,NH,43.2081,-71.5376
Boston,MA,42.3601,-71.0589
New York,NY,40.7128,-74.0060
Philadelphia,PA,39.9526,-75.1652
Pittsburgh,PA,40.4406,-79.9959
Hartford,CT,41.7658,-72.6734
Baltimore,MD,39.2904,-76.6122
Washington,DC,38.9072,-77.0369
Richmond,VA,37.5407,-77.4360
Charlotte,NC,35.2271,-80.8431
Raleigh,NC,35.7796,-78.6382
```

This is the bundled `Data/TestCities.csv`. It contains **12 cities** (Albany + 11
delivery cities). Because 11! ≈ 40 million permutations, selecting all 11 delivery
cities will take roughly 1–30 seconds depending on hardware. The computation-time
warning dialog appears only when more than 12 delivery cities are selected, so it
cannot be triggered with this file alone.

---

## Functional Scenario Trace

The following scenarios were traced through the source code to verify correctness.

### 1 · Load `TestCities.csv` (12 cities → 11 delivery cities)

`CityDataService.LoadCitiesFromCsv` parses all 12 city rows (the header is skipped
because "Latitude" does not parse as a `double`). `LoadDataAsync` detects Albany in the
CSV and uses its coordinates for `_origin`. The remaining 11 cities are added to
`AvailableCities`. The minimum-2-city validation passes and `IsDataLoaded` is set to
`true`.

### 2 · Select 5 cities → Compute → Result starts and ends at Albany

`CanStartComputation` returns `true` (5 ≥ 2, data loaded, not computing).
`SolveInternal` evaluates 5! = 120 permutations. The returned `RouteResult.Route` list
has the form `[Albany, c₁, c₂, c₃, c₄, c₅, Albany]` — Albany appears at both index 0
and the last index. This is verified directly by unit test
`FindShortestRouteAsync_Result_StartsAndEndsWithOrigin`.

### 3 · Select 10 cities (or all 11) — computation-time warning behaviour

The warning dialog fires when `SelectedCities.Count > 12`. The test CSV provides at most
11 selectable delivery cities, so **the warning cannot be triggered with this file**.
With 10 or 11 cities selected the solver proceeds immediately without a prompt.

To exercise the warning path, supply a CSV containing 14 or more rows (Albany + 13+
delivery cities). The application will benchmark 1,000 route evaluations on the local
machine, extrapolate to the total permutation count, and display the estimated duration
before asking for confirmation.

### 4 · Start → Cancel midway → Partial result retained

`CancelComputation` calls `_cts.Cancel()`. The solver checks
`cancellationToken.IsCancellationRequested` at each Heap's iteration boundary and
returns `null`. `StartComputationAsync` saves `MapRoute` (the last throttled live
update) as `partialRoute` and calls
`RefreshResultPanel(partialRoute, cancelled: true, result: null)`. If at least one live
map update had been delivered the panel shows **"Computation Cancelled – Best Route So
Far"** with the partial itinerary. If cancelled before the first 500 ms map update the
panel is hidden (nothing meaningful to display).

### 5 · Compute to completion → Optimal route on map and in itinerary

`FindShortestRouteAsync` returns the `RouteResult`. `MapRoute` is set to
`result.Route`, triggering a `Redraw()` that draws the outbound leg as a solid
coloured polyline and the return leg as a static dashed line (marching-ants animation
stops). `HasResult` becomes `true`, showing the route-summary overlay. `HasResultPanel`
becomes `true`, revealing the Expander with one `RouteLeg` card per segment
(each carrying `LegDistanceDisplay`, `CumulativeDistanceDisplay`, `IsReturn`, and
`IsAlternate`). A completion flash plays on all route elements via `PlayCompletionFlash`.

### 6 · Load invalid or empty file → Error handling

| Condition | Exception / State | Response |
|---|---|---|
| File deleted after dialog opened | `FileNotFoundException` | "File Not Found" dialog |
| No read permission | `UnauthorizedAccessException` | "Access Denied" dialog |
| Disk / network I/O error | `IOException` | "File Read Error: …" dialog |
| Empty file or unparseable content | `AvailableCities.Count == 0` | "Too Few Cities" dialog |
| Only Albany present | `AvailableCities.Count == 0` | "Too Few Cities" dialog |
| Any other unexpected exception | `Exception` | "Unexpected Error: …" dialog |

In all error cases `StatusMessage` is updated and the function returns early without
altering `IsDataLoaded` to `true`, so the Compute button remains disabled.

---

## Credits and Attribution

> **[Developer — fill in this section]**
>
> - Developer name and contact
> - Course / project context (if applicable)
> - Any third-party assets, icons, or data sources used
> - Acknowledgements

### Dependencies

| Package | Version | License |
|---|---|---|
| [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet) | 8.4.0 | MIT |
| [xUnit](https://xunit.net/) | 2.9.2 | Apache-2.0 |
| [xunit.runner.visualstudio](https://github.com/xunit/visualstudio.xunit) | 2.8.2 | Apache-2.0 |
| Microsoft.NET.Test.Sdk | 17.11.1 | MIT |

### Algorithm Reference

Heap, B. R. (1963). "Permutations by Interchanges."
*The Computer Journal*, 6(3), 293–294.
<https://doi.org/10.1093/comjnl/6.3.293>

Built with **.NET 10** · **Avalonia** · **CommunityToolkit.Mvvm**
