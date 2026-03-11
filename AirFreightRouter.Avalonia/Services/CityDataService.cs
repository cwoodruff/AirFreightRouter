using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using AirFreightRouter.Models;

namespace AirFreightRouter.Services;

/// <summary>
/// Provides methods for loading city data from external sources.
/// </summary>
public static class CityDataService
{
    /// <summary>
    /// Parses a CSV file and returns a list of <see cref="City"/> objects.
    /// </summary>
    /// <remarks>
    /// Expected column order: <c>City, State, Latitude, Longitude</c>.
    /// Header rows, blank lines, and malformed lines are skipped with a
    /// <see cref="Debug.WriteLine"/> warning. Latitude and longitude are
    /// parsed using <see cref="CultureInfo.InvariantCulture"/>.
    /// </remarks>
    /// <param name="filePath">Absolute or relative path to the CSV file.</param>
    /// <returns>A list of successfully parsed <see cref="City"/> instances.</returns>
    public static List<City> LoadCitiesFromCsv(string filePath) =>
        LoadCitiesFromLines(File.ReadLines(filePath));

    /// <summary>
    /// Parses city data from a <see cref="Stream"/> (e.g. from Avalonia's StorageProvider).
    /// </summary>
    public static List<City> LoadCitiesFromStream(Stream stream)
    {
        using var reader = new StreamReader(stream);
        var lines = new List<string>();
        while (reader.ReadLine() is { } line)
            lines.Add(line);
        return LoadCitiesFromLines(lines);
    }

    private static List<City> LoadCitiesFromLines(IEnumerable<string> lines)
    {
        var cities = new List<City>();

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();

            if (string.IsNullOrWhiteSpace(line))
            {
                Debug.WriteLine("[CityDataService] Skipping blank line.");
                continue;
            }

            var parts = line.Split(',');

            if (parts.Length < 4)
            {
                Debug.WriteLine($"[CityDataService] Skipping malformed line (expected ≥4 fields): \"{line}\"");
                continue;
            }

            var name  = parts[0].Trim();
            var state = parts[1].Trim();
            var latStr = parts[2].Trim();
            var lonStr = parts[3].Trim();

            if (!double.TryParse(latStr, NumberStyles.Float, CultureInfo.InvariantCulture, out double lat))
            {
                Debug.WriteLine($"[CityDataService] Skipping line with invalid latitude \"{latStr}\": \"{line}\"");
                continue;
            }

            if (!double.TryParse(lonStr, NumberStyles.Float, CultureInfo.InvariantCulture, out double lon))
            {
                Debug.WriteLine($"[CityDataService] Skipping line with invalid longitude \"{lonStr}\": \"{line}\"");
                continue;
            }

            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(state))
            {
                Debug.WriteLine($"[CityDataService] Skipping line with empty name or state: \"{line}\"");
                continue;
            }

            cities.Add(new City(name, state, lat, lon));
        }

        return cities;
    }

    /// <summary>
    /// Returns the hardcoded origin/destination city: Albany, NY.
    /// </summary>
    /// <returns>A <see cref="City"/> representing Albany, NY.</returns>
    public static City GetAlbany() =>
        new City("Albany", "NY", 42.6526, -73.7562);
}
