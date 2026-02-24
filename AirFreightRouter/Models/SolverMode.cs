namespace AirFreightRouter.Models;

/// <summary>Identifies which solver algorithm is used for route computation.</summary>
public enum SolverMode
{
    /// <summary>
    /// Heap's algorithm — evaluates every permutation of delivery cities and
    /// guarantees the globally optimal route.  Practical up to ~11–12 cities.
    /// </summary>
    BruteForce,

    /// <summary>
    /// Genetic algorithm — population-based heuristic using Order Crossover,
    /// swap mutation, and tournament selection.  Finds a near-optimal route in
    /// bounded time and scales comfortably to 30+ cities.
    /// </summary>
    GeneticAlgorithm
}
