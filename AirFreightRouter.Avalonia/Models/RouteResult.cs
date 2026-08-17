namespace AirFreightRouter.Models;

/// <summary>
/// Holds the complete output of a completed route-solving operation.
/// </summary>
public class RouteResult
{
    /// <summary>
    /// Gets or sets the ordered list of cities representing the optimal route,
    /// including the origin city at both the start and end of the list.
    /// </summary>
    public List<City> Route { get; set; } = [];

    /// <summary>
    /// Gets or sets the total Euclidean degree-distance of the optimal route
    /// (origin → all delivery cities → origin).
    /// </summary>
    public double TotalDistance { get; set; }

    /// <summary>
    /// Gets or sets the number of permutations actually evaluated during the search.
    /// For a complete run this equals <see cref="TotalPermutations"/>.
    /// </summary>
    public long PermutationsEvaluated { get; set; }

    /// <summary>
    /// Gets or sets the total number of possible permutations (n! where n is the
    /// number of delivery cities).
    /// </summary>
    public long TotalPermutations { get; set; }

    /// <summary>Gets or sets the wall-clock time taken by the solver.</summary>
    public TimeSpan ElapsedTime { get; set; }

    /// <summary>
    /// Gets or sets the itemised operating cost of <see cref="Route"/>, or
    /// <see langword="null"/> when the solver ran with
    /// <see cref="RouteObjective.ShortestDistance"/>.
    /// </summary>
    public RouteCostBreakdown? Cost { get; set; }
}
