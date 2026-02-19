using System.IO;
using AirFreightRouter.Models;
using AirFreightRouter.Services;
using Xunit;

namespace AirFreightRouter.Tests;

public class CityDataServiceTests
{
    // Writes content to a temp file and returns its path.
    private static string WriteTempCsv(string content)
    {
        var path = Path.GetTempFileName();
        File.WriteAllText(path, content);
        return path;
    }

    /// <summary>
    /// Verifies that a well-formed CSV file (including a header row) is parsed
    /// into the correct number of cities with the expected field values.
    /// </summary>
    [Fact]
    public void LoadCitiesFromCsv_ValidFile_ParsesAllCitiesCorrectly()
    {
        const string csv =
            "City,State,Latitude,Longitude\n" +
            "Albany,NY,42.6526,-73.7562\n" +
            "Boston,MA,42.3601,-71.0589\n";

        var path = WriteTempCsv(csv);
        try
        {
            var cities = CityDataService.LoadCitiesFromCsv(path);

            Assert.Equal(2, cities.Count);

            Assert.Equal("Albany", cities[0].Name);
            Assert.Equal("NY",     cities[0].State);
            Assert.Equal(42.6526,  cities[0].Latitude,  precision: 4);
            Assert.Equal(-73.7562, cities[0].Longitude, precision: 4);

            Assert.Equal("Boston", cities[1].Name);
            Assert.Equal("MA",     cities[1].State);
            Assert.Equal(42.3601,  cities[1].Latitude,  precision: 4);
            Assert.Equal(-71.0589, cities[1].Longitude, precision: 4);
        }
        finally { File.Delete(path); }
    }

    /// <summary>
    /// Verifies that the header row is not included in the parsed results.
    /// </summary>
    [Fact]
    public void LoadCitiesFromCsv_HeaderRow_IsSkipped()
    {
        const string csv =
            "City,State,Latitude,Longitude\n" +
            "Albany,NY,42.6526,-73.7562\n";

        var path = WriteTempCsv(csv);
        try
        {
            var cities = CityDataService.LoadCitiesFromCsv(path);

            Assert.Single(cities);
            Assert.Equal("Albany", cities[0].Name);
        }
        finally { File.Delete(path); }
    }

    /// <summary>
    /// Verifies that blank lines interspersed in the CSV are silently skipped.
    /// </summary>
    [Fact]
    public void LoadCitiesFromCsv_BlankLines_AreSkipped()
    {
        const string csv =
            "City,State,Latitude,Longitude\n" +
            "\n" +
            "Albany,NY,42.6526,-73.7562\n" +
            "   \n" +
            "Boston,MA,42.3601,-71.0589\n";

        var path = WriteTempCsv(csv);
        try
        {
            var cities = CityDataService.LoadCitiesFromCsv(path);

            Assert.Equal(2, cities.Count);
        }
        finally { File.Delete(path); }
    }

    /// <summary>
    /// Verifies that lines with too few fields, non-numeric coordinates, or
    /// empty name/state are skipped, while valid lines are still parsed.
    /// </summary>
    [Fact]
    public void LoadCitiesFromCsv_MalformedLines_AreSkippedAndValidLinesPreserved()
    {
        const string csv =
            "City,State,Latitude,Longitude\n" +         // header   → skipped
            "Albany,NY,42.6526,-73.7562\n" +            // valid    → kept
            "TooFewFields,NY\n" +                       // <4 cols  → skipped
            "Boston,MA,NOT_A_NUMBER,-71.0589\n" +       // bad lat  → skipped
            "Hartford,CT,41.7658,BAD_LON\n" +           // bad lon  → skipped
            ",, , \n" +                                 // empty fields → skipped
            "Boston,MA,42.3601,-71.0589\n";             // valid    → kept

        var path = WriteTempCsv(csv);
        try
        {
            var cities = CityDataService.LoadCitiesFromCsv(path);

            Assert.Equal(2, cities.Count);
            Assert.Equal("Albany", cities[0].Name);
            Assert.Equal("Boston", cities[1].Name);
        }
        finally { File.Delete(path); }
    }

    /// <summary>
    /// Verifies that <see cref="CityDataService.GetAlbany"/> returns a city
    /// with exactly the coordinates specified in the project requirements.
    /// </summary>
    [Fact]
    public void GetAlbany_ReturnsAlbanyNyWithCorrectCoordinates()
    {
        var albany = CityDataService.GetAlbany();

        Assert.Equal("Albany", albany.Name);
        Assert.Equal("NY",     albany.State);
        Assert.Equal(42.6526,  albany.Latitude,  precision: 4);
        Assert.Equal(-73.7562, albany.Longitude, precision: 4);
    }

    /// <summary>
    /// Verifies that <see cref="CityDataService.GetAlbany"/> returns an instance
    /// that is a <see cref="City"/>, which is itself a <see cref="Coordinates"/>.
    /// </summary>
    [Fact]
    public void GetAlbany_ReturnsCity_WhichIsCoordinates()
    {
        var albany = CityDataService.GetAlbany();

        Assert.IsType<City>(albany);
        Assert.IsAssignableFrom<AirFreightRouter.Models.Coordinates>(albany);
    }

    /// <summary>
    /// Verifies that the full test CSV bundled with the project parses all
    /// 12 city rows (header excluded) without errors.
    /// </summary>
    [Fact]
    public void LoadCitiesFromCsv_TestCitiesCsv_ParsesTwelveCities()
    {
        // Locate the CSV relative to the test assembly's output directory.
        var csvPath = Path.Combine(
            AppContext.BaseDirectory, "Data", "TestCities.csv");

        var cities = CityDataService.LoadCitiesFromCsv(csvPath);

        Assert.Equal(12, cities.Count);
    }
}
