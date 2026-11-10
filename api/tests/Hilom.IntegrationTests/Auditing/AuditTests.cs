using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Hilom.Access.Domain;
using Hilom.Infrastructure.Auditing;
using Hilom.Infrastructure.Persistence;
using Hilom.IntegrationTests.Infrastructure;
using Hilom.SharedKernel.Auditing;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Hilom.IntegrationTests.Auditing;

[Collection(ApiTestGroup.Name)]
public sealed class AuditTests(HilomApiFactory factory)
{
    private const string Password = "Audit-Test-Password-1";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<HttpClient> AdminAsync()
    {
        var admin = factory.CreateBrowserClient();
        await admin.SignInAsync(HilomApiFactory.AdminEmail, HilomApiFactory.AdminPassword);
        return admin;
    }

    [Fact]
    public void Every_state_changing_endpoint_declares_its_audit_event_or_why_not()
    {
        var problems = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText?.StartsWith("/api/", StringComparison.Ordinal) == true)
            .Select(e => new
            {
                Methods = e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? [],
                Route = e.RoutePattern.RawText,
                Rules = (e.Metadata.GetMetadata<AuditedMetadata>() is null ? 0 : 1)
                    + (e.Metadata.GetMetadata<NotAuditedMetadata>() is null ? 0 : 1),
            })
            .Where(e => e.Methods.Any(m => m is "POST" or "PUT" or "PATCH" or "DELETE") && e.Rules != 1)
            .Select(e => $"{string.Join(",", e.Methods)} {e.Route}")
            .ToList();

        problems.ShouldBeEmpty("Add .Audited(AuditEvents.X) or .NotAudited(\"reason\") to each of these.");
    }

    [Fact]
    public async Task Sign_ins_failures_lockouts_and_sign_outs_are_recorded()
    {
        var user = await factory.CreateUserAsync(Password);
        var client = factory.CreateBrowserClient();
        await client.PrimeAntiforgeryAsync();

        for (var i = 0; i < 5; i++)
        {
            await client.LoginAsync(user.Email, "Wrong-Password-1");
        }

        await factory.UpdateUserAsync(user.Id, u => u.Unlock());
        await client.LoginAsync(user.Email, Password);
        await client.PostAsync("/api/access/logout", null, Ct);

        var entries = await factory.AuditEntriesAsync(q => q.Where(e => e.EntityId == user.Id || e.UserId == user.Id));
        var events = entries.Select(e => e.Event).ToList();

        events.Count(e => e == "auth.login.failed").ShouldBe(5);
        events.ShouldContain("auth.account.locked_out");
        events.ShouldContain("auth.login.succeeded");
        events.ShouldContain("auth.logout");

        var failure = entries.First(e => e.Event == "auth.login.failed");
        failure.Outcome.ShouldBe(AuditOutcome.Failed);
        failure.Severity.ShouldBe(AuditSeverity.Critical);
        failure.UserId.ShouldBeNull(); // whoever typed the wrong password may not be the owner
        failure.AttemptedIdentifier.ShouldBe(user.Email);
        failure.Details!.ShouldContain("wrong_password");
        failure.Details!.ShouldNotContain("Wrong-Password-1"); // passwords never reach the log

        var success = entries.Single(e => e.Event == "auth.login.succeeded");
        success.UserId.ShouldBe(user.Id);
        success.CorrelationId.ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public async Task An_unknown_email_is_recorded_without_an_account()
    {
        var email = $"ghost-{Guid.NewGuid():N}@hilom.test";
        var client = factory.CreateBrowserClient();
        await client.PrimeAntiforgeryAsync();

        await client.LoginAsync(email, "Whatever-Password-1");

        var entry = (await factory.AuditEntriesAsync(q => q.Where(e => e.AttemptedIdentifier == email))).ShouldHaveSingleItem();
        entry.Event.ShouldBe("auth.login.failed");
        entry.EntityId.ShouldBeNull();
        entry.Details!.ShouldContain("unknown_email");
    }

    [Fact]
    public async Task Changing_a_roles_permissions_records_what_changed_with_its_data_changes()
    {
        var admin = await AdminAsync();
        var role = await (await admin.PostAsJsonAsync(
            "/api/access/roles",
            new { name = $"Auditors {Guid.NewGuid():N}", description = "", grants = new[] { new { permission = "access.roles.view", scope = "all" } } },
            Ct)).ReadJsonAsync();
        var roleId = role.GetProperty("id").GetGuid();

        var update = await admin.PutAsJsonAsync(
            $"/api/access/roles/{roleId}",
            new
            {
                name = role.GetProperty("name").GetString(),
                description = "",
                grants = new[] { new { permission = "access.users.view", scope = "all" } },
                version = role.GetProperty("version").GetUInt32(),
            },
            Ct);
        update.StatusCode.ShouldBe(HttpStatusCode.OK);

        var changed = (await factory.AuditEntriesAsync(q => q.Where(e => e.Event == "access.role.permissions_changed" && e.EntityId == roleId)))
            .ShouldHaveSingleItem();
        using var details = JsonDocument.Parse(changed.Details!);
        details.RootElement.GetProperty("added")[0].GetString().ShouldBe("access.users.view:all");
        details.RootElement.GetProperty("removed")[0].GetString().ShouldBe("access.roles.view:all");

        // The named event and the field-level changes it caused share one correlation id.
        var related = await factory.AuditEntriesAsync(q => q.Where(e => e.CorrelationId == changed.CorrelationId));
        related.ShouldContain(e => e.Kind == AuditKind.DataChange && e.EntityType == nameof(RoleGrant));
        related.ShouldNotContain(e => e.Event == "access.role.renamed");
    }

    [Fact]
    public async Task Creating_a_user_records_the_change_but_never_the_password_hash()
    {
        var admin = await AdminAsync();
        var email = $"audited-{Guid.NewGuid():N}@hilom.test";

        var created = await admin.PostAsJsonAsync(
            "/api/access/users",
            new { email, displayName = "Audited User", password = Password },
            Ct);
        var userId = (await created.ReadJsonAsync()).GetProperty("id").GetGuid();

        var entries = await factory.AuditEntriesAsync(q => q.Where(e => e.EntityId == userId));
        entries.ShouldContain(e => e.Event == "access.user.created");
        var dataChange = entries.Single(e => e.Kind == AuditKind.DataChange && e.EntityType == nameof(User));

        using var changes = JsonDocument.Parse(dataChange.Changes!);
        changes.RootElement.GetProperty("email")[1].GetString().ShouldBe(email);
        changes.RootElement.GetProperty("passwordHash")[1].GetString().ShouldBe("[redacted]");
        changes.RootElement.GetProperty("securityStamp")[1].GetString().ShouldBe("[redacted]");
    }

    [Fact]
    public async Task Requests_refused_by_a_permission_check_are_recorded_as_denied()
    {
        var (nurse, user) = await factory.SignInWithRolesAsync("Staff Nurse");

        (await nurse.GetAsync("/api/access/roles", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var denied = (await factory.AuditEntriesAsync(q => q.Where(e => e.Event == "access.denied" && e.UserId == user.Id)))
            .ShouldHaveSingleItem();
        denied.Outcome.ShouldBe(AuditOutcome.Denied);
        denied.Details!.ShouldContain("access.roles.view");
        denied.Details!.ShouldContain("/api/access/roles");
    }

    [Fact]
    public async Task The_log_is_append_only_even_for_the_database_owner()
    {
        var entry = (await factory.AuditEntriesAsync(q => q.Take(1))).ShouldHaveSingleItem();
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var update = () => db.Database.ExecuteSqlAsync($"UPDATE access.audit_log SET event = 'tampered' WHERE id = {entry.Id}", Ct);
        var delete = () => db.Database.ExecuteSqlAsync($"DELETE FROM access.audit_log WHERE id = {entry.Id}", Ct);
        var truncate = () => db.Database.ExecuteSqlRawAsync("TRUNCATE access.audit_log", Ct);

        (await Should.ThrowAsync<PostgresException>(update)).MessageText.ShouldContain("append-only");
        (await Should.ThrowAsync<PostgresException>(delete)).MessageText.ShouldContain("append-only");
        (await Should.ThrowAsync<PostgresException>(truncate)).MessageText.ShouldContain("append-only");
    }

    [Fact]
    public async Task Administrators_can_read_and_filter_the_log_and_reading_it_is_recorded()
    {
        var admin = await AdminAsync();
        var adminId = (await admin.GetFromJsonAsync<JsonElement>("/api/access/me", Ct)).GetProperty("id").GetGuid();

        var page = await admin.GetFromJsonAsync<JsonElement>("/api/access/audit?event=auth.login.succeeded&pageSize=5", Ct);

        var items = page.GetProperty("items").EnumerateArray().ToList();
        items.ShouldNotBeEmpty();
        items.ShouldAllBe(i => i.GetProperty("event").GetString() == "auth.login.succeeded");
        items[0].GetProperty("label").GetString().ShouldBe("Signed in");
        (await factory.AuditEntriesAsync(q => q.Where(e => e.Event == "access.audit.viewed" && e.UserId == adminId))).ShouldNotBeEmpty();

        var (nurse, _) = await factory.SignInWithRolesAsync("Staff Nurse");
        (await nurse.GetAsync("/api/access/audit", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Enum_filters_use_the_same_snake_case_values_as_the_responses()
    {
        var admin = await AdminAsync();

        var changes = await admin.GetFromJsonAsync<JsonElement>("/api/access/audit?kind=data_change&pageSize=5", Ct);
        var critical = await admin.GetFromJsonAsync<JsonElement>("/api/access/audit?severity=critical&outcome=success&pageSize=5", Ct);
        var disabledUsers = await admin.GetAsync("/api/access/users?status=disabled", Ct);
        var unknown = await admin.GetAsync("/api/access/audit?kind=banana", Ct);

        changes.GetProperty("items").EnumerateArray().ShouldAllBe(i => i.GetProperty("kind").GetString() == "data_change");
        critical.GetProperty("items").EnumerateArray().ShouldAllBe(i => i.GetProperty("severity").GetString() == "critical");
        disabledUsers.StatusCode.ShouldBe(HttpStatusCode.OK);
        unknown.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await unknown.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("code").GetString().ShouldBe("bad_request");
    }
}
