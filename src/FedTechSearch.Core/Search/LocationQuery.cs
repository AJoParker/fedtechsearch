namespace FedTechSearch.Core.Search;

/// <summary>
/// A free-text location such as "Houston, TX, US", normalized to how USAJOBS spells
/// locations: "Houston, Texas", full state names, no country for US postings.
/// </summary>
public sealed record LocationQuery(string? City, string? State, string? Region)
{
    /// <summary>False when the query names a place outside the US (e.g. "Stuttgart, Germany").</summary>
    public bool IsUs => Region is null;

    private static readonly HashSet<string> UsCountry = new(StringComparer.OrdinalIgnoreCase)
    {
        "US", "U.S.", "USA", "U.S.A.", "United States", "United States of America"
    };

    private static readonly Dictionary<string, string> StateCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["AL"] = "Alabama", ["AK"] = "Alaska", ["AZ"] = "Arizona", ["AR"] = "Arkansas", ["CA"] = "California",
        ["CO"] = "Colorado", ["CT"] = "Connecticut", ["DE"] = "Delaware", ["FL"] = "Florida", ["GA"] = "Georgia",
        ["HI"] = "Hawaii", ["ID"] = "Idaho", ["IL"] = "Illinois", ["IN"] = "Indiana", ["IA"] = "Iowa",
        ["KS"] = "Kansas", ["KY"] = "Kentucky", ["LA"] = "Louisiana", ["ME"] = "Maine", ["MD"] = "Maryland",
        ["MA"] = "Massachusetts", ["MI"] = "Michigan", ["MN"] = "Minnesota", ["MS"] = "Mississippi", ["MO"] = "Missouri",
        ["MT"] = "Montana", ["NE"] = "Nebraska", ["NV"] = "Nevada", ["NH"] = "New Hampshire", ["NJ"] = "New Jersey",
        ["NM"] = "New Mexico", ["NY"] = "New York", ["NC"] = "North Carolina", ["ND"] = "North Dakota", ["OH"] = "Ohio",
        ["OK"] = "Oklahoma", ["OR"] = "Oregon", ["PA"] = "Pennsylvania", ["RI"] = "Rhode Island", ["SC"] = "South Carolina",
        ["SD"] = "South Dakota", ["TN"] = "Tennessee", ["TX"] = "Texas", ["UT"] = "Utah", ["VT"] = "Vermont",
        ["VA"] = "Virginia", ["WA"] = "Washington", ["WV"] = "West Virginia", ["WI"] = "Wisconsin", ["WY"] = "Wyoming",
        ["DC"] = "District of Columbia", ["PR"] = "Puerto Rico", ["GU"] = "Guam", ["VI"] = "Virgin Islands",
        ["AS"] = "American Samoa", ["MP"] = "Northern Mariana Islands"
    };

    private static readonly Dictionary<string, string> StateNames = StateCodes.Values
        .Append("Washington DC").Append("Washington D.C.")
        .ToDictionary(n => n, n => n.StartsWith("Washington D") ? "District of Columbia" : n, StringComparer.OrdinalIgnoreCase);

    public static LocationQuery? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        var tokens = text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList();
        if (tokens.Count > 1 && UsCountry.Contains(tokens[^1])) tokens.RemoveAt(tokens.Count - 1);
        if (tokens.Count == 0 || (tokens.Count == 1 && UsCountry.Contains(tokens[0]))) return null;

        if (tokens.Count == 1)
            return ToState(tokens[0]) is { } only ? new LocationQuery(null, only, null) : new LocationQuery(tokens[0], null, null);

        var city = tokens[0];
        var last = tokens[^1];
        return ToState(last) is { } state
            ? new LocationQuery(city, state, null)
            : new LocationQuery(city, null, last);
    }

    private static string? ToState(string token) =>
        StateCodes.TryGetValue(token, out var byCode) ? byCode
        : StateNames.TryGetValue(token, out var byName) ? byName
        : null;
}
