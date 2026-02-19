using AirFreightRouter.Models;
using AirFreightRouter.Services;
using Xunit;

namespace AirFreightRouter.Tests;

public class ValidationTests
{
    private static City Albany() => CityDataService.GetAlbany();

    // -------------------------------------------------------------------------
    // Factorial — boundary values and overflow protection
    // -------------------------------------------------------------------------

    [Fact]
    public void Factorial_Zero_ReturnsOne()
        => Assert.Equal(1L, RouteSolver.Factorial(0));

    [Fact]
    public void Factorial_One_ReturnsOne()
        => Assert.Equal(1L, RouteSolver.Factorial(1));

    [Fact]
    public void Factorial_Five_Returns120()
        => Assert.Equal(120L, RouteSolver.Factorial(5));

    [Fact]
    public void Factorial_Twenty_ReturnsCorrectValue()
        => Assert.Equal(2_432_902_008_176_640_000L, RouteSolver.Factorial(20));

    /// <summary>
    /// 21! overflows a 64-bit signed integer; the guard must return long.MaxValue
    /// rather than wrapping or throwing.
    /// </summary>
    [Fact]
    public void Factorial_TwentyOne_ReturnsLongMaxValue()
        => Assert.Equal(long.MaxValue, RouteSolver.Factorial(21));

    [Fact]
    public void Factorial_LargeValue_ReturnsLongMaxValue()
        => Assert.Equal(long.MaxValue, RouteSolver.Factorial(100));

    // -------------------------------------------------------------------------
    // EstimateSecondsAsync
    // -------------------------------------------------------------------------

    /// <summary>
    /// Three cities produce a finite, positive estimate (6 permutations × the
    /// per-permutation benchmark time).
    /// </summary>
    [Fact]
    public async Task EstimateSecondsAsync_ThreeCities_ReturnsPositive()
    {
        var cities = new List<City>
        {
            new City("Boston",       "MA", 42.3601, -71.0589),
            new City("New York",     "NY", 40.7128, -74.0060),
            new City("Philadelphia", "PA", 39.9526, -75.1652),
        };

        double estimate = await RouteSolver.EstimateSecondsAsync(cities, Albany());

        Assert.True(estimate > 0.0, $"Expected positive estimate, got {estimate}");
        Assert.NotEqual(double.MaxValue, estimate);
    }

    /// <summary>
    /// With 0 or 1 delivery cities there is nothing meaningful to estimate;
    /// the method must return 0.0 for both cases.
    /// </summary>
    [Fact]
    public async Task EstimateSecondsAsync_OneCityOrFewer_ReturnsZero()
    {
        var oneCity  = new List<City> { new City("Boston", "MA", 42.3601, -71.0589) };
        var noCities = new List<City>();

        double withOne   = await RouteSolver.EstimateSecondsAsync(oneCity,  Albany());
        double withNone  = await RouteSolver.EstimateSecondsAsync(noCities, Albany());

        Assert.Equal(0.0, withOne);
        Assert.Equal(0.0, withNone);
    }
}
