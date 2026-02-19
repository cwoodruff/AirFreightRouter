using System.Windows;
using AirFreightRouter.Models;

namespace AirFreightRouter.Services;

/// <summary>
/// Converts geographic coordinates (latitude / longitude) to WPF canvas pixel
/// coordinates using a simple equirectangular (flat-earth) projection.
/// </summary>
/// <remarks>
/// A constant padding is applied on all four sides so that city dots and labels
/// never clip against the canvas edge.  The longitude range is mapped to the
/// horizontal axis and the latitude range (inverted) to the vertical axis so
/// that north is at the top of the canvas.
/// </remarks>
public sealed class MapRenderer
{
    // ------------------------------------------------------------------
    // Constants
    // ------------------------------------------------------------------

    /// <summary>Pixel inset applied to every edge of the canvas.</summary>
    public const double Padding = 40.0;

    /// <summary>
    /// Minimum allowed span (in degrees) for either axis.  Prevents a
    /// single-city data set from collapsing to a degenerate point.
    /// </summary>
    private const double MinRange = 1.0;

    // ------------------------------------------------------------------
    // State computed once per Initialise call
    // ------------------------------------------------------------------

    private double _minLon, _maxLat;
    private double _lonRange, _latRange;
    private double _drawWidth, _drawHeight;

    // ------------------------------------------------------------------
    // Public API
    // ------------------------------------------------------------------

    /// <summary>
    /// Initialises the renderer with the bounding box of all cities that
    /// will be rendered and the current canvas size.
    /// </summary>
    /// <param name="cities">All cities to be drawn (origin included).</param>
    /// <param name="canvasWidth">Canvas <c>ActualWidth</c> in device-independent pixels.</param>
    /// <param name="canvasHeight">Canvas <c>ActualHeight</c> in device-independent pixels.</param>
    public void Initialise(IEnumerable<City> cities, double canvasWidth, double canvasHeight)
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

        // Guard: ensure a sensible range even for a single city.
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

        _minLon    = minLon;
        _maxLat    = maxLat;
        _lonRange  = lonRange;
        _latRange  = latRange;
        _drawWidth  = canvasWidth  - Padding * 2.0;
        _drawHeight = canvasHeight - Padding * 2.0;
    }

    /// <summary>
    /// Projects a <see cref="City"/> to its 2-D canvas position.
    /// </summary>
    /// <param name="city">The city to project.</param>
    /// <returns>
    /// A <see cref="Point"/> in canvas device-independent pixels where
    /// (0,0) is the top-left corner of the canvas.
    /// </returns>
    public Point Project(City city)
    {
        double x = Padding + (city.Longitude - _minLon) / _lonRange * _drawWidth;
        double y = Padding + (_maxLat - city.Latitude)  / _latRange * _drawHeight;
        return new Point(x, y);
    }
}
