using System.Globalization;
using System.Net;
using System.Text;
using FedTechSearch.Data.Search;

namespace FedTechSearch.Data.Email;

/// <summary>Links every alert email carries.</summary>
public sealed record EmailLinks(string Manage, string Unsubscribe, string Privacy);

/// <summary>
/// The alert emails: plain, accessible HTML (real headings, no images, inline styles in the site's
/// navy and red) plus a text part. Everything from users or postings is HTML-encoded.
/// </summary>
public static class EmailTemplates
{
    private const string Navy = "#0a3161";
    private const string Red = "#b31942";
    private const string Ink = "#14213d";
    private const string Muted = "#4f5b6e";

    public static (string Subject, string Html, string Text) Confirm(string alertName, string confirmUrl, string privacyUrl)
    {
        var subject = $"Confirm your FedTech Search alert: {alertName}";
        var html = Layout(subject, $"""
            <h1 style="font-size:20px;color:{Navy};margin:0 0 12px">Confirm your job alert</h1>
            <p style="margin:0 0 12px">Someone (hopefully you) asked for email alerts for <strong>{E(alertName)}</strong> on FedTech Search.</p>
            <p style="margin:0 0 20px">{Button(confirmUrl, "Confirm this alert")}</p>
            <p style="margin:0 0 8px;color:{Muted};font-size:13px">Nothing is sent until you confirm. If you didn't ask for this, ignore this email and nothing will happen. The link works for 48 hours.</p>
            """, privacyUrl, unsubscribeUrl: null);
        var text = $"""
            Confirm your job alert

            Someone (hopefully you) asked for email alerts for "{alertName}" on FedTech Search.

            Confirm this alert: {confirmUrl}

            Nothing is sent until you confirm. If you didn't ask for this, ignore this email. The link works for 48 hours.

            Privacy: {privacyUrl}
            """;
        return (subject, html, text);
    }

    public static (string Subject, string Html, string Text) ManageLink(string manageUrl, string privacyUrl)
    {
        const string subject = "Your FedTech Search sign-in link";
        var html = Layout(subject, $"""
            <h1 style="font-size:20px;color:{Navy};margin:0 0 12px">Manage your job alerts</h1>
            <p style="margin:0 0 20px">{Button(manageUrl, "Open my alerts")}</p>
            <p style="margin:0;color:{Muted};font-size:13px">The link works once, for one hour. If you didn't ask for it, you can ignore this email.</p>
            """, privacyUrl, unsubscribeUrl: null);
        var text = $"""
            Manage your job alerts

            Open my alerts: {manageUrl}

            The link works once, for one hour. If you didn't ask for it, you can ignore this email.

            Privacy: {privacyUrl}
            """;
        return (subject, html, text);
    }

    public static (string Subject, string Html, string Text) Digest(string alertName, IReadOnlyList<JobListItem> jobs, int total, string seeAllUrl, EmailLinks links)
    {
        var count = total == 1 ? "1 new job" : $"{total} new jobs";
        var subject = $"{count}: {alertName}";

        var items = new StringBuilder();
        var textItems = new StringBuilder();
        foreach (var job in jobs)
        {
            var link = UsajobsLink(job.Url);
            var salary = Salary(job);
            var closes = job.CloseDate is { } c ? $"Closes {c.ToString("MMM d", CultureInfo.InvariantCulture)}" : null;
            var meta = string.Join(" · ", new[] { job.Agency, job.LocationDisplay, salary, closes }.Where(s => !string.IsNullOrWhiteSpace(s)));
            items.Append($"""
                <li style="margin:0 0 16px;padding:0 0 16px;border-bottom:1px solid #dcdfe5;list-style:none">
                  <h2 style="font-size:16px;margin:0 0 2px;color:{Ink}">{(link is null ? E(job.WorkingTitle) : $"<a href=\"{E(link)}\" style=\"color:{Navy}\">{E(job.WorkingTitle)}</a>")}</h2>
                  <p style="margin:0 0 4px;color:{Muted};font-size:13px">{E(job.OfficialTitle)}</p>
                  <p style="margin:0;font-size:14px">{E(meta)}</p>
                </li>
                """);
            textItems.Append($"- {job.WorkingTitle} ({job.OfficialTitle})\n  {meta}\n");
            if (link is not null) textItems.Append($"  {link}\n");
            textItems.Append('\n');
        }

        var more = total > jobs.Count ? $"See all {total} on FedTech Search" : "Open this search on FedTech Search";
        var html = Layout(subject, $"""
            <h1 style="font-size:20px;color:{Navy};margin:0 0 4px">{E(count)}</h1>
            <p style="margin:0 0 20px;color:{Muted}">for your alert <strong style="color:{Ink}">{E(alertName)}</strong></p>
            <ul style="margin:0;padding:0">{items}</ul>
            <p style="margin:8px 0 0">{Button(seeAllUrl, more)}</p>
            <p style="margin:20px 0 0;font-size:13px"><a href="{E(links.Manage)}" style="color:{Navy}">Manage alerts</a></p>
            """, links.Privacy, links.Unsubscribe);
        var text = $"""
            {count} for your alert "{alertName}"

            {textItems}{more}: {seeAllUrl}

            Manage alerts: {links.Manage}
            Unsubscribe from all alerts: {links.Unsubscribe}
            Privacy: {links.Privacy}
            """;
        return (subject, html, text);
    }

    /// <summary>Only real USAJOBS pages, as https without the :443 the API includes (like the web UI's usajobsLink).</summary>
    public static string? UsajobsLink(string? raw)
    {
        if (!Uri.TryCreate(raw, UriKind.Absolute, out var uri)) return null;
        if (uri.Host != "usajobs.gov" && !uri.Host.EndsWith(".usajobs.gov", StringComparison.Ordinal)) return null;
        return new UriBuilder(uri) { Scheme = "https", Port = -1 }.Uri.ToString();
    }

    private static string? Salary(JobListItem job) => job.SalaryMin is { } min
        ? job.SalaryMax is { } max && max != min
            ? $"{min.ToString("C0", CultureInfo.GetCultureInfo("en-US"))}–{max.ToString("C0", CultureInfo.GetCultureInfo("en-US"))}"
            : min.ToString("C0", CultureInfo.GetCultureInfo("en-US"))
        : null;

    private static string Button(string url, string label) =>
        $"""<a href="{E(url)}" style="display:inline-block;padding:10px 18px;border-radius:999px;background:{Navy};color:#ffffff;font-weight:600;text-decoration:none">{E(label)}</a>""";

    private static string Layout(string title, string body, string privacyUrl, string? unsubscribeUrl)
    {
        var unsubscribe = unsubscribeUrl is null ? "" : $"""<a href="{E(unsubscribeUrl)}" style="color:{Muted}">Unsubscribe from all alerts</a> · """;
        return $"""
            <!doctype html>
            <html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width"><title>{E(title)}</title></head>
            <body style="margin:0;padding:0;background:#f7f6f2;font-family:'Public Sans',Arial,sans-serif;color:{Ink};line-height:1.5">
              <div style="max-width:600px;margin:0 auto;padding:24px 16px">
                <div style="padding:14px 20px;background:{Navy};color:#f5f5f5;font-weight:800;font-size:18px;border-bottom:4px solid {Red};border-radius:10px 10px 0 0">&#9733; FedTech Search</div>
                <div style="padding:24px 20px;background:#ffffff;border:1px solid #dcdfe5;border-top:0;border-radius:0 0 10px 10px">
                  {body}
                </div>
                <p style="margin:16px 0 0;font-size:12px;color:{Muted};text-align:center">{unsubscribe}<a href="{E(privacyUrl)}" style="color:{Muted}">Privacy</a></p>
              </div>
            </body></html>
            """;
    }

    private static string E(string? value) => WebUtility.HtmlEncode(value ?? "");
}
