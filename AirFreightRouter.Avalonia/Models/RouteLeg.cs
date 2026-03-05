namespace AirFreightRouter.Models;

/// <summary>
/// Represents a single flight segment within a computed route, together with
/// preformatted display strings for use in the results-panel itinerary.
/// </summary>
public sealed class RouteLeg
{
    /// <summary>1-based position of this leg within the full itinerary.</summary>
    public int LegNumber { get; init; }

    /// <summary>The departure city for this leg.</summary>
    public City FromCity { get; init; } = null!;

    /// <summary>The arrival city for this leg.</summary>
    public City ToCity { get; init; } = null!;

    /// <summary>Straight-line distance (degree-distance) for this leg only.</summary>
    public double LegDistance { get; init; }

    /// <summary>Running total distance from the origin up to and including this leg.</summary>
    public double CumulativeDistance { get; init; }

    /// <summary>
    /// <see langword="true"/> for the final leg that returns to the origin city.
    /// Used in the view to apply a distinct accent border.
    /// </summary>
    public bool IsReturn { get; init; }

    /// <summary>
    /// <see langword="true"/> for odd-indexed (0-based) legs, enabling alternating
    /// row backgrounds in the itinerary without a value converter.
    /// </summary>
    public bool IsAlternate { get; init; }

    // ------------------------------------------------------------------
    // Preformatted display strings (keeps format logic out of XAML)
    // ------------------------------------------------------------------

    /// <summary>"City, ST" form of the departure city.</summary>
    public string FromDisplay => FromCity.ToString();

    /// <summary>"City, ST" form of the arrival city.</summary>
    public string ToDisplay => ToCity.ToString();

    /// <summary>Leg distance formatted to four decimal places, e.g. "3.1416°".</summary>
    public string LegDistanceDisplay => $"{LegDistance:F4}°";

    /// <summary>Cumulative distance formatted to four decimal places.</summary>
    public string CumulativeDistanceDisplay => $"{CumulativeDistance:F4}°";

    /// <summary>"(Return)" for the final leg, empty for all other legs.</summary>
    public string ReturnTag => IsReturn ? "(Return)" : string.Empty;
}
