namespace FedTechSearch.Data.Alerts;

public sealed class AlertOptions
{
    public const string Section = "Alerts";

    /// <summary>IANA time zone for the 07:00 send time.</summary>
    public string TimeZone { get; set; } = "America/New_York";

    public int MaxSearchesPerSubscriber { get; set; } = 10;

    /// <summary>Confirmation (or sign-in) emails per address per 24 hours; more are silently not sent.</summary>
    public int EmailsPerAddressPerDay { get; set; } = 3;

    /// <summary>Jobs listed in one digest; the email links to the rest.</summary>
    public int DigestSize { get; set; } = 20;
}

public sealed class AppOptions
{
    public const string Section = "App";

    /// <summary>Where the web UI lives, for links in emails (no trailing slash).</summary>
    public string PublicBaseUrl { get; set; } = "http://localhost:8081";
}
