using System.Diagnostics;
using AirFreightRouter.Models;

namespace AirFreightRouter.Services;

/// <summary>
/// Genetic algorithm TSP solver adapted from the traveling-salesman-GA reference project.
/// Uses Order Crossover (OX), swap mutation, tournament selection, and elitism to find
/// a near-optimal round-trip route in bounded time.
/// </summary>
/// <remarks>
/// Unlike the brute-force solver, this algorithm does not guarantee the globally optimal
/// solution, but it scales to 30+ cities in seconds instead of hours.
///
/// Chromosome encoding: an <c>int[]</c> permutation of delivery-city indices
/// (0 … n−1 into the <paramref name="cities"/> list).  The origin is not part of
/// the chromosome; it is always prepended and appended when computing route distance.
///
/// Fitness is the reciprocal of whichever quantity the caller's
/// <see cref="RouteObjective"/> selects — route distance, or the operating cost
/// computed by <see cref="RouteCostModel"/> — so the GA maximises fitness while
/// minimising the chosen objective.
///
/// Algorithm reference:
///   Goldberg, D. E. (1989). Genetic Algorithms in Search, Optimization, and Machine Learning.
///   Order Crossover (OX) operator: Davis, L. (1985). Applying Adaptive Algorithms to
///   Epistatic Domains. IJCAI.
/// </remarks>
public class GeneticRouteSolver
{
    // ── GA hyperparameters ────────────────────────────────────────────────────
    private const int    PopulationSize = 150;
    private const double MutationRate   = 0.02;
    private const double CrossoverRate  = 0.8;
    private const int    TournamentSize = 5;
    private const int    MaxGenerations = 500;

    /// <summary>
    /// Asynchronously finds a near-optimal round-trip route that starts at
    /// <paramref name="origin"/>, visits every city in <paramref name="cities"/>
    /// exactly once, and returns to <paramref name="origin"/>.
    /// </summary>
    /// <param name="cities">
    /// The delivery cities to visit (must not include the origin city).
    /// </param>
    /// <param name="origin">The fixed start and end point of the route.</param>
    /// <param name="objective">
    /// The fitness function to maximise — shortest distance or lowest operating cost.
    /// </param>
    /// <param name="progress">
    /// Receives a <see cref="RouteProgressInfo"/> after every generation.
    /// <see cref="RouteProgressInfo.PermutationsEvaluated"/> is the current
    /// generation number; <see cref="RouteProgressInfo.TotalPermutations"/> is
    /// <see cref="MaxGenerations"/>.
    /// </param>
    /// <param name="cancellationToken">Token that can abort the search.</param>
    /// <returns>
    /// A <see cref="RouteResult"/> holding the best route found, or
    /// <see langword="null"/> if cancelled before completing.
    /// </returns>
    public async Task<RouteResult?> FindRouteAsync(
        List<City>                   cities,
        City                         origin,
        RouteObjective               objective,
        IProgress<RouteProgressInfo> progress,
        CancellationToken            cancellationToken)
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

    // ── Core solver — runs on a thread-pool thread via Task.Run ──────────────

    private static RouteResult? SolveInternal(
        List<City>                    cities,
        City                          origin,
        RouteObjective                objective,
        IProgress<RouteProgressInfo>? progress,
        CancellationToken             cancellationToken)
    {
        int  n         = cities.Count;
        var  sw        = Stopwatch.StartNew();
        bool costMode  = objective == RouteObjective.LowestOperatingCost;
        var  costModel = new RouteCostModel(cities.Append(origin));

        // ── Degenerate cases ─────────────────────────────────────────────────
        if (n == 0)
        {
            List<City> trivial = [origin, origin];
            return new RouteResult
            {
                Route                 = trivial,
                TotalDistance         = 0.0,
                PermutationsEvaluated = 0,
                TotalPermutations     = MaxGenerations,
                ElapsedTime           = sw.Elapsed,
                Cost                  = costMode ? costModel.Evaluate(trivial) : null
            };
        }

        if (n == 1)
        {
            List<City> single = [origin, cities[0], origin];
            double     d      = origin.DistanceTo(cities[0]) * 2.0;
            return new RouteResult
            {
                Route                 = single,
                TotalDistance         = d,
                PermutationsEvaluated = 1,
                TotalPermutations     = MaxGenerations,
                ElapsedTime           = sw.Elapsed,
                Cost                  = costMode ? costModel.Evaluate(single) : null
            };
        }

        var rand = new Random();

        // ── Local helpers ────────────────────────────────────────────────────

        // Route: origin → cities[chr[0]] → … → cities[chr[n-1]] → origin
        double RouteDistance(int[] chromosome)
        {
            double d = origin.DistanceTo(cities[chromosome[0]]);
            for (int k = 1; k < n; k++)
                d += cities[chromosome[k - 1]].DistanceTo(cities[chromosome[k]]);
            d += cities[chromosome[n - 1]].DistanceTo(origin);
            return d;
        }

        // Scratch buffer holding origin → chromosome cities → origin, reused across every
        // cost evaluation so the hot loop stays allocation-free.  Only used in cost mode.
        var scratchRoute = new City[n + 2];
        scratchRoute[0]     = origin;
        scratchRoute[n + 1] = origin;

        City[] AsRoute(int[] chromosome)
        {
            for (int k = 0; k < n; k++)
                scratchRoute[k + 1] = cities[chromosome[k]];
            return scratchRoute;
        }

        // The value being minimised: distance, or total operating cost.
        double RouteScore(int[] chromosome) =>
            costMode ? costModel.TotalCost(AsRoute(chromosome))
                     : RouteDistance(chromosome);

        // Fitness = 1 / score (maximized by the GA ≡ minimizing the score).
        double Fitness(int[] chromosome) => 1.0 / (RouteScore(chromosome) + 1e-10);

        // Fisher–Yates shuffle.
        void Shuffle(int[] arr)
        {
            for (int i = arr.Length - 1; i > 0; i--)
            {
                int j = rand.Next(i + 1);
                (arr[i], arr[j]) = (arr[j], arr[i]);
            }
        }

        // ── Initialise population ────────────────────────────────────────────
        var population = new (int[] Chromosome, double Fitness)[PopulationSize];
        for (int i = 0; i < PopulationSize; i++)
        {
            var chr = new int[n];
            for (int j = 0; j < n; j++) chr[j] = j;
            Shuffle(chr);
            population[i] = (chr, Fitness(chr));
        }

        // Track all-time best across generations.
        int[] bestChromosome = (int[])population[0].Chromosome.Clone();
        double bestFitness   = population[0].Fitness;
        for (int i = 1; i < PopulationSize; i++)
        {
            if (population[i].Fitness > bestFitness)
            {
                bestFitness    = population[i].Fitness;
                bestChromosome = (int[])population[i].Chromosome.Clone();
            }
        }

        void EmitProgress(int generation, bool isFinal)
        {
            if (progress == null) return;

            // Distance is reported explicitly rather than as 1 / bestFitness: in cost mode
            // fitness is the reciprocal of cost, not of distance.
            progress.Report(new RouteProgressInfo
            {
                PermutationsEvaluated = generation,
                TotalPermutations     = MaxGenerations,
                PercentComplete       = isFinal ? 100.0 : (double)generation / MaxGenerations * 100.0,
                CurrentBestDistance   = RouteDistance(bestChromosome),
                CurrentBestCost       = costMode ? 1.0 / bestFitness : null,
                CurrentBestRoute      = bestChromosome.Select(idx => cities[idx]).ToList()
            });
        }

        // ── Tournament selection ─────────────────────────────────────────────
        // Returns the index of the tournament winner in population[].
        int TournamentSelect()
        {
            int best = rand.Next(PopulationSize);
            for (int t = 1; t < TournamentSize; t++)
            {
                int challenger = rand.Next(PopulationSize);
                if (population[challenger].Fitness > population[best].Fitness)
                    best = challenger;
            }
            return best;
        }

        // ── Order Crossover (OX) — O(n) using a HashSet for membership ───────
        //
        // Reference: Davis, L. (1985). Applying Adaptive Algorithms to Epistatic Domains.
        //
        // Copies a contiguous segment [start, end] from p1 verbatim, then fills
        // the remaining positions (in wrap-around order starting at end+1) with
        // genes from p2 in the order they appear, skipping duplicates.
        int[] OrderCrossover(int[] p1, int[] p2)
        {
            int start = rand.Next(n);
            int end   = rand.Next(n);
            if (start > end) (start, end) = (end, start);

            var child      = new int[n];
            var inSegment  = new HashSet<int>(end - start + 1);

            Array.Fill(child, -1);
            for (int i = start; i <= end; i++)
            {
                child[i] = p1[i];
                inSegment.Add(p1[i]);
            }

            int ci = (end + 1) % n;
            for (int i = 0; i < n; i++)
            {
                int gene = p2[(end + 1 + i) % n];
                if (!inSegment.Contains(gene))
                {
                    child[ci] = gene;
                    ci = (ci + 1) % n;
                }
            }
            return child;
        }

        // ── Swap mutation ────────────────────────────────────────────────────
        void Mutate(int[] chromosome)
        {
            for (int i = 0; i < n; i++)
            {
                if (rand.NextDouble() < MutationRate)
                {
                    int a = rand.Next(n);
                    int b = rand.Next(n);
                    (chromosome[a], chromosome[b]) = (chromosome[b], chromosome[a]);
                }
            }
        }

        // ── Main generational loop ───────────────────────────────────────────
        var newPop = new (int[] Chromosome, double Fitness)[PopulationSize];

        for (int generation = 1; generation <= MaxGenerations; generation++)
        {
            if (cancellationToken.IsCancellationRequested)
                return null;

            // Elitism: preserve the all-time best individual in slot 0.
            newPop[0] = ((int[])bestChromosome.Clone(), bestFitness);
            int filled = 1;

            while (filled < PopulationSize)
            {
                int p1Idx = TournamentSelect();
                int p2Idx = TournamentSelect();

                int[] c1, c2;
                if (rand.NextDouble() < CrossoverRate)
                {
                    c1 = OrderCrossover(population[p1Idx].Chromosome, population[p2Idx].Chromosome);
                    c2 = OrderCrossover(population[p2Idx].Chromosome, population[p1Idx].Chromosome);
                }
                else
                {
                    c1 = (int[])population[p1Idx].Chromosome.Clone();
                    c2 = (int[])population[p2Idx].Chromosome.Clone();
                }

                Mutate(c1);
                Mutate(c2);

                newPop[filled++] = (c1, Fitness(c1));
                if (filled < PopulationSize)
                    newPop[filled++] = (c2, Fitness(c2));
            }

            population = newPop;
            newPop     = new (int[] Chromosome, double Fitness)[PopulationSize];

            // Update all-time best from this generation.
            for (int i = 0; i < PopulationSize; i++)
            {
                if (population[i].Fitness > bestFitness)
                {
                    bestFitness    = population[i].Fitness;
                    bestChromosome = (int[])population[i].Chromosome.Clone();
                }
            }

            EmitProgress(generation, generation == MaxGenerations);
        }

        // ── Build result ─────────────────────────────────────────────────────
        var route = new List<City>(n + 2) { origin };
        route.AddRange(bestChromosome.Select(idx => cities[idx]));
        route.Add(origin);

        return new RouteResult
        {
            Route                 = route,
            TotalDistance         = RouteDistance(bestChromosome),
            PermutationsEvaluated = MaxGenerations,   // generations completed
            TotalPermutations     = MaxGenerations,
            ElapsedTime           = sw.Elapsed,
            Cost                  = costMode ? costModel.Evaluate(route) : null
        };
    }
}
