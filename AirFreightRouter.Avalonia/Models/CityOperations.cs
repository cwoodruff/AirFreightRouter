namespace AirFreightRouter.Models;

/// <summary>
/// The operational profile of a single delivery city: what it costs to unload there,
/// when the shipment is due, and when the airport is closed to night traffic.
/// </summary>
/// <remarks>
/// The source CSV files carry only name, state, latitude and longitude, so these values
/// are derived deterministically from the city's name and state by
/// <see cref="Services.RouteCostModel"/>.  The derivation is a pure function of that
/// string, so a given city always produces the same profile — across runs and machines.
/// </remarks>
/// <param name="HandlingFee">
/// Ground-handling charge in USD levied on arrival at this stop.
/// </param>
/// <param name="DueByHour">
/// Hours after departure from the origin by which the shipment must land here.
/// Arriving later incurs a per-hour late penalty.
/// </param>
/// <param name="CurfewStartHour">
/// Local clock hour (0–23) at which the airport closes to freight traffic.
/// </param>
/// <param name="CurfewEndHour">
/// Local clock hour (0–23) at which the airport reopens.  This is always earlier in the
/// day than <paramref name="CurfewStartHour"/>, so the closed window wraps past midnight.
/// </param>
public readonly record struct CityOperations(
    double HandlingFee,
    double DueByHour,
    int    CurfewStartHour,
    int    CurfewEndHour)
{
    /// <summary>
    /// Determines whether <paramref name="clockHour"/> (0–24) falls inside this city's
    /// overnight curfew window.
    /// </summary>
    /// <remarks>
    /// The window runs from <see cref="CurfewStartHour"/> to <see cref="CurfewEndHour"/>
    /// across midnight, so the test is a disjunction rather than a range check.
    /// </remarks>
    public bool IsClosedAt(double clockHour) =>
        clockHour >= CurfewStartHour || clockHour < CurfewEndHour;
}
