using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Hilom.Access.Domain;
using Hilom.IntegrationTests.Infrastructure;
using Hilom.SharedKernel.Auditing;
using Npgsql;

namespace Hilom.IntegrationTests.Workforce;

/// <summary>Staff invites: HR sends one, the employee sets a password and gets an account with the invite's roles.</summary>
[Collection(ApiTestGroup.Name)]
public sealed partial class InviteTests(HilomApiFactory factory)
{
    private const string NewPassword = "Welcome-To-Hilom-2026";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<HttpClient> AdminAsync()
    {
        var admin = factory.CreateBrowserClient();
        await admin.SignInAsync(HilomApiFactory.AdminEmail, HilomApiFactory.AdminPassword);
        return admin;
    }

    private static async Task<string> LookupAsync(HttpClient admin, string list, string name)
    {
        var response = await admin.PostAsJsonAsync($"/api/workforce/{list}", new { name = $"{name} {Guid.NewGuid():N}"[..(name.Length + 9)] }, Ct);
        return (await response.ReadJsonAsync()).GetProperty("id").GetString()!;
    }

    /// <summary>Creates a nurse and returns their id.</summary>
    private static async Task<string> NurseAsync(HttpClient admin, string lastName = "Reyes")
    {
        var response = await admin.PostAsJsonAsync(
            "/api/workforce/employees",
            new
            {
                firstName = "Ana",
                lastName,
                sex = "female",
                birthDate = "1995-04-12",
                departmentId = await LookupAsync(admin, "departments", "Nursing"),
                positionId = await LookupAsync(admin, "positions", "Staff Nurse"),
                professionalType = "nurse",
                licenseNumber = "0456789",
                employmentType = "full_time",
                hiredOn = "2024-06-01",
            },
            Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await response.ReadJsonAsync()).GetProperty("id").GetString()!;
    }

    private static string NewEmail() => $"nurse-{Guid.NewGuid():N}@hilom.test";

    private async Task<HttpResponseMessage> SendAsync(HttpClient client, string employeeId, string email, params string[] roles)
    {
        var roleIds = new List<Guid>();
        foreach (var role in roles)
        {
            roleIds.Add(await factory.RoleIdAsync(role));
        }

        return await client.PostAsJsonAsync($"/api/workforce/employees/{employeeId}/invites", new { email, roleIds }, Ct);
    }

    /// <summary>The token from the link in the last invite email to this address.</summary>
    private string TokenSentTo(string email)
    {
        var message = factory.Emails.LastTo(email);
        var match = InviteLink().Match(message.TextBody);
        match.Success.ShouldBeTrue("the email contains the invite link");
        return match.Groups["token"].Value;
    }

    private async Task<HttpClient> AnonymousAsync()
    {
        var client = factory.CreateBrowserClient();
        await client.PrimeAntiforgeryAsync();
        return client;
    }

    private async Task SetExpiresAtAsync(string email, DateTimeOffset expiresAt)
    {
        await using var connection = new NpgsqlConnection(factory.ConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = new NpgsqlCommand("update workforce.staff_invites set expires_at = @at where email = @email", connection);
        command.Parameters.AddWithValue("at", expiresAt);
        command.Parameters.AddWithValue("email", email);
        (await command.ExecuteNonQueryAsync(Ct)).ShouldBe(1);
    }

    private Task<List<Hilom.Infrastructure.Auditing.AuditEntry>> InviteEventsAsync(string employeeId, AuditEvent auditEvent)
    {
        var id = Guid.Parse(employeeId);
        return factory.AuditEntriesAsync(q => q.Where(e => e.EntityId == id && e.Event == auditEvent.Key));
    }

    [Fact]
    public async Task An_accepted_invite_creates_a_linked_account_with_the_invites_roles()
    {
        var (hr, hrUser) = await factory.SignInWithRolesAsync("HR Officer");
        var employeeId = await NurseAsync(await AdminAsync());
        var email = NewEmail();

        var sent = await SendAsync(hr, employeeId, email, "Staff Nurse");
        sent.StatusCode.ShouldBe(HttpStatusCode.Created);
        var message = factory.Emails.LastTo(email);
        message.Subject.ShouldContain("Set up your");
        message.TextBody.ShouldContain("Hello Ana");

        var listed = await hr.GetFromJsonAsync<JsonElement>($"/api/workforce/employees/{employeeId}/invites", Ct);
        listed[0].GetProperty("state").GetString().ShouldBe("pending");
        listed[0].GetProperty("roles")[0].GetProperty("name").GetString().ShouldBe("Staff Nurse");
        listed[0].GetProperty("sentBy").GetString().ShouldBe(hrUser.DisplayName);

        var token = TokenSentTo(email);
        var visitor = await AnonymousAsync();
        var lookup = await visitor.PostAsJsonAsync("/api/workforce/invites/lookup", new { token }, Ct);
        (await lookup.ReadJsonAsync()).GetProperty("firstName").GetString().ShouldBe("Ana");

        var accepted = await visitor.PostAsJsonAsync("/api/workforce/invites/accept", new { token, password = NewPassword }, Ct);
        accepted.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await accepted.ReadJsonAsync()).GetProperty("email").GetString().ShouldBe(email);

        var nurse = factory.CreateBrowserClient();
        await nurse.SignInAsync(email, NewPassword);
        var me = await nurse.GetFromJsonAsync<JsonElement>("/api/access/me", Ct);
        me.GetProperty("displayName").GetString().ShouldBe("Ana Reyes");
        me.GetProperty("permissions").GetProperty("clinical.vitals.record")[0].GetString().ShouldBe("ward");

        // The nurse's own scope now reaches their employee record.
        var own = await nurse.GetFromJsonAsync<JsonElement>("/api/workforce/employees", Ct);
        own.GetProperty("items")[0].GetProperty("id").GetString().ShouldBe(employeeId);
        own.GetProperty("items")[0].GetProperty("hasAccount").GetBoolean().ShouldBeTrue();

        var accepts = await factory.AuditEntriesAsync(q => q.Where(e => e.Event == AuditEvents.Auth.InviteAccepted.Key));
        var accept = accepts.Single(e => e.Details!.Contains(employeeId, StringComparison.Ordinal));
        accept.UserId.ShouldBe(accept.EntityId, "the new account is both the actor and the target");
        (await InviteEventsAsync(employeeId, AuditEvents.Workforce.InviteSent)).Single().UserId.ShouldBe(hrUser.Id);
    }

    [Fact]
    public async Task A_used_link_cant_be_used_again()
    {
        var admin = await AdminAsync();
        var employeeId = await NurseAsync(admin);
        var email = NewEmail();
        (await SendAsync(admin, employeeId, email)).EnsureSuccessStatusCode();
        var token = TokenSentTo(email);
        var visitor = await AnonymousAsync();
        (await visitor.PostAsJsonAsync("/api/workforce/invites/accept", new { token, password = NewPassword }, Ct)).EnsureSuccessStatusCode();

        var again = await visitor.PostAsJsonAsync("/api/workforce/invites/accept", new { token, password = "Another-Password-2026" }, Ct);
        var lookup = await visitor.PostAsJsonAsync("/api/workforce/invites/lookup", new { token }, Ct);
        var resend = await SendAsync(admin, employeeId, NewEmail());

        again.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await again.ProblemCodeAsync()).ShouldBe("invite_used");
        (await lookup.ProblemCodeAsync()).ShouldBe("invite_used");
        (await resend.ProblemCodeAsync()).ShouldBe("employee_has_account");
    }

    [Fact]
    public async Task Expired_links_are_rejected_and_a_new_invite_replaces_them()
    {
        var admin = await AdminAsync();
        var employeeId = await NurseAsync(admin);
        var email = NewEmail();
        (await SendAsync(admin, employeeId, email)).EnsureSuccessStatusCode();
        var oldToken = TokenSentTo(email);
        await SetExpiresAtAsync(email, DateTimeOffset.UtcNow.AddMinutes(-1));

        var visitor = await AnonymousAsync();
        var accept = await visitor.PostAsJsonAsync("/api/workforce/invites/accept", new { token = oldToken, password = NewPassword }, Ct);
        (await accept.ProblemCodeAsync()).ShouldBe("invite_expired");

        var secondEmail = NewEmail();
        (await SendAsync(admin, employeeId, secondEmail)).StatusCode.ShouldBe(HttpStatusCode.Created);
        var invites = await admin.GetFromJsonAsync<JsonElement>($"/api/workforce/employees/{employeeId}/invites", Ct);
        invites.EnumerateArray().Select(i => i.GetProperty("state").GetString()).ShouldBe(["pending", "revoked"]);
        (await visitor.PostAsJsonAsync("/api/workforce/invites/accept", new { token = TokenSentTo(secondEmail), password = NewPassword }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Only_one_pending_invite_per_employee()
    {
        var admin = await AdminAsync();
        var employeeId = await NurseAsync(admin);
        (await SendAsync(admin, employeeId, NewEmail())).EnsureSuccessStatusCode();

        var second = await SendAsync(admin, employeeId, NewEmail());

        second.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await second.ProblemCodeAsync()).ShouldBe("invite_already_open");
    }

    [Fact]
    public async Task Revoked_invites_stop_working()
    {
        var admin = await AdminAsync();
        var employeeId = await NurseAsync(admin);
        var email = NewEmail();
        (await SendAsync(admin, employeeId, email)).EnsureSuccessStatusCode();
        var invite = (await admin.GetFromJsonAsync<JsonElement>($"/api/workforce/employees/{employeeId}/invites", Ct))[0];
        var id = invite.GetProperty("id").GetString();

        (await admin.PostAsync($"/api/workforce/invites/{id}/revoke", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var twice = await admin.PostAsync($"/api/workforce/invites/{id}/revoke", null, Ct);
        var visitor = await AnonymousAsync();
        var accept = await visitor.PostAsJsonAsync("/api/workforce/invites/accept", new { token = TokenSentTo(email), password = NewPassword }, Ct);

        (await twice.ProblemCodeAsync()).ShouldBe("invite_not_open");
        (await accept.ProblemCodeAsync()).ShouldBe("invite_revoked");
        (await InviteEventsAsync(employeeId, AuditEvents.Workforce.InviteRevoked)).Count.ShouldBe(1);
    }

    [Fact]
    public async Task Unknown_tokens_and_weak_passwords_are_rejected()
    {
        var visitor = await AnonymousAsync();

        var unknown = await visitor.PostAsJsonAsync("/api/workforce/invites/lookup", new { token = "not-a-real-token" }, Ct);
        var weak = await visitor.PostAsJsonAsync("/api/workforce/invites/accept", new { token = "not-a-real-token", password = "short" }, Ct);

        unknown.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await unknown.ProblemCodeAsync()).ShouldBe("invite_not_found");
        weak.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await weak.ReadJsonAsync()).GetProperty("errors").GetProperty("password")[0].GetString().ShouldBe("Use at least 12 characters.");
    }

    [Fact]
    public async Task Invites_cant_go_to_an_email_that_already_has_an_account()
    {
        var admin = await AdminAsync();
        var employeeId = await NurseAsync(admin);

        var response = await SendAsync(admin, employeeId, HilomApiFactory.AdminEmail.ToUpperInvariant());

        (await response.ProblemCodeAsync()).ShouldBe("email_taken");
    }

    [Fact]
    public async Task Only_role_managers_can_invite_a_system_administrator()
    {
        var (hr, _) = await factory.SignInWithRolesAsync("HR Officer");
        var admin = await AdminAsync();
        var employeeId = await NurseAsync(admin);

        var byHr = await SendAsync(hr, employeeId, NewEmail(), Role.SystemAdministratorName);
        var byAdmin = await SendAsync(admin, employeeId, NewEmail(), Role.SystemAdministratorName);

        byHr.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await byHr.ProblemCodeAsync()).ShouldBe("system_administrator_requires_roles_manage");
        byAdmin.StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Nothing_is_saved_when_the_email_cant_be_sent()
    {
        var admin = await AdminAsync();
        var employeeId = await NurseAsync(admin);
        var email = NewEmail();
        factory.Emails.FailFor(email);

        var response = await SendAsync(admin, employeeId, email);

        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        (await response.ProblemCodeAsync()).ShouldBe("email_not_sent");
        (await admin.GetFromJsonAsync<JsonElement>($"/api/workforce/employees/{employeeId}/invites", Ct)).GetArrayLength().ShouldBe(0);
        (await InviteEventsAsync(employeeId, AuditEvents.Workforce.InviteSent)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Separated_employees_cant_be_invited()
    {
        var admin = await AdminAsync();
        var employeeId = await NurseAsync(admin);
        var employee = await admin.GetFromJsonAsync<JsonElement>($"/api/workforce/employees/{employeeId}", Ct);
        (await admin.PostAsJsonAsync(
            $"/api/workforce/employees/{employeeId}/status",
            new { status = "separated", separatedOn = "2026-10-31", version = employee.GetProperty("version").GetUInt32() },
            Ct)).EnsureSuccessStatusCode();

        var response = await SendAsync(admin, employeeId, NewEmail());

        (await response.ProblemCodeAsync()).ShouldBe("employee_separated");
    }

    [Fact]
    public async Task Staff_without_the_permission_cant_send_or_see_invites()
    {
        var (nurse, _) = await factory.SignInWithRolesAsync("Staff Nurse");
        var employeeId = await NurseAsync(await AdminAsync());

        (await SendAsync(nurse, employeeId, NewEmail())).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await nurse.GetAsync($"/api/workforce/employees/{employeeId}/invites", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [GeneratedRegex(@"https://hilom\.test/invite#(?<token>[A-Za-z0-9_-]+)")]
    private static partial Regex InviteLink();
}
