using System.Diagnostics;
using AirFreightRouter.Models;

namespace AirFreightRouter.Services;

/// <summary>
/// Brute-force Travelling Salesman solver that evaluates every permutation of the
/// delivery cities to guarantee the globally optimal round-trip route.
/// </summary>
/// <remarks>
/// Practical upper bound: the algorithm is feasible for up to ~11 or –12 delivery cities
/// Beyond that, computation time grows factorially and a heuristic solver should be preferred.
///
/// "Optimal" is defined by the caller's <see cref="RouteObjective"/>: either the shortest
/// total distance, or the lowest operating cost as priced by <see cref="RouteCostModel"/>.
/// </remarks>
public class RouteSolver
{
    // How often to emit a progress update (whichever threshold is reached first).
    private const long ProgressEveryNPermutations = 10_000;
    private const double ProgressEveryMs           = 100.0;

    // Check elapsed time every this many permutations to amortize Stopwatch overhead.
    private const long TimeCheckInterval = 1_000;

    /// <summary>
    /// Asynchronously finds the best round-trip route — by the chosen
    /// <paramref name="objective"/> — that starts at <paramref name="origin"/>, visits
    /// every city in <paramref name="cities"/> exactly once, and returns to
    /// <paramref name="origin"/>.
    /// </summary>
    /// <param name="cities">
    /// The delivery cities to visit (must not include the origin city).
    /// </param>
    /// <param name="origin">
    /// The fixed start and end point of the route (e.g. Albany, NY).
    /// </param>
    /// <param name="objective">
    /// The quantity to minimise — total distance or total operating cost.
    /// </param>
    /// <param name="progress">
    /// Receives periodic <see cref="RouteProgressInfo"/> snapshots. May be
    /// <see langword="null"/> if progress reporting is not needed.
    /// </param>
    /// <param name="cancellationToken">
    /// Token that can be used to abort the search early.
    /// </param>
    /// <returns>
    /// The optimal <see cref="RouteResult"/>, or <see langword="null"/> if
    /// the operation was cancelled before completion.
    /// </returns>
    public async Task<RouteResult?> FindShortestRouteAsync(
        List<City> cities,
        City origin,
        RouteObjective objective,
        IProgress<RouteProgressInfo> progress,
        CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
            return null;

        try
        {
            return await Task.Run(
                () => SolveInternal(cities, origin, objective, progress, cancellationToken),
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
    }

    // -------------------------------------------------------------------------
    // Core solver — runs on a thread-pool thread via Task.Run.
    // -------------------------------------------------------------------------

    private static RouteResult? SolveInternal(
        List<City>               cities,
        City                     origin,
        RouteObjective           objective,
        IProgress<RouteProgressInfo>? progress,
        CancellationToken        cancellationToken)
    {
        int  n                  = cities.Count;
        long totalPermutations  = Factorial(n);
        bool costMode           = objective == RouteObjective.LowestOperatingCost;
        var  costModel          = new RouteCostModel(cities.Append(origin));

        // Working array mutated in-place by Heap's algorithm.
        City[] perm    = cities.ToArray();
        // Heap's algorithm control counters, one per position.
        int[]  c       = new int[n];

        // Scratch buffer holding origin → perm → origin, reused across every cost
        // evaluation so the inner loop stays allocation-free.  Only used in cost mode.
        City[] scratchRoute = new City[n + 2];
        scratchRoute[0]     = origin;
        scratchRoute[n + 1] = origin;

        double  bestScore    = double.MaxValue;
        double  bestDistance = double.MaxValue;
        City[]? bestPerm     = null;
        long    evaluated    = 0L;

        var      sw           = Stopwatch.StartNew();
        TimeSpan lastReportAt = TimeSpan.Zero;

        // ----- helpers -------------------------------------------------------

        // Computes origin → perm[0] → … → perm[n-1] → origin.
        double RouteDistance()
        {
            if (n == 0) return 0.0;
            double d = origin.DistanceTo(perm[0]);
            for (int k = 1; k < n; k++)
                d += perm[k - 1].DistanceTo(perm[k]);
            d += perm[n - 1].DistanceTo(origin);
            return d;
        }

        // Copies the current permutation into the scratch route and prices it.
        double RouteCost()
        {
            for (int k = 0; k < n; k++)
                scratchRoute[k + 1] = perm[k];
            return costModel.TotalCost(scratchRoute);
        }

        void EvaluateCurrent()
        {
            double dist  = RouteDistance();
            double score = costMode ? RouteCost() : dist;
            evaluated++;
            if (score < bestScore)
            {
                bestScore    = score;
                bestDistance = dist;
                bestPerm     = (City[])perm.Clone();
            }
        }

        void EmitProgress(bool isFinal)
        {
            if (progress == null) return;
            progress.Report(new RouteProgressInfo
            {
                PermutationsEvaluated = evaluated,
                TotalPermutations     = totalPermutations,
                PercentComplete       = isFinal || totalPermutations == 0
                                            ? 100.0
                                            : (double)evaluated / totalPermutations * 100.0,
                CurrentBestDistance   = bestDistance == double.MaxValue ? 0.0 : bestDistance,
                CurrentBestCost       = costMode && bestScore != double.MaxValue ? bestScore : null,
                CurrentBestRoute      = bestPerm?.ToList()
            });
        }

        // ----- Heap's algorithm (iterative) ----------------------------------
        //
        // Reference:
        //   Heap, B. R. (1963). "Permutations by Interchanges."
        //   The Computer Journal, 6(3), 293–294.
        //   https://doi.org/10.1093/comjnl/6.3.293
        //
        // The iterative variant is chosen over the recursive one to:
        //   • Avoid stack overflow for larger n values.
        //   • Allow a natural, low-overhead cancellation check at every
        //     permutation boundary (resetting i = 0).
        //   • Maintain O(n) auxiliary space (only the c[] control array).
        //
        // The algorithm generates all n! permutations by making a single swap
        // per permutation, which is optimal for cache performance.

        // Evaluate the initial (input-order) permutation before the loop.
        EvaluateCurrent();

        int i = 0;
        while (i < n)
        {
            if (cancellationToken.IsCancellationRequested)
                return null;

            if (c[i] < i)
            {
                // Generate the next permutation with a single swap.
                if (i % 2 == 0)
                    (perm[0], perm[i]) = (perm[i], perm[0]);
                else
                    (perm[c[i]], perm[i]) = (perm[i], perm[c[i]]);

                EvaluateCurrent();

                // Report progress every ProgressEveryNPermutations, or
                // every ProgressEveryMs milliseconds (checked at coarser
                // intervals to amortise Stopwatch.Elapsed overhead).
                if (evaluated % ProgressEveryNPermutations == 0)
                {
                    EmitProgress(false);
                    lastReportAt = sw.Elapsed;
                }
                else if (evaluated % TimeCheckInterval == 0)
                {
                    var now = sw.Elapsed;
                    if ((now - lastReportAt).TotalMilliseconds >= ProgressEveryMs)
                    {
                        EmitProgress(false);
                        lastReportAt = now;
                    }
                }

                c[i]++;
                i = 0;
            }
            else
            {
                c[i] = 0;
                i++;
            }
        }

        // Final 100 % progress update.
        EmitProgress(true);

        // Build the full route list: origin → [optimal order] → origin.
        var route = new List<City>(n + 2) { origin };
        if (bestPerm != null) route.AddRange(bestPerm);
        route.Add(origin);

        return new RouteResult
        {
            Route                = route,
            TotalDistance        = bestDistance == double.MaxValue ? 0.0 : bestDistance,
            PermutationsEvaluated = evaluated,
            TotalPermutations    = totalPermutations,
            ElapsedTime          = sw.Elapsed,
            Cost                 = costMode ? costModel.Evaluate(route) : null
        };
    }

    // -------------------------------------------------------------------------
    // Utilities
    // -------------------------------------------------------------------------

    /// <summary>
    /// Computes n! as a <see langword="long"/>.
    /// Valid for 0 ≤ n ≤ 20 (20! is the largest factorial that fits in a
    /// <see langword="long"/>). Returns <see cref="long.MaxValue"/> for n &gt; 20
    /// to signal overflow without throwing.
    /// </summary>
    internal static long Factorial(int n)
    {
        if (n > 20) return long.MaxValue;
        if (n <= 1) return 1L;
        long result = 1L;
        for (int k = 2; k <= n; k++)
            result *= k;
        return result;
    }

    /// <summary>
    /// Benchmarks the route-distance inner loop on this machine and extrapolates
    /// the expected wall-clock time to evaluate all n! permutations.
    /// </summary>
    /// <param name="deliveryCities">The delivery cities to include in the benchmark.</param>
    /// <param name="origin">The fixed start/end point of each route.</param>
    /// <returns>
    /// Estimated seconds to brute-force all permutations, or
    /// <see cref="double.MaxValue"/> if the permutation count overflows
    /// <see langword="long"/>. Returns <c>0.0</c> if fewer than 2 delivery cities
    /// are provided (nothing meaningful to estimate).
    /// </returns>
    public static async Task<double> EstimateSecondsAsync(List<City> deliveryCities, City origin)
    {
        int n = deliveryCities.Count;
        if (n <= 1) return 0.0;

        long totalPerms = Factorial(n);
        if (totalPerms == long.MaxValue) return double.MaxValue;

        return await Task.Run(() =>
        {
            const int SampleSize = 1_000;
            var sw   = Stopwatch.StartNew();
            double sink = 0;

            for (int iter = 0; iter < SampleSize; iter++)
            {
                double d = origin.DistanceTo(deliveryCities[0]);
                for (int k = 1; k < n; k++)
                    d += deliveryCities[k - 1].DistanceTo(deliveryCities[k]);
                d += deliveryCities[n - 1].DistanceTo(origin);
                sink += d;  // prevent dead-code elimination
            }

            sw.Stop();
            _ = sink;

            // ms-per-permutation × total-permutations → total-ms → seconds
            return sw.Elapsed.TotalMilliseconds / SampleSize * totalPerms / 1_000.0;
        });
    }
}
