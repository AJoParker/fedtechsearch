using System.Text.RegularExpressions;
using FedTechSearch.Core.Model;
using FedTechSearch.Data.Alerts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FedTechSearch.Data.Tests;

[Collection(PostgresCollection.Name)]
public sealed class AlertTests(PostgresFixture db)
{
    private static readonly Regex TokenInLink = new(@"token=([A-Za-z0-9_\-]+)");

    private async Task<T> With<T>(Func<IServiceProvider, Task<T>> work)
    {
        using var scope = db.Services.CreateScope();
        return await work(scope.ServiceProvider);
    }

    private Task With(Func<IServiceProvider, Task> work) => With<int>(async sp => { await work(sp); return 0; });

    /// <summary>Clean alert tables and a clock far past the fixture's FirstSeen times, so no job counts as new.</summary>
    private async Task Reset(DateTime nowUtc)
    {
        db.Clock.SetUtcNow(nowUtc);
        db.Emails.Clear();
        await With(sp => sp.GetRequiredService<AppDbContext>().Subscribers.ExecuteDeleteAsync());
    }

    private Task<string?> Subscribe(string email, string query = "tech=Python", AlertFrequency frequency = AlertFrequency.Daily) =>
        With(sp => sp.GetRequiredService<AlertService>().SubscribeAsync(email, query, "Python jobs", frequency, CancellationToken.None));

    private static string LastToken(PostgresFixture db) => TokenInLink.Match(db.Emails.Sent[^1].Text).Groups[1].Value;

    private Task<string?> Confirm(string token) => With(sp => sp.GetRequiredService<AlertService>().ConfirmAsync(token, CancellationToken.None));

    private Task<DigestRunResult> RunDigests() => With(sp => sp.GetRequiredService<AlertDigestService>().RunAsync(CancellationToken.None));

    private static readonly DateTime Monday0800Eastern = new(2027, 3, 1, 13, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Sign_up_sends_one_confirmation_and_looks_the_same_for_known_addresses()
    {
        await Reset(Monday0800Eastern);

        Assert.Null(await Subscribe("New.Person@Example.com"));
        Assert.Null(await Subscribe("new.person@example.com", "tech=AWS")); // known address: same answer
        Assert.Equal("Enter a valid email address.", await Subscribe("nope"));

        Assert.Equal(2, db.Emails.Sent.Count);
        Assert.All(db.Emails.Sent, m => Assert.Equal("new.person@example.com", m.To));
        Assert.Contains("/alerts/confirm?token=", db.Emails.Sent[0].Text);

        var stored = await With(sp => sp.GetRequiredService<AppDbContext>().AuthTokens.AsNoTracking().ToListAsync());
        Assert.All(stored, t => Assert.DoesNotContain(t.TokenHash, db.Emails.Sent[0].Text)); // only hashes stored
    }

    [Fact]
    public async Task Confirmation_emails_are_capped_per_address_per_day()
    {
        await Reset(Monday0800Eastern);
        for (var i = 0; i < 5; i++) await Subscribe("capped@example.com", $"tech=T{i}");
        Assert.Equal(3, db.Emails.Sent.Count);
    }

    [Fact]
    public async Task Confirm_links_work_once_and_expire()
    {
        await Reset(Monday0800Eastern);
        await Subscribe("once@example.com");
        var token = LastToken(db);

        Assert.NotNull(await Confirm(token));
        Assert.Null(await Confirm(token)); // single use

        await Subscribe("late@example.com");
        var late = LastToken(db);
        db.Clock.Advance(TimeSpan.FromHours(49));
        Assert.Null(await Confirm(late)); // 48 h lifetime
    }

    [Fact]
    public async Task Nothing_is_sent_before_confirmation_and_a_new_job_produces_exactly_one_digest()
    {
        await Reset(Monday0800Eastern);
        await Subscribe("digest@example.com", "q=python");
        db.Emails.Clear();

        db.Clock.Advance(TimeSpan.FromDays(1));
        Assert.Equal(0, (await RunDigests()).Due); // unconfirmed: never due

        await Subscribe("digest@example.com", "q=python"); // resend the confirmation and use it
        Assert.NotNull(await Confirm(LastToken(db)));
        db.Emails.Clear();

        var newJobId = await MakeOneJobNew("python", db.Clock.GetUtcNow().UtcDateTime.AddHours(2));
        try
        {
            db.Clock.Advance(TimeSpan.FromDays(1)); // next morning, 08:00 Eastern
            var first = await RunDigests();
            Assert.Equal(1, first.Sent);
            var digest = Assert.Single(db.Emails.Sent);
            Assert.StartsWith("1 new job", digest.Subject);
            Assert.Contains("/api/alerts/unsubscribe?token=", digest.UnsubscribeUrl);
            Assert.Contains("Manage alerts", digest.Text);

            db.Emails.Clear();
            Assert.Equal(0, (await RunDigests()).Sent); // same morning again: nothing
            Assert.Empty(db.Emails.Sent);
        }
        finally
        {
            await RestoreFirstSeen(newJobId);
        }
    }

    [Fact]
    public async Task A_window_sent_before_a_restart_is_not_sent_again_and_mail_failures_retry()
    {
        await Reset(Monday0800Eastern);
        await Subscribe("restart@example.com", "q=python");
        await Confirm(LastToken(db));
        db.Emails.Clear();
        var newJobId = await MakeOneJobNew("python", db.Clock.GetUtcNow().UtcDateTime.AddHours(2));
        try
        {
            db.Clock.Advance(TimeSpan.FromDays(1));

            db.Emails.FailNext = true;
            var failed = await RunDigests();
            Assert.Equal((1, 0), (failed.Failed, failed.Sent));

            // Simulate "sent, then the worker died before saving the alert": mark the window sent.
            await With(async sp =>
            {
                var context = sp.GetRequiredService<AppDbContext>();
                var delivery = await context.AlertDeliveries.SingleAsync();
                delivery.SentAt = db.Clock.GetUtcNow().UtcDateTime;
                await context.SaveChangesAsync();
            });
            var afterRestart = await RunDigests();
            Assert.Equal(0, afterRestart.Sent);
            Assert.Empty(db.Emails.Sent);
            var alert = await With(sp => sp.GetRequiredService<AppDbContext>().SavedSearches.AsNoTracking().SingleAsync());
            Assert.Equal(db.Clock.GetUtcNow().UtcDateTime, alert.LastNotifiedAt!.Value, TimeSpan.FromSeconds(1));
        }
        finally
        {
            await RestoreFirstSeen(newJobId);
        }
    }

    [Fact]
    public async Task Paused_and_unsubscribed_alerts_get_nothing_and_unsubscribe_is_idempotent()
    {
        await Reset(Monday0800Eastern);
        await Subscribe("paused@example.com", "q=python");
        var session = await Confirm(LastToken(db));
        var subscriberId = (await With(sp => sp.GetRequiredService<AlertService>().SubscriberForSessionAsync(session, CancellationToken.None)))!.Value;
        var alertId = (await With(sp => sp.GetRequiredService<AlertService>().GetAsync(subscriberId, CancellationToken.None)))!.Searches[0].Id;
        await With(sp => sp.GetRequiredService<AlertService>().UpdateAsync(subscriberId, alertId, new SearchPatch(null, null, true), CancellationToken.None));

        db.Clock.Advance(TimeSpan.FromDays(1));
        Assert.Equal(0, (await RunDigests()).Due);

        // Unsubscribe link: valid, repeatable, and stops everything.
        var unsubscribe = await With(sp =>
        {
            var raw = sp.GetRequiredService<AlertTokens>().Issue(subscriberId, TokenPurpose.Unsubscribe);
            return sp.GetRequiredService<AppDbContext>().SaveChangesAsync().ContinueWith(_ => raw);
        });
        Assert.True(await With(sp => sp.GetRequiredService<AlertService>().UnsubscribeAsync(unsubscribe, CancellationToken.None)));
        Assert.True(await With(sp => sp.GetRequiredService<AlertService>().UnsubscribeAsync(unsubscribe, CancellationToken.None)));
        await With(sp => sp.GetRequiredService<AlertService>().UpdateAsync(subscriberId, alertId, new SearchPatch(null, null, false), CancellationToken.None));
        db.Clock.Advance(TimeSpan.FromDays(1));
        Assert.Equal(0, (await RunDigests()).Due);
    }

    [Fact]
    public async Task Deleting_a_subscriber_removes_everything()
    {
        await Reset(Monday0800Eastern);
        await Subscribe("gone@example.com");
        var session = await Confirm(LastToken(db));
        var id = (await With(sp => sp.GetRequiredService<AlertService>().SubscriberForSessionAsync(session, CancellationToken.None)))!.Value;

        await With(sp => sp.GetRequiredService<AlertService>().DeleteSubscriberAsync(id, CancellationToken.None));

        var counts = await With(async sp =>
        {
            var c = sp.GetRequiredService<AppDbContext>();
            return (await c.Subscribers.CountAsync(), await c.SavedSearches.CountAsync(), await c.AuthTokens.CountAsync(t => t.SubscriberId == id));
        });
        Assert.Equal((0, 0, 0), counts);
    }

    /// <summary>Gives one job matching the search a FirstSeen inside the next digest window.</summary>
    private Task<string> MakeOneJobNew(string q, DateTime firstSeen) => With(async sp =>
    {
        var context = sp.GetRequiredService<AppDbContext>();
        var job = await context.Jobs.Where(j => EF.Functions.ToTsVector("english", j.SearchText).Matches(EF.Functions.WebSearchToTsQuery("english", q)))
            .OrderBy(j => j.Id).FirstAsync();
        _original[job.Id] = job.FirstSeen;
        job.FirstSeen = firstSeen;
        await context.SaveChangesAsync();
        return job.Id;
    });

    private readonly Dictionary<string, DateTime> _original = [];

    private Task RestoreFirstSeen(string jobId) => With(async sp =>
    {
        var context = sp.GetRequiredService<AppDbContext>();
        var job = await context.Jobs.FirstAsync(j => j.Id == jobId);
        job.FirstSeen = _original[jobId];
        await context.SaveChangesAsync();
    });
}
