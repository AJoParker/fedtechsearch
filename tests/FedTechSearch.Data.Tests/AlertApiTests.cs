using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using FedTechSearch.Data.Email;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FedTechSearch.Data.Tests;

[Collection(PostgresCollection.Name)]
public sealed class AlertApiTests(PostgresFixture db)
{
    private static readonly Regex TokenInLink = new(@"token=([A-Za-z0-9_\-]+)");

    private WebApplicationFactory<Program> Api(int perHour = 1000)
    {
        Environment.SetEnvironmentVariable("ConnectionStrings__Jobs", db.ConnectionString);
        return new WebApplicationFactory<Program>().WithWebHostBuilder(b => b
            .UseSetting("Alerts:RateLimitPerHour", perHour.ToString())
            .ConfigureServices(s => s.AddSingleton<IEmailSender>(db.Emails)));
    }

    private async Task Reset()
    {
        db.Clock.SetUtcNow(new DateTimeOffset(2027, 6, 1, 15, 0, 0, TimeSpan.Zero));
        db.Emails.Clear();
        using var scope = db.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Subscribers.ExecuteDeleteAsync();
    }

    private static object Signup(string email, string query = "tech=Python") =>
        new { email, queryString = query, name = "Python jobs", frequency = "Daily" };

    private async Task<string> SignUpAndConfirm(HttpClient client, string email)
    {
        await client.PostAsJsonAsync("/api/alerts", Signup(email));
        var token = TokenInLink.Match(db.Emails.Sent[^1].Text).Groups[1].Value;
        var confirmed = await client.PostAsJsonAsync("/api/alerts/confirm", new { token });
        return (await confirmed.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("session").GetString()!;
    }

    [Fact]
    public async Task Sign_up_answers_the_same_for_new_and_known_addresses_and_is_rate_limited()
    {
        await Reset();
        using var api = Api(perHour: 3);
        var client = api.CreateClient();

        var first = await client.PostAsJsonAsync("/api/alerts", Signup("same@example.com"));
        var again = await client.PostAsJsonAsync("/api/alerts", Signup("same@example.com"));
        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
        Assert.Equal(first.StatusCode, again.StatusCode);
        Assert.Equal(await first.Content.ReadAsStringAsync(), await again.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.Accepted, (await client.PostAsJsonAsync("/api/alerts/manage-link", new { email = "nobody@example.com" })).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.PostAsJsonAsync("/api/alerts", Signup("x@example.com"))).StatusCode);
    }

    [Fact]
    public async Task Confirm_gives_a_session_that_reads_and_edits_only_your_alerts()
    {
        await Reset();
        using var api = Api();
        var client = api.CreateClient();

        var mine = await SignUpAndConfirm(client, "mine@example.com");
        var theirs = await SignUpAndConfirm(client, "theirs@example.com");

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", mine);
        var view = await client.GetFromJsonAsync<JsonElement>("/api/alerts");
        Assert.Equal("mine@example.com", view.GetProperty("email").GetString());
        var alert = view.GetProperty("searches")[0];
        Assert.Equal("Daily", alert.GetProperty("frequency").GetString());
        Assert.True(alert.GetProperty("confirmed").GetBoolean());
        var myAlertId = alert.GetProperty("id").GetString();

        var renamed = await client.PatchAsJsonAsync($"/api/alerts/searches/{myAlertId}", new { name = "Renamed", frequency = "Weekly" });
        Assert.Equal("Weekly", (await renamed.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("frequency").GetString());

        // Their session can't see or touch my alert.
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", theirs);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PatchAsJsonAsync($"/api/alerts/searches/{myAlertId}", new { name = "Hijacked" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync($"/api/alerts/searches/{myAlertId}")).StatusCode);

        client.DefaultRequestHeaders.Authorization = null;
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/alerts")).StatusCode);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "made-up");
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/alerts")).StatusCode);
    }

    [Fact]
    public async Task Sign_in_links_and_one_click_unsubscribe_work_through_the_api()
    {
        await Reset();
        using var api = Api();
        var client = api.CreateClient();
        await SignUpAndConfirm(client, "link@example.com");

        await client.PostAsJsonAsync("/api/alerts/manage-link", new { email = "link@example.com" });
        var manage = TokenInLink.Match(db.Emails.Sent[^1].Text).Groups[1].Value;
        var session = await client.PostAsJsonAsync("/api/alerts/session", new { token = manage });
        Assert.Equal(HttpStatusCode.OK, session.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/alerts/session", new { token = manage })).StatusCode); // single use

        // RFC 8058: the mail provider POSTs "List-Unsubscribe=One-Click" as a form body.
        string unsubscribe;
        using (var scope = db.Services.CreateScope())
        {
            var subscriber = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Subscribers.SingleAsync();
            unsubscribe = scope.ServiceProvider.GetRequiredService<Alerts.AlertTokens>().Issue(subscriber.Id, Core.Model.TokenPurpose.Unsubscribe);
            await scope.ServiceProvider.GetRequiredService<AppDbContext>().SaveChangesAsync();
        }
        var form = new FormUrlEncodedContent([new KeyValuePair<string, string>("List-Unsubscribe", "One-Click")]);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync($"/api/alerts/unsubscribe?token={unsubscribe}", form)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync($"/api/alerts/unsubscribe?token={unsubscribe}", form)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/api/alerts/unsubscribe?token=bogus", form)).StatusCode);
    }
}
