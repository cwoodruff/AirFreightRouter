namespace AirFreightRouter.Models;

/// <summary>
/// The itemized operating cost of a complete round-trip route, as scored by the
/// <see cref="RouteObjective.LowestOperatingCost"/> fitness function.
/// </summary>
/// <remarks>
/// <see cref="TotalCost"/> is the denominator of that fitness function:
/// <c>Fitness = 1 / (FuelCost + HandlingFees + LatePenalty + CurfewViolations × 1000)</c>.
/// The individual terms are retained so the results panel can show <em>why</em> a route
/// scored the way it did.
/// </remarks>
public sealed class RouteCostBreakdown
{
    /// <summary>Fuel burned across every leg, in USD.</summary>
    public double FuelCost { get; init; }

    /// <summary>Ground-handling charges summed over every delivery stop, in USD.</summary>
    public double HandlingFees { get; init; }

    /// <summary>Penalties for stops reached after their due-by time, in USD.</summary>
    public double LatePenalty { get; init; }

    /// <summary>Number of stops reached while the destination airport was under curfew.</summary>
    public int CurfewViolations { get; init; }

    /// <summary>
    /// Fines for the curfew arrivals: <see cref="CurfewViolations"/> × $1,000.
    /// </summary>
    public double CurfewPenalty { get; init; }

    /// <summary>The sum of all four terms — the value the solver minimizes.</summary>
    public double TotalCost { get; init; }
}
