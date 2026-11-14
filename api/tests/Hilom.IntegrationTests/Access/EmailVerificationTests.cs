using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Hilom.Access.Domain;
using Hilom.Infrastructure.Persistence;
using Hilom.IntegrationTests.Infrastructure;
using Hilom.SharedKernel.Auditing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Hilom.IntegrationTests.Access;

/// <summary>
/// Self-registered portal accounts confirm their email with an emailed code before they can ask to be linked
/// to a patient.
/// </summary>
[Collection(ApiTestGroup.Name)]
public sealed class EmailVerificationTests(HilomApiFactory factory)
{
    private const string SendUrl = "/api/access/me/email/code";
    private const string VerifyUrl = "/api/access/me/email/verify";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly object LinkRequest = new { patientName = "Juan Dela Cruz", patientBirthDate = "1958-03-21", relationship = "child" };

    private async Task<(HttpClient Client, Guid Id, string Email)> RegisterAsync()
    {
        var client = factory.CreateBrowserClient();
        await client.PrimeAntiforgeryAsync();
        var email = $"verify.{Guid.NewGuid():N}@family.test";
        var response = await client.PostAsJsonAsync(
            "/api/access/register",
            new { displayName = "Maria Dela Cruz", email, password = "Portal-Password-2026" },
            Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var me = await response.ReadJsonAsync();
        me.GetProperty("mustVerifyEmail").GetBoolean().ShouldBeTrue();
        return (client, Guid.Parse(me.GetProperty("id").GetString()!), email);
    }

    /// <summary>Moves the latest code's send and expiry times into the past, as if time had passed.</summary>
    private async Task AgeCodeAsync(Guid userId, TimeSpan by)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Set<User>().Where(u => u.Id == userId).ExecuteUpdateAsync(
            set => set
                .SetProperty(u => u.EmailCodeSentAt, u => u.EmailCodeSentAt - by)
                .SetProperty(u => u.EmailCodeExpiresAt, u => u.EmailCodeExpiresAt - by),
            Ct);
    }

    [Fact]
    public async Task Registering_emails_a_code_and_links_wait_until_it_is_entered()
    {
        var (client, id, email) = await RegisterAsync();

        var sent = factory.Emails.LastTo(email);
        sent.Subject.ShouldMatch("^Your Hilom code: [0-9]{6}$");
        sent.TextBody.ShouldContain(factory.LatestEmailCode(email));

        var blocked = await client.PostAsJsonAsync("/api/patients/links", LinkRequest, Ct);
        blocked.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await blocked.ProblemCodeAsync()).ShouldBe("email_not_verified");
        // Everything else a portal account does still works.
        (await client.GetAsync("/api/patients/links/mine", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var verified = await client.PostAsJsonAsync(VerifyUrl, new { code = factory.LatestEmailCode(email) }, Ct);
        verified.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await verified.ReadJsonAsync()).GetProperty("mustVerifyEmail").GetBoolean().ShouldBeFalse();

        (await client.PostAsJsonAsync("/api/patients/links", LinkRequest, Ct)).StatusCode.ShouldBe(HttpStatusCode.Created);

        var events = (await factory.AuditEntriesAsync(q => q.Where(e => e.EntityId == id && e.Event.StartsWith("auth.email."))))
            .Select(e => (e.Event, e.Outcome)).ToList();
        events.ShouldContain(("auth.email.code_sent", AuditOutcome.Success));
        events.ShouldContain(("auth.email.verified", AuditOutcome.Success));
        (await factory.AuditEntriesAsync(q => q.Where(e => e.UserId == id && e.Event == "access.denied")))
            .ShouldContain(e => e.Details!.Contains("email_not_verified"));
    }

    [Fact]
    public async Task A_wrong_code_is_refused_and_five_wrong_tries_use_the_code_up()
    {
        var (client, _, email) = await RegisterAsync();
        var right = factory.LatestEmailCode(email);
        var wrong = right == "000000" ? "111111" : "000000";

        var first = await client.PostAsJsonAsync(VerifyUrl, new { code = wrong }, Ct);
        first.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var body = await first.ReadJsonAsync();
        body.GetProperty("code").GetString().ShouldBe("email_code_wrong");
        body.GetProperty("errors").GetProperty("code")[0].GetString().ShouldBe("That code isn't right. Check the email and try again.");

        for (var i = 2; i <= 5; i++)
        {
            await client.PostAsJsonAsync(VerifyUrl, new { code = wrong }, Ct);
        }

        // Even the right code no longer works; a new one has to be sent.
        var usedUp = await client.PostAsJsonAsync(VerifyUrl, new { code = right }, Ct);
        usedUp.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await usedUp.ProblemCodeAsync()).ShouldBe("email_code_used_up");
    }

    [Fact]
    public async Task Codes_expire_and_a_new_one_can_be_sent_once_a_minute()
    {
        var (client, id, email) = await RegisterAsync();
        var first = factory.LatestEmailCode(email);

        var tooSoon = await client.PostAsync(SendUrl, null, Ct);
        tooSoon.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        var wait = await tooSoon.ReadJsonAsync();
        wait.GetProperty("code").GetString().ShouldBe("email_code_cooldown");
        wait.GetProperty("retryAfterSeconds").GetInt32().ShouldBeInRange(1, 60);

        await AgeCodeAsync(id, TimeSpan.FromMinutes(16));
        (await (await client.PostAsJsonAsync(VerifyUrl, new { code = first }, Ct)).ProblemCodeAsync()).ShouldBe("email_code_expired");

        (await client.PostAsync(SendUrl, null, Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var second = factory.LatestEmailCode(email);
        (await client.PostAsJsonAsync(VerifyUrl, new { code = second }, Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);

        // Once confirmed there's nothing more to send.
        (await (await client.PostAsync(SendUrl, null, Ct)).ProblemCodeAsync()).ShouldBe("email_already_verified");
    }

    [Fact]
    public async Task A_code_that_cannot_be_emailed_leaves_the_last_one_working()
    {
        var (client, id, email) = await RegisterAsync();
        var first = factory.LatestEmailCode(email);
        await AgeCodeAsync(id, TimeSpan.FromMinutes(2));
        factory.Emails.FailFor(email);

        var failed = await client.PostAsync(SendUrl, null, Ct);

        failed.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        (await failed.ProblemCodeAsync()).ShouldBe("email_not_sent");
        (await client.PostAsJsonAsync(VerifyUrl, new { code = first }, Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Staff_and_existing_portal_accounts_are_not_asked_to_confirm()
    {
        var (staff, _) = await factory.SignInWithRolesAsync("Front Desk");
        var (portal, _) = await factory.SignInWithRolesAsync("Patient Portal User");

        (await staff.GetFromJsonAsync<JsonElement>("/api/access/me", Ct)).GetProperty("mustVerifyEmail").GetBoolean().ShouldBeFalse();
        (await portal.GetFromJsonAsync<JsonElement>("/api/access/me", Ct)).GetProperty("mustVerifyEmail").GetBoolean().ShouldBeFalse();
        (await portal.PostAsJsonAsync("/api/patients/links", LinkRequest, Ct)).StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    public async Task The_code_must_be_six_digits()
    {
        var (client, _, _) = await RegisterAsync();

        var response = await client.PostAsJsonAsync(VerifyUrl, new { code = "12ab" }, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.ReadJsonAsync()).GetProperty("errors").GetProperty("code")[0].GetString().ShouldBe("The code is 6 digits.");
    }
}
