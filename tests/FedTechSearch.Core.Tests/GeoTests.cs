using System.Text.Json;
using FedTechSearch.Core.Search;
using FedTechSearch.Data.UsaJobs;

namespace FedTechSearch.Core.Tests;

public sealed class GeoTests
{
    [Fact]
    public void Houston_to_Sugar_Land_is_about_20_miles()
    {
        Assert.InRange(Geo.Miles(29.7604, -95.3698, 29.6197, -95.6349), 17, 22);
        Assert.Equal(0, Geo.Miles(29.7604, -95.3698, 29.7604, -95.3698), 6);
    }

    [Theory]
    [InlineData(29.7604, -95.3698, 25)]
    [InlineData(47.6062, -122.3321, 50)]   // farther north: longitude degrees are shorter
    [InlineData(-33.8688, 151.2093, 10)]   // southern hemisphere
    public void Box_contains_every_point_at_the_radius(double lat, double lon, double miles)
    {
        var (minLat, maxLat, minLon, maxLon) = Geo.Box(lat, lon, miles);
        for (var bearing = 0; bearing < 360; bearing += 15)
        {
            var (pLat, pLon) = Destination(lat, lon, bearing, miles * 0.999);
            Assert.InRange(pLat, minLat, maxLat);
            Assert.InRange(pLon, minLon, maxLon);
        }
    }

    [Fact]
    public void Mapper_keeps_real_coordinates_and_derives_the_town()
    {
        var location = Parse("""{ "LocationName": "Pentagon, Arlington, Virginia", "CountryCode": "United States", "CountrySubDivisionCode": "Virginia", "Latitude": 38.8719, "Longitude": -77.0563 }""");
        var geo = UsaJobsMapper.GeoOf(location);

        Assert.NotNull(geo);
        Assert.Equal("Arlington", geo.City);
        Assert.Equal("Virginia", geo.State);
        Assert.Equal(38.8719, geo.Latitude, 4);
    }

    [Theory]
    [InlineData("""{ "LocationName": "Houston, Texas", "Latitude": 0, "Longitude": 0 }""")]
    [InlineData("""{ "LocationName": "Anywhere in the U.S. (remote job)", "Latitude": 39.8, "Longitude": -98.5 }""")]
    [InlineData("""{ "LocationName": "Location Negotiable After Selection", "Latitude": 38.9, "Longitude": -77.0 }""")]
    [InlineData("""{ "LocationName": "Houston, Texas" }""")]
    public void Mapper_skips_placeholders_and_missing_coordinates(string json) =>
        Assert.Null(UsaJobsMapper.GeoOf(Parse(json)));

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private static (double Lat, double Lon) Destination(double lat, double lon, double bearingDeg, double miles)
    {
        var d = miles / Geo.EarthRadiusMiles;
        var b = Geo.Radians(bearingDeg);
        var φ1 = Geo.Radians(lat);
        var λ1 = Geo.Radians(lon);
        var φ2 = Math.Asin(Math.Sin(φ1) * Math.Cos(d) + Math.Cos(φ1) * Math.Sin(d) * Math.Cos(b));
        var λ2 = λ1 + Math.Atan2(Math.Sin(b) * Math.Sin(d) * Math.Cos(φ1), Math.Cos(d) - Math.Sin(φ1) * Math.Sin(φ2));
        return (φ2 * 180 / Math.PI, λ2 * 180 / Math.PI);
    }
}
