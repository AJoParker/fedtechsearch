using FedTechSearch.Core.Search;

namespace FedTechSearch.Core.Tests;

public sealed class LocationQueryTests
{
    [Theory]
    [InlineData("Houston, TX, US", "Houston", "Texas", null)]
    [InlineData("houston, tx", "houston", "Texas", null)]
    [InlineData("Washington, DC", "Washington", "District of Columbia", null)]
    [InlineData("Fort Meade, Maryland, USA", "Fort Meade", "Maryland", null)]
    [InlineData("Seattle, Washington, United States", "Seattle", "Washington", null)]
    [InlineData("MD", null, "Maryland", null)]
    [InlineData("maryland", null, "Maryland", null)]
    [InlineData("IN", null, "Indiana", null)]
    [InlineData("OR", null, "Oregon", null)]
    [InlineData("ME", null, "Maine", null)]
    [InlineData("Baltimore", "Baltimore", null, null)]
    [InlineData("Stuttgart, Germany", "Stuttgart", null, "Germany")]
    public void Parses_city_state_and_region(string text, string? city, string? state, string? region)
    {
        var loc = LocationQuery.Parse(text);
        Assert.NotNull(loc);
        Assert.Equal(new LocationQuery(city, state, region), loc);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(" , ")]
    [InlineData("USA")]
    public void Empty_or_country_only_means_no_location_filter(string? text) =>
        Assert.Null(LocationQuery.Parse(text));

    [Fact]
    public void Foreign_places_do_not_pull_in_us_remote_jobs()
    {
        Assert.False(LocationQuery.Parse("Stuttgart, Germany")!.IsUs);
        Assert.True(LocationQuery.Parse("Houston, TX")!.IsUs);
    }
}
