using NetTopologySuite.Geometries;

namespace Bank.Services.Helpers;


public static partial class LocationLinkHelper
{
    public static string? ToGoogleMapsLink(Point? point)
    {
        if (point is null) return null;

        // NetTopologySuite: X = Longitude, Y = Latitude
        return $"https://www.google.com/maps?q={point.Y.ToString("F6", System.Globalization.CultureInfo.InvariantCulture)},{point.X.ToString("F6", System.Globalization.CultureInfo.InvariantCulture)}";
    }
    public static string ToGoogleMapsLink(double latitude, double longitude)
    {
        return $"https://www.google.com/maps?q={latitude.ToString("F6", System.Globalization.CultureInfo.InvariantCulture)},{longitude.ToString("F6", System.Globalization.CultureInfo.InvariantCulture)}";
    }

    public static Point ToPoint(double latitude, double longitude)
    {
        return new Point(longitude, latitude) { SRID = 4326 };
    }
}
