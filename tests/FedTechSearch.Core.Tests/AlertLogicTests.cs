using FedTechSearch.Core.Model;
using FedTechSearch.Data.Alerts;
using FedTechSearch.Data.Email;

namespace FedTechSearch.Core.Tests;

public sealed class AlertLogicTests
{
    private static readonly TimeZoneInfo Eastern = AlertSchedule.Zone("America/New_York");

    private static DateTime Utc(int month, int day, int hour, int minute = 0) => new(2026, month, day, hour, minute, 0, DateTimeKind.Utc);

    [Fact]
    public void Query_strings_parse_into_requests_and_back_to_one_canonical_form()
    {
        var request = AlertQuery.Parse("?tech=Python&tech=AWS&location=Houston,%20TX&radius=50&minSalary=120000&directHire=true&sort=salary&page=3&hide=123&junk=1");

        Assert.Equal(["Python", "AWS"], request.Tech!);
        Assert.Equal("Houston, TX", request.Location);
        Assert.Equal(50, request.Radius);
        Assert.Equal(120000m, request.MinSalary);
        Assert.True(request.DirectHire);
        Assert.Null(request.Sort);   // the digest sorts
        Assert.Null(request.Page);
        Assert.Null(request.Hide);   // hidden posts live only in the browser

        var canonical = AlertQuery.Canonicalize(request);
        Assert.Equal("location=Houston%2C%20TX&radius=50&tech=Python&tech=AWS&minSalary=120000&directHire=true", canonical);
        Assert.Equal(canonical, AlertQuery.Canonicalize(canonical)); // stable
    }

    [Fact]
    public void New_since_cannot_be_set_from_a_query_string()
    {
        Assert.Null(AlertQuery.Parse("newSince=2020-01-01").NewSince);
    }

    [Theory]
    [InlineData(11, 24, 11, 59, false)] // 06:59 Eastern (UTC-5): not yet
    [InlineData(11, 24, 12, 0, true)]   // 07:00 Eastern
    [InlineData(11, 24, 20, 0, true)]
    public void Daily_alerts_are_due_from_seven_am_local(int month, int day, int hour, int minute, bool due)
    {
        var lastNotified = Utc(11, 23, 12, 0); // yesterday 07:00 Eastern
        Assert.Equal(due, AlertSchedule.IsDue(AlertFrequency.Daily, lastNotified, Utc(month, day, hour, minute), Eastern));
    }

    [Fact]
    public void A_daily_alert_sent_this_morning_is_not_due_again_today()
    {
        Assert.False(AlertSchedule.IsDue(AlertFrequency.Daily, Utc(11, 24, 12, 5), Utc(11, 24, 23, 0), Eastern));
    }

    [Fact]
    public void Weekly_alerts_are_due_on_monday_morning_only()
    {
        var lastWeek = Utc(11, 16, 12, 0); // Monday Nov 16, 07:00 Eastern
        Assert.False(AlertSchedule.IsDue(AlertFrequency.Weekly, lastWeek, Utc(11, 22, 20, 0), Eastern)); // Sunday
        Assert.True(AlertSchedule.IsDue(AlertFrequency.Weekly, lastWeek, Utc(11, 23, 12, 0), Eastern));  // Monday 07:00
        Assert.True(AlertSchedule.IsDue(AlertFrequency.Weekly, lastWeek, Utc(11, 24, 15, 0), Eastern));  // Tuesday, missed Monday
    }

    [Fact]
    public void Seven_am_follows_daylight_saving_time()
    {
        // 2026-03-08 springs forward: 07:00 EDT is 11:00 UTC (it was 12:00 UTC the day before).
        Assert.Equal(Utc(3, 8, 11), AlertSchedule.MostRecentSendTimeUtc(AlertFrequency.Daily, Utc(3, 8, 11, 30), Eastern));
        Assert.Equal(Utc(3, 7, 12), AlertSchedule.MostRecentSendTimeUtc(AlertFrequency.Daily, Utc(3, 8, 10, 59), Eastern));
    }

    [Fact]
    public void A_new_alert_waits_for_the_next_send_time()
    {
        var confirmed = Utc(11, 24, 15, 0); // 10:00 Eastern, after today's send time
        Assert.False(AlertSchedule.IsDue(AlertFrequency.Daily, confirmed, Utc(11, 24, 23, 0), Eastern));
        Assert.True(AlertSchedule.IsDue(AlertFrequency.Daily, confirmed, Utc(11, 25, 12, 0), Eastern));
        Assert.False(AlertSchedule.IsDue(AlertFrequency.Daily, null, Utc(11, 25, 12, 0), Eastern)); // never confirmed
    }

    [Fact]
    public void Emails_have_text_and_html_parts_and_one_click_unsubscribe_headers()
    {
        var mime = MimeBuilder.Build(
            new EmailMessage("someone@example.com", "3 new jobs", "<p>Hello</p>", "Hello", "https://example.org/api/alerts/unsubscribe?token=abc"),
            "FedTech Search <alerts@example.org>");

        Assert.Equal("<https://example.org/api/alerts/unsubscribe?token=abc>", mime.Headers["List-Unsubscribe"]);
        Assert.Equal("List-Unsubscribe=One-Click", mime.Headers["List-Unsubscribe-Post"]);
        Assert.Equal("Hello", mime.TextBody!.Trim());
        Assert.Equal("<p>Hello</p>", mime.HtmlBody!.Trim());
    }

    [Theory]
    [InlineData(" Someone@Example.com ", "someone@example.com")]
    [InlineData("not-an-email", null)]
    [InlineData("Name <a@b.com>", null)]
    [InlineData("a@localhost", null)]
    public void Email_addresses_are_normalized_or_rejected(string input, string? expected) =>
        Assert.Equal(expected, AlertService.NormalizeEmail(input));

    [Fact]
    public void Digest_links_only_point_at_usajobs()
    {
        Assert.Equal("https://www.usajobs.gov/job/123", EmailTemplates.UsajobsLink("https://www.usajobs.gov:443/job/123"));
        Assert.Null(EmailTemplates.UsajobsLink("https://evil.example/job/123"));
    }
}
