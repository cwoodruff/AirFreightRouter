namespace AirFreightRouter.Models;

/// <summary>
/// Represents a city with a geographic location, extending <see cref="Coordinates"/>
/// with a human-readable name and U.S. state identifier.
/// </summary>
public class City : Coordinates
{
    /// <summary>Gets or sets the name of the city.</summary>
    public string Name { get; set; }

    /// <summary>Gets or sets the state in which the city is located.</summary>
    public string State { get; set; }

    /// <summary>
    /// Initializes a new <see cref="City"/> with the specified name, state, and location.
    /// </summary>
    /// <param name="name">The city name.</param>
    /// <param name="state">The state abbreviation or full name.</param>
    /// <param name="latitude">The latitude of the city in degrees.</param>
    /// <param name="longitude">The longitude of the city in degrees.</param>
    public City(string name, string state, double latitude, double longitude)
    {
        Name = name;
        State = state;
        Latitude = latitude;
        Longitude = longitude;
    }

    /// <summary>Returns the city as a string in the form <c>{Name}, {State}</c>.</summary>
    public override string ToString() => $"{Name}, {State}";
}
