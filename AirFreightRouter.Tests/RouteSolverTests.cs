using AirFreightRouter.Models;
using AirFreightRouter.Services;
using Xunit;

namespace AirFreightRouter.Tests;

public class RouteSolverTests
{
    // -----------------------------------------------------------------------
    // Shared fixtures
    // -----------------------------------------------------------------------

    private static City Albany()       => CityDataService.GetAlbany();
    private static RouteSolver Solver() => new RouteSolver();

    /// <summary>Three north-eastern cities used in several tests.</summary>
    private static List<City> ThreeCities() =>
    [
        new City("Boston",       "MA", 42.3601, -71.0589),
        new City("New York",     "NY", 40.7128, -74.0060),
        new City("Philadelphia", "PA", 39.9526, -75.1652),
    ];

    // -----------------------------------------------------------------------
    // Permutation count
    // -----------------------------------------------------------------------

    /// <summary>
    /// For n=3 delivery cities the solver must evaluate exactly 3! = 6 permutations.
    /// </summary>
    [Fact]
    public async Task FindShortestRouteAsync_ThreeCities_EvaluatesExactlySixPermutations()
    {
        var result = await Solver().FindShortestRouteAsync(
            ThreeCities(), Albany(), null!, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(6L, result.PermutationsEvaluated);
        Assert.Equal(6L, result.TotalPermutations);
    }

    /// <summary>
    /// For n=1 delivery city the solver evaluates exactly 1! = 1 permutation.
    /// </summary>
    [Fact]
    public async Task FindShortestRouteAsync_OneCity_EvaluatesExactlyOnePermutation()
    {
        var cities = new List<City> { new City("Boston", "MA", 42.3601, -71.0589) };

        var result = await Solver().FindShortestRouteAsync(
            cities, Albany(), null!, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(1L, result.PermutationsEvaluated);
        Assert.Equal(1L, result.TotalPermutations);
    }

    // -----------------------------------------------------------------------
    // Route structure
    // -----------------------------------------------------------------------

    /// <summary>
    /// The returned route must begin and end with the origin city (Albany).
    /// </summary>
    [Fact]
    public async Task FindShortestRouteAsync_Result_StartsAndEndsWithOrigin()
    {
        var result = await Solver().FindShortestRouteAsync(
            ThreeCities(), Albany(), null!, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("Albany", result.Route.First().Name);
        Assert.Equal("Albany", result.Route.Last().Name);
    }

    /// <summary>
    /// The route must contain exactly n+2 entries (origin + n delivery cities + origin).
    /// </summary>
    [Fact]
    public async Task FindShortestRouteAsync_ThreeCities_RouteHasFiveCities()
    {
        var result = await Solver().FindShortestRouteAsync(
            ThreeCities(), Albany(), null!, CancellationToken.None);

        Assert.NotNull(result);
        // Albany + 3 delivery cities + Albany = 5
        Assert.Equal(5, result.Route.Count);
    }

    /// <summary>
    /// Every delivery city supplied must appear exactly once in the middle of the route.
    /// </summary>
    [Fact]
    public async Task FindShortestRouteAsync_ThreeCities_AllDeliveryCitiesPresent()
    {
        var deliveryCities = ThreeCities();
        var result = await Solver().FindShortestRouteAsync(
            deliveryCities, Albany(), null!, CancellationToken.None);

        Assert.NotNull(result);

        // Strip the two Albany endpoints, leaving only the delivery stops.
        var middle = result.Route.Skip(1).SkipLast(1).ToList();
        Assert.Equal(deliveryCities.Count, middle.Count);

        foreach (var expected in deliveryCities)
            Assert.Contains(middle, c => c.Name == expected.Name);
    }

    // -----------------------------------------------------------------------
    // Optimality
    // -----------------------------------------------------------------------

    /// <summary>
    /// The solver's reported total distance must equal the sum of consecutive
    /// DistanceTo calls along the returned route, verifying internal consistency.
    /// </summary>
    [Fact]
    public async Task FindShortestRouteAsync_TotalDistance_MatchesRouteSegments()
    {
        var result = await Solver().FindShortestRouteAsync(
            ThreeCities(), Albany(), null!, CancellationToken.None);

        Assert.NotNull(result);

        double computed = 0;
        for (int i = 0; i < result.Route.Count - 1; i++)
            computed += result.Route[i].DistanceTo(result.Route[i + 1]);

        Assert.Equal(computed, result.TotalDistance, precision: 10);
    }

    /// <summary>
    /// The solver's result must be at least as short as every other permutation,
    /// confirming it found the global optimum.
    /// </summary>
    [Fact]
    public async Task FindShortestRouteAsync_ThreeCities_ResultIsGlobalOptimum()
    {
        var deliveryCities = ThreeCities();
        var origin         = Albany();
        var result         = await Solver().FindShortestRouteAsync(
            deliveryCities, origin, null!, CancellationToken.None);

        Assert.NotNull(result);

        // Generate all 6 permutations manually and verify none is shorter.
        var perms = GetAllPermutations(deliveryCities);
        foreach (var perm in perms)
        {
            double d = origin.DistanceTo(perm[0]);
            for (int i = 1; i < perm.Count; i++)
                d += perm[i - 1].DistanceTo(perm[i]);
            d += perm[^1].DistanceTo(origin);

            Assert.True(result.TotalDistance <= d + 1e-10,
                $"Solver returned {result.TotalDistance:F6} but permutation " +
                $"{string.Join("→", perm.Select(c => c.Name))} yields {d:F6}");
        }
    }

    // -----------------------------------------------------------------------
    // Cancellation
    // -----------------------------------------------------------------------

    /// <summary>
    /// When the CancellationToken is already cancelled before the call,
    /// the method must return null without throwing.
    /// </summary>
    [Fact]
    public async Task FindShortestRouteAsync_PreCancelledToken_ReturnsNull()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = await Solver().FindShortestRouteAsync(
            ThreeCities(), Albany(), null!, cts.Token);

        Assert.Null(result);
    }

    /// <summary>
    /// When the CancellationToken is cancelled during a long-running solve,
    /// the method must return null without throwing.
    /// </summary>
    [Fact]
    public async Task FindShortestRouteAsync_CancelledMidRun_ReturnsNull()
    {
        // Build a large enough city list so the solver does not finish instantly.
        var cities = Enumerable.Range(1, 10)
            .Select(i => new City($"City{i}", "XX", i * 1.0, i * 0.5))
            .ToList();

        using var cts = new CancellationTokenSource();

        // Cancel after a short delay to interrupt the 10! = 3.6 M permutation search.
        cts.CancelAfter(TimeSpan.FromMilliseconds(30));

        var result = await Solver().FindShortestRouteAsync(
            cities, Albany(), null!, cts.Token);

        Assert.Null(result);
    }

    // -----------------------------------------------------------------------
    // Progress reporting
    // -----------------------------------------------------------------------

    /// <summary>
    /// Progress must be reported at least once (the final 100 % update) even for
    /// a tiny input, and every report must have a non-negative PercentComplete.
    /// </summary>
    [Fact]
    public async Task FindShortestRouteAsync_Progress_ReportsAtLeastFinalUpdate()
    {
        var reports = new List<RouteProgressInfo>();
        var progress = new Progress<RouteProgressInfo>(r => reports.Add(r));

        await Solver().FindShortestRouteAsync(
            ThreeCities(), Albany(), progress, CancellationToken.None);

        // Allow the Progress<T> post to the sync context to complete.
        await Task.Delay(50);

        Assert.NotEmpty(reports);
        Assert.All(reports, r => Assert.True(r.PercentComplete >= 0));

        // Last report should be 100 %.
        Assert.Equal(100.0, reports.Last().PercentComplete);
    }

    // -----------------------------------------------------------------------
    // Helper: naive permutation generator for the optimality test
    // -----------------------------------------------------------------------

    private static List<List<City>> GetAllPermutations(List<City> items)
    {
        var result = new List<List<City>>();
        Permute(items.ToList(), 0, result);
        return result;
    }

    private static void Permute(List<City> items, int start, List<List<City>> result)
    {
        if (start == items.Count - 1) { result.Add(new List<City>(items)); return; }
        for (int i = start; i < items.Count; i++)
        {
            (items[start], items[i]) = (items[i], items[start]);
            Permute(items, start + 1, result);
            (items[start], items[i]) = (items[i], items[start]);
        }
    }
}
