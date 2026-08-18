using Avalonia;
using AirFreightRouter.Models;

namespace AirFreightRouter.Services;

/// <summary>
/// Converts geographic coordinates (latitude / longitude) to Avalonia canvas pixel
/// coordinates using a simple equirectangular (flat-earth) projection.
/// </summary>
public sealed class MapRenderer
{
    public const double Padding  = 40.0;
    private const double MinRange = 1.0;

    private double _minLon, _maxLat;
    private double _lonRange, _latRange;
    private double _drawWidth, _drawHeight;

    public void Initialize(IEnumerable<City> cities, double canvasWidth, double canvasHeight)
    {
        double minLat = double.MaxValue, maxLat = double.MinValue;
        double minLon = double.MaxValue, maxLon = double.MinValue;

        foreach (var c in cities)
        {
            if (c.Latitude  < minLat) minLat = c.Latitude;
            if (c.Latitude  > maxLat) maxLat = c.Latitude;
            if (c.Longitude < minLon) minLon = c.Longitude;
            if (c.Longitude > maxLon) maxLon = c.Longitude;
        }

        double latRange = maxLat - minLat;
        double lonRange = maxLon - minLon;

        if (latRange < MinRange)
        {
            double centre = (minLat + maxLat) / 2.0;
            minLat = centre - MinRange / 2.0;
            maxLat = centre + MinRange / 2.0;
            latRange = MinRange;
        }

        if (lonRange < MinRange)
        {
            double centre = (minLon + maxLon) / 2.0;
            minLon = centre - MinRange / 2.0;
            maxLon = centre + MinRange / 2.0;
            lonRange = MinRange;
        }

        _minLon     = minLon;
        _maxLat     = maxLat;
        _lonRange   = lonRange;
        _latRange   = latRange;
        _drawWidth  = canvasWidth  - Padding * 2.0;
        _drawHeight = canvasHeight - Padding * 2.0;
    }

    public Point Project(City city)
    {
        double x = Padding + (city.Longitude - _minLon) / _lonRange * _drawWidth;
        double y = Padding + (_maxLat - city.Latitude)  / _latRange * _drawHeight;
        return new Point(x, y);
    }
}
