using System;

namespace AirFreightRouter.Models;

/// <summary>
/// Represents a geographic point defined by latitude and longitude,
/// using a flat-earth model where 1 degree latitude equals 1 degree longitude.
/// </summary>
public class Coordinates
{
    /// <summary>Gets or sets the latitude of this coordinate point in degrees.</summary>
    public double Latitude { get; set; }

    /// <summary>Gets or sets the longitude of this coordinate point in degrees.</summary>
    public double Longitude { get; set; }

    /// <summary>
    /// Computes the straight-line Euclidean distance between this coordinate
    /// and <paramref name="other"/>, expressed in degrees.
    /// </summary>
    /// <param name="other">The coordinate to measure the distance to.</param>
    /// <returns>
    /// The Euclidean degree-distance: <c>sqrt((lat2-lat1)² + (lon2-lon1)²)</c>.
    /// </returns>
    public double DistanceTo(Coordinates other)
    {
        double dLat = other.Latitude - Latitude;
        double dLon = other.Longitude - Longitude;
        return Math.Sqrt(dLat * dLat + dLon * dLon);
    }

    /// <summary>Returns the coordinate as a string in the form <c>({Latitude}, {Longitude})</c>.</summary>
    public override string ToString() => $"({Latitude}, {Longitude})";

    /// <summary>
    /// Determines whether this coordinate is equal to <paramref name="obj"/>
    /// based on <see cref="Latitude"/> and <see cref="Longitude"/>.
    /// </summary>
    public override bool Equals(object? obj) =>
        obj is Coordinates other &&
        Latitude == other.Latitude &&
        Longitude == other.Longitude;

    /// <summary>
    /// Returns a hash code derived from <see cref="Latitude"/> and <see cref="Longitude"/>.
    /// </summary>
    public override int GetHashCode() => HashCode.Combine(Latitude, Longitude);
}
