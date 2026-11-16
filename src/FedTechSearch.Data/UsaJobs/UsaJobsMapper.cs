using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using FedTechSearch.Core.Enrichment;

namespace FedTechSearch.Data.UsaJobs;

/// <summary>
/// Maps one USAJOBS SearchResultItems[] element to a RawJob. Works on JsonElement on purpose:
/// the published API docs do not list every field (MajorDuties, TeleworkEligible, HiringPath...),
/// and some fields arrive as a string in one posting and an array in another.
/// </summary>
public static partial class UsaJobsMapper
{
    public static IEnumerable<JsonElement> Items(JsonElement root)
    {
        if (root.TryGetProperty("SearchResult", out var sr) && sr.TryGetProperty("SearchResultItems", out var items)
            && items.ValueKind == JsonValueKind.Array)
            return items.EnumerateArray();
        return Enumerable.Empty<JsonElement>();
    }

    public static int NumberOfPages(JsonElement root)
    {
        if (root.TryGetProperty("SearchResult", out var sr) && sr.TryGetProperty("UserArea", out var ua)
            && Int(ua, "NumberOfPages") is { } pages)
            return pages;
        return 1;
    }

    public static RawJob? Map(JsonElement item)
    {
        if (!item.TryGetProperty("MatchedObjectDescriptor", out var d)) return null;
        var id = Str(item, "MatchedObjectId");
        if (string.IsNullOrWhiteSpace(id)) return null;

        var details = d.TryGetProperty("UserArea", out var ua) && ua.TryGetProperty("Details", out var det)
            ? det
            : default;

        var remuneration = FirstOf(d, "PositionRemuneration");
        var locations = ArrayOf(d, "PositionLocation").ToList();

        var requirements = string.Join("\n",
            new[] { Str(details, "Requirements") }
                .Concat(Strs(details, "KeyRequirements"))
                .Where(s => !string.IsNullOrWhiteSpace(s)));

        return new RawJob
        {
            Id = id!,
            AnnouncementNumber = Str(d, "PositionID") ?? "",
            Title = Clean(Str(d, "PositionTitle")),
            Agency = Str(d, "OrganizationName") ?? "",
            Department = Str(d, "DepartmentName") ?? "",
            SubAgency = NullIfEmpty(Str(details, "SubAgencyName")),
            Url = NormalizeUrl(Str(d, "PositionURI")) ?? "",
            ApplyUrl = NormalizeUrl(Strs(d, "ApplyURI").FirstOrDefault()),
            LocationDisplay = Str(d, "PositionLocationDisplay") ?? "",
            Locations = locations.Select(l => Str(l, "LocationName")).OfType<string>()
                .Where(s => s.Length > 0).Distinct().ToList(),
            States = locations.Select(l => Str(l, "CountrySubDivisionCode")).OfType<string>()
                .Where(s => s.Length > 0).Distinct().ToList(),
            GeoLocations = locations.Select(GeoOf).OfType<GeoLocation>().Distinct().ToList(),
            Series = ArrayOf(d, "JobCategory").Select(c => Str(c, "Code")).OfType<string>()
                .Where(s => s.Length > 0).Distinct().ToList(),
            PayPlan = NullIfEmpty(Str(FirstOf(d, "JobGrade"), "Code")),
            LowGrade = Int(details, "LowGrade"),
            HighGrade = Int(details, "HighGrade"),
            SalaryMin = Dec(remuneration, "MinimumRange"),
            SalaryMax = Dec(remuneration, "MaximumRange"),
            SalaryInterval = NullIfEmpty(Str(remuneration, "RateIntervalCode")),
            OpenDate = Date(d, "PublicationStartDate") ?? Date(d, "PositionStartDate"),
            CloseDate = Date(d, "ApplicationCloseDate") ?? Date(d, "PositionEndDate"),
            Summary = Clean(Str(details, "JobSummary")),
            Duties = Strs(details, "MajorDuties").Select(Clean).Where(s => s.Length > 0).ToList(),
            Qualifications = Clean(Str(d, "QualificationSummary")),
            Requirements = Clean(requirements),
            WhoMayApply = NullIfEmpty(Str(Prop(details, "WhoMayApply"), "Name")),
            HiringPaths = Strs(details, "HiringPath").Select(s => s.Trim().ToLowerInvariant()).Distinct().ToList(),
            TeleworkEligible = Bool(details, "TeleworkEligible"),
            RemoteIndicator = Bool(details, "RemoteIndicator"),
            SecurityClearance = NullIfEmpty(Str(details, "SecurityClearance")),
            Schedule = NullIfEmpty(Str(FirstOf(d, "PositionSchedule"), "Name")),
            OfferingType = NullIfEmpty(Str(FirstOf(d, "PositionOfferingType"), "Name")),
            // Live names are usually blank or free text; the codes are reliable.
            ScheduleType = ScheduleType.FromCode(Str(FirstOf(d, "PositionSchedule"), "Code")),
            AppointmentType = AppointmentType.FromCode(Str(FirstOf(d, "PositionOfferingType"), "Code")),
            TravelRequirement = TravelRequirement.FromCode(Str(details, "TravelCode")),
            // "00" and out-of-range values show up for non-GS pay plans.
            PromotionPotential = Int(details, "PromotionPotential") is >= 1 and <= 15 and var pp ? pp : null,
            HowToApply = Clean(Str(details, "HowToApply")),
        };
    }

    private static readonly Regex PlaceholderLocation = new(
        @"remote job|negotiable|anywhere in", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// A location with usable coordinates, or null. Remote and "negotiable" placeholders and
    /// 0/0 coordinates are skipped so they never match a radius search.
    /// </summary>
    public static GeoLocation? GeoOf(JsonElement location)
    {
        var name = Str(location, "LocationName")?.Trim() ?? "";
        var lat = Dec(location, "Latitude");
        var lon = Dec(location, "Longitude");
        if (name.Length == 0 || lat is null || lon is null || (lat == 0 && lon == 0)) return null;
        if (PlaceholderLocation.IsMatch(name)) return null;
        if (lat is < -90 or > 90 || lon is < -180 or > 180) return null;

        var state = Str(location, "CountrySubDivisionCode")?.Trim() ?? "";
        var country = Str(location, "CountryCode")?.Trim() ?? "";
        return new GeoLocation(name, CityOf(name, state, country), state, country, (double)lat, (double)lon);
    }

    /// <summary>"Pentagon, Arlington, Virginia" → "Arlington": the segment just before the state or country.</summary>
    private static string CityOf(string name, string state, string country)
    {
        var parts = name.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList();
        while (parts.Count > 1 && (parts[^1].Equals(state, StringComparison.OrdinalIgnoreCase)
                                   || parts[^1].Equals(country, StringComparison.OrdinalIgnoreCase)))
            parts.RemoveAt(parts.Count - 1);
        return parts.Count > 0 ? parts[^1] : name;
    }

    // ---- JsonElement helpers -------------------------------------------------------------

    private static JsonElement Prop(JsonElement el, string name) =>
        el.ValueKind == JsonValueKind.Object && el.TryGetProperty(name, out var v) ? v : default;

    private static IEnumerable<JsonElement> ArrayOf(JsonElement el, string name)
    {
        var v = Prop(el, name);
        if (v.ValueKind == JsonValueKind.Array) return v.EnumerateArray();
        return Enumerable.Empty<JsonElement>();
    }

    private static JsonElement FirstOf(JsonElement el, string name) => ArrayOf(el, name).FirstOrDefault();

    private static string? Str(JsonElement el, string name)
    {
        var v = Prop(el, name);
        return v.ValueKind switch
        {
            JsonValueKind.String => v.GetString(),
            JsonValueKind.Number => v.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Array => string.Join("\n", v.EnumerateArray().Select(ElementText).Where(s => s.Length > 0)),
            _ => null
        };
    }

    /// <summary>Reads a field that may be a string, an array of strings, or an array of {Name|Code} objects.</summary>
    private static List<string> Strs(JsonElement el, string name)
    {
        var v = Prop(el, name);
        return v.ValueKind switch
        {
            JsonValueKind.Array => v.EnumerateArray().Select(ElementText).Where(s => s.Length > 0).ToList(),
            JsonValueKind.String when !string.IsNullOrWhiteSpace(v.GetString()) => [v.GetString()!],
            _ => []
        };
    }

    private static string ElementText(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.String => e.GetString() ?? "",
        JsonValueKind.Object => Str(e, "Code") is { Length: > 0 } code ? code : Str(e, "Name") ?? "",
        JsonValueKind.Number => e.GetRawText(),
        _ => ""
    };

    private static int? Int(JsonElement el, string name)
    {
        var s = Str(el, name);
        if (string.IsNullOrWhiteSpace(s)) return null;
        if (int.TryParse(s.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var i)) return i;
        if (decimal.TryParse(s.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out var d)) return (int)d;
        return null;
    }

    private static decimal? Dec(JsonElement el, string name)
    {
        var s = Str(el, name);
        return decimal.TryParse(s?.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out var d) ? d : null;
    }

    private static bool? Bool(JsonElement el, string name)
    {
        var s = Str(el, name)?.Trim().ToLowerInvariant();
        return s switch
        {
            "true" or "yes" or "y" or "1" => true,
            "false" or "no" or "n" or "0" => false,
            _ => null
        };
    }

    private static DateTime? Date(JsonElement el, string name)
    {
        var s = Str(el, name);
        if (string.IsNullOrWhiteSpace(s)) return null;
        // USAJOBS dates have no offset; store them as UTC (Npgsql requires Kind=Utc for timestamptz).
        return DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var dt)
            ? DateTime.SpecifyKind(dt, DateTimeKind.Utc)
            : null;
    }

    /// <summary>USAJOBS returns links like https://www.usajobs.gov:443/job/123; drop the port.</summary>
    public static string? NormalizeUrl(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw) || !Uri.TryCreate(raw.Trim(), UriKind.Absolute, out var uri)) return null;
        return new UriBuilder(uri) { Scheme = Uri.UriSchemeHttps, Port = -1 }.Uri.ToString();
    }

    private static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex HtmlTag();

    [GeneratedRegex(@"[ \t ]+")]
    private static partial Regex Spaces();

    /// <summary>Strips HTML tags and entities that sometimes appear in duty text.</summary>
    public static string Clean(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "";
        var text = HtmlTag().Replace(s, " ");
        text = WebUtility.HtmlDecode(text);
        return Spaces().Replace(text, " ").Trim();
    }
}
