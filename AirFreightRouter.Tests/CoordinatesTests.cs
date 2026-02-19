using AirFreightRouter.Models;
using Xunit;

namespace AirFreightRouter.Tests;

public class CoordinatesTests
{
    /// <summary>
    /// Verifies that DistanceTo returns the correct Euclidean distance
    /// between two known coordinate points.
    /// Using a (0,0)→(3,4) triangle gives an expected distance of 5.
    /// </summary>
    [Fact]
    public void DistanceTo_KnownCoordinates_ReturnsCorrectDistance()
    {
        var a = new Coordinates { Latitude = 0, Longitude = 0 };
        var b = new Coordinates { Latitude = 3, Longitude = 4 };

        double distance = a.DistanceTo(b);

        Assert.Equal(5.0, distance, precision: 10);
    }

    /// <summary>
    /// Verifies that <see cref="City"/> is derived from <see cref="Coordinates"/>
    /// and an instance is assignable to the base type.
    /// </summary>
    [Fact]
    public void City_InheritsFromCoordinates()
    {
        var city = new City("Chicago", "IL", 41.8781, -87.6298);

        Assert.IsAssignableFrom<Coordinates>(city);
    }

    /// <summary>
    /// Verifies that the distance from a coordinate point to itself is zero.
    /// </summary>
    [Fact]
    public void DistanceTo_SamePoint_ReturnsZero()
    {
        var point = new Coordinates { Latitude = 33.749, Longitude = -84.388 };

        double distance = point.DistanceTo(point);

        Assert.Equal(0.0, distance);
    }
}
