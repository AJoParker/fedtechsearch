namespace FedTechSearch.Core.Search;

/// <summary>Great-circle distance in miles, plus a lat/lon box for cheap prefiltering.</summary>
public static class Geo
{
    public const double EarthRadiusMiles = 3958.8;
    private const double MilesPerDegreeLatitude = 69.0;

    public static double Miles(double lat1, double lon1, double lat2, double lon2)
    {
        var dLat = Radians(lat2 - lat1);
        var dLon = Radians(lon2 - lon1);
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
                + Math.Cos(Radians(lat1)) * Math.Cos(Radians(lat2)) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return 2 * EarthRadiusMiles * Math.Asin(Math.Min(1, Math.Sqrt(a)));
    }

    /// <summary>A box that contains every point within <paramref name="miles"/> (and some corners beyond).</summary>
    public static (double MinLat, double MaxLat, double MinLon, double MaxLon) Box(double lat, double lon, double miles)
    {
        var dLat = miles / MilesPerDegreeLatitude;
        // Longitude degrees shrink toward the poles; clamp so the box stays finite near them.
        var dLon = miles / (MilesPerDegreeLatitude * Math.Max(0.01, Math.Cos(Radians(lat))));
        return (lat - dLat, lat + dLat, lon - dLon, lon + dLon);
    }

    public static double Radians(double degrees) => degrees * Math.PI / 180;
}
