using AirFreightRouter.Models;

namespace AirFreightRouter.Services;

/// <summary>
/// Scores a route on operating cost rather than raw distance, implementing the
/// <see cref="RouteObjective.LowestOperatingCost"/> fitness function:
/// <code>
/// Fitness(route) = 1 / (FuelCost + HandlingFees + LatePenalty(route) + CurfewViolations(route) * 1_000)
/// </code>
/// </summary>
/// <remarks>
/// <para>
/// The CSV data source supplies only name, state, latitude and longitude, so the
/// commercial inputs — handling fee, delivery deadline, and airport curfew window — are
/// derived from a stable FNV-1a hash of <c>"Name,State"</c>.  <see cref="string.GetHashCode()"/>
/// is deliberately <em>not</em> used: it is randomised per process in .NET, which would make
/// the same city set score differently between runs.
/// </para>
/// <para>
/// An instance caches the derived profile of every city it is constructed with, so the
/// genetic solver's tens of thousands of evaluations do not re-hash the same strings.
/// Instances are not thread-safe; construct one per solve.
/// </para>
/// </remarks>
public sealed class RouteCostModel
{
    // ── Commercial constants ─────────────────────────────────────────────────
    // Tuned so that fuel dominates a well-ordered route while the penalties are
    // large enough to reorder stops that would otherwise be geometrically ideal.

    /// <summary>USD of fuel burned per degree-distance flown.</summary>
    private const double FuelCostPerDegree = 850.0;

    /// <summary>Degree-distance covered per hour of flight — converts distance to elapsed time.</summary>
    private const double CruiseDegreesPerHour = 7.5;

    /// <summary>Hours of turnaround spent on the ground at each delivery stop.</summary>
    private const double GroundHoursPerStop = 1.5;

    /// <summary>Local clock hour at which the aircraft departs the origin.</summary>
    private const double DepartureHour = 8.0;

    /// <summary>USD charged per hour a shipment lands past its <see cref="CityOperations.DueByHour"/>.</summary>
    private const double LatePenaltyPerHour = 400.0;

    /// <summary>USD fine for each arrival inside a destination's curfew window.</summary>
    private const double CurfewViolationPenalty = 1_000.0;

    // ── Derivation ranges for the per-city profile ───────────────────────────

    private const double MinHandlingFee      = 180.0;   // $180 … $400
    private const double MinDueByHour        = 6.0;     //   6 … 36 hours
    private const int    MinCurfewStartHour  = 21;      //  21 … 23
    private const int    MinCurfewEndHour    = 5;       //   5 …  6

    // Keyed by reference: Coordinates.Equals compares latitude/longitude, so a
    // value-equality dictionary would merge two distinct cities sharing a location.
    private readonly Dictionary<City, CityOperations> _operations =
        new(ReferenceEqualityComparer.Instance);

    /// <summary>
    /// Creates a cost model and pre-computes the operational profile of every supplied city.
    /// </summary>
    /// <param name="cities">
    /// Every city that may appear in a route, including the origin.  Cities not listed here
    /// are still scored correctly; their profile is computed and cached on first use.
    /// </param>
    public RouteCostModel(IEnumerable<City> cities)
    {
        foreach (var city in cities)
            _operations.TryAdd(city, DeriveOperations(city));
    }

    /// <summary>
    /// Returns the operational profile of <paramref name="city"/>, computing and caching
    /// it if this is the first time the city has been seen.
    /// </summary>
    public CityOperations GetOperations(City city)
    {
        if (_operations.TryGetValue(city, out var ops))
            return ops;

        ops = DeriveOperations(city);
        _operations[city] = ops;
        return ops;
    }

    /// <summary>
    /// Computes the itemised operating cost of a complete round-trip route.
    /// </summary>
    /// <param name="route">
    /// The ordered stop sequence <c>origin → delivery cities → origin</c>.  The first and
    /// last entries are treated as the depot and incur no handling fee or penalty.
    /// </param>
    public RouteCostBreakdown Evaluate(IReadOnlyList<City> route)
    {
        double totalDistance = 0.0;
        double handlingFees  = 0.0;
        double latePenalty   = 0.0;
        int    curfewHits    = 0;

        // Walk the route accumulating distance, converting it to elapsed hours as we go.
        // Index 0 is the origin; the final index is the return to the origin, which is
        // flown (and therefore burns fuel) but is not a billable delivery stop.
        for (int i = 1; i < route.Count; i++)
        {
            totalDistance += route[i - 1].DistanceTo(route[i]);

            if (i == route.Count - 1)
                break;

            // i deliveries completed once we land here, but the turnaround for this stop
            // has not happened yet — hence (i - 1) ground stops so far.
            double elapsedHours = totalDistance / CruiseDegreesPerHour
                                  + (i - 1) * GroundHoursPerStop;

            var ops = GetOperations(route[i]);

            handlingFees += ops.HandlingFee;

            if (elapsedHours > ops.DueByHour)
                latePenalty += (elapsedHours - ops.DueByHour) * LatePenaltyPerHour;

            double clockHour = (DepartureHour + elapsedHours) % 24.0;
            if (ops.IsClosedAt(clockHour))
                curfewHits++;
        }

        double fuelCost      = totalDistance * FuelCostPerDegree;
        double curfewPenalty = curfewHits * CurfewViolationPenalty;

        return new RouteCostBreakdown
        {
            FuelCost         = fuelCost,
            HandlingFees     = handlingFees,
            LatePenalty      = latePenalty,
            CurfewViolations = curfewHits,
            CurfewPenalty    = curfewPenalty,
            TotalCost        = fuelCost + handlingFees + latePenalty + curfewPenalty
        };
    }

    /// <summary>
    /// Computes just the total operating cost of <paramref name="route"/> — the value the
    /// solvers minimise.  See <see cref="Evaluate"/> for the itemised form.
    /// </summary>
    public double TotalCost(IReadOnlyList<City> route) => Evaluate(route).TotalCost;

    // ── Deterministic derivation ─────────────────────────────────────────────

    /// <summary>
    /// Derives a city's handling fee, deadline, and curfew window from a stable hash of
    /// its name and state.  Independent quotients of the hash feed each value so that
    /// they vary independently of one another.
    /// </summary>
    private static CityOperations DeriveOperations(City city)
    {
        uint h = Fnv1a($"{city.Name},{city.State}");

        return new CityOperations(
            HandlingFee:     MinHandlingFee     + h % 2200 / 10.0,
            DueByHour:       MinDueByHour       + h / 7 % 300 / 10.0,
            CurfewStartHour: MinCurfewStartHour + (int)(h / 11 % 3),
            CurfewEndHour:   MinCurfewEndHour   + (int)(h / 13 % 2));
    }

    /// <summary>
    /// FNV-1a 32-bit hash — a small, well-distributed, and above all <em>stable</em>
    /// string hash, unlike <see cref="string.GetHashCode()"/> which is randomised per process.
    /// </summary>
    private static uint Fnv1a(string text)
    {
        const uint OffsetBasis = 2166136261;
        const uint Prime       = 16777619;

        uint hash = OffsetBasis;
        foreach (char c in text)
        {
            hash ^= c;
            hash *= Prime;
        }
        return hash;
    }
}
