namespace AirFreightRouter.Models;

/// <summary>
/// A snapshot of solver progress reported periodically while the route search is running.
/// </summary>
public class RouteProgressInfo
{
    /// <summary>Gets or sets the number of permutations evaluated so far.</summary>
    public long PermutationsEvaluated { get; set; }

    /// <summary>Gets or sets the total number of permutations to evaluate (n!).</summary>
    public long TotalPermutations { get; set; }

    /// <summary>
    /// Gets or sets the completion percentage in the range [0, 100].
    /// </summary>
    public double PercentComplete { get; set; }

    /// <summary>Gets or sets the shortest route distance found so far.</summary>
    public double CurrentBestDistance { get; set; }

    /// <summary>
    /// Gets or sets the delivery-city ordering (without the origin endpoints) of the
    /// best route found so far, or <see langword="null"/> if none has been evaluated yet.
    /// </summary>
    public List<City>? CurrentBestRoute { get; set; }
}
