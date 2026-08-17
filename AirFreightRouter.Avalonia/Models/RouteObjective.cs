namespace AirFreightRouter.Models;

/// <summary>
/// Identifies which fitness function a solver uses to score candidate routes.
/// The objective is orthogonal to <see cref="SolverMode"/>: either algorithm can
/// optimise either objective.
/// </summary>
public enum RouteObjective
{
    /// <summary>
    /// Minimise the total Euclidean degree-distance of the round trip.
    /// This is the classic TSP objective and the application's original behaviour.
    /// </summary>
    ShortestDistance,

    /// <summary>
    /// Minimise the total operating cost of the round trip — fuel, per-stop handling
    /// fees, penalties for late deliveries, and fines for arriving during an airport
    /// curfew.  See <see cref="Services.RouteCostModel"/> for the cost breakdown.
    /// </summary>
    LowestOperatingCost
}
