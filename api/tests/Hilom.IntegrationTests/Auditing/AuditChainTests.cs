using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Hilom.IntegrationTests.Infrastructure;
using Npgsql;

namespace Hilom.IntegrationTests.Auditing;

/// <summary>Tamper evidence (docs/AUDIT.md, rule 6): the sealing job's hash chain and the verify endpoint.</summary>
[Collection(ApiTestGroup.Name)]
public sealed class AuditChainTests(HilomApiFactory factory)
{
    private const string VerifyUrl = "/api/access/audit/verify";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<HttpClient> AdminAsync()
    {
        var admin = factory.CreateBrowserClient();
        await admin.SignInAsync(HilomApiFactory.AdminEmail, HilomApiFactory.AdminPassword);
        return admin;
    }

    /// <summary>Creates a user through the API, so the log gets a named event and its data changes.</summary>
    private static async Task<Guid> CreateUserAsync(HttpClient admin)
    {
        var response = await admin.PostAsJsonAsync(
            "/api/access/users",
            new { email = $"chain-{Guid.NewGuid():N}@hilom.test", displayName = "Chain Test", password = "Chain-Test-Password-1" },
            Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await response.ReadJsonAsync()).GetProperty("id").GetGuid();
    }

    private async Task<Guid> UserCreatedEntryAsync(Guid userId) =>
        (await factory.AuditEntriesAsync(q => q.Where(e => e.Event == "access.user.created" && e.EntityId == userId)))
            .ShouldHaveSingleItem().Id;

    private static async Task<JsonElement> VerifyAsync(HttpClient admin)
    {
        var response = await admin.GetAsync(VerifyUrl, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return await response.ReadJsonAsync();
    }

    /// <summary>
    /// What someone with direct database access could do: switch the append-only trigger off, change rows, switch it
    /// back on, all in one transaction.
    /// </summary>
    private async Task AroundTheTriggerAsync(string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = new NpgsqlConnection(factory.ConnectionString);
        await connection.OpenAsync(Ct);
        await using var transaction = await connection.BeginTransactionAsync(Ct);
        await using var command = new NpgsqlCommand(
            $"""
            ALTER TABLE access.audit_log DISABLE TRIGGER audit_log_append_only;
            {sql};
            ALTER TABLE access.audit_log ENABLE TRIGGER audit_log_append_only;
            """,
            connection,
            transaction);
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        await command.ExecuteNonQueryAsync(Ct);
        await transaction.CommitAsync(Ct);
    }

    [Fact]
    public async Task Sealing_links_every_entry_and_new_entries_wait_for_the_next_run()
    {
        var admin = await AdminAsync();
        await CreateUserAsync(admin);
        await factory.SealAuditLogAsync();

        var sealedReport = await VerifyAsync(admin);
        sealedReport.GetProperty("intact").GetBoolean().ShouldBeTrue();
        sealedReport.GetProperty("unsealedCount").GetInt64().ShouldBe(0);
        sealedReport.GetProperty("firstBreak").ValueKind.ShouldBe(JsonValueKind.Null);
        var sealedCount = sealedReport.GetProperty("verifiedCount").GetInt64();
        sealedCount.ShouldBeGreaterThan(0);

        var userId = await CreateUserAsync(admin);
        var waiting = await VerifyAsync(admin);
        waiting.GetProperty("unsealedCount").GetInt64().ShouldBeGreaterThan(0);
        waiting.GetProperty("verifiedCount").GetInt64().ShouldBe(sealedCount);

        (await factory.SealAuditLogAsync()).ShouldBeGreaterThan(0);
        var extended = await VerifyAsync(admin);
        extended.GetProperty("intact").GetBoolean().ShouldBeTrue();
        extended.GetProperty("verifiedCount").GetInt64().ShouldBeGreaterThan(sealedCount);

        var entry = (await factory.AuditEntriesAsync(q => q.Where(e => e.EntityId == userId && e.Event == "access.user.created"))).Single();
        entry.Hash.ShouldNotBeNull();
        entry.PrevHash.ShouldNotBeNull();
        entry.SealedAt.ShouldNotBeNull();
    }

    [Fact]
    public async Task A_sealed_entry_can_not_be_resealed_through_the_trigger()
    {
        await AdminAsync();
        await factory.SealAuditLogAsync();
        var entry = (await factory.AuditEntriesAsync(q => q.Where(e => e.SealedAt != null).Take(1))).Single();

        await using var connection = new NpgsqlConnection(factory.ConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = new NpgsqlCommand("UPDATE access.audit_log SET hash = @hash WHERE id = @id", connection);
        command.Parameters.AddWithValue("hash", new byte[32]);
        command.Parameters.AddWithValue("id", entry.Id);

        (await Should.ThrowAsync<PostgresException>(() => command.ExecuteNonQueryAsync(Ct))).MessageText.ShouldContain("append-only");
    }

    [Fact]
    public async Task Verify_names_an_entry_altered_directly_in_the_database()
    {
        var admin = await AdminAsync();
        var target = await UserCreatedEntryAsync(await CreateUserAsync(admin));
        await CreateUserAsync(admin);
        await factory.SealAuditLogAsync();
        (await VerifyAsync(admin)).GetProperty("intact").GetBoolean().ShouldBeTrue();

        var original = (await factory.AuditEntriesAsync(q => q.Where(e => e.Id == target))).Single().Details;
        await AroundTheTriggerAsync("UPDATE access.audit_log SET details = '{\"forged\": true}' WHERE id = @id", ("id", target));
        try
        {
            var report = await VerifyAsync(admin);

            report.GetProperty("intact").GetBoolean().ShouldBeFalse();
            var broken = report.GetProperty("firstBreak");
            broken.GetProperty("entryId").GetGuid().ShouldBe(target);
            broken.GetProperty("reason").GetString().ShouldBe("entry_altered");
            broken.GetProperty("event").GetString().ShouldBe("access.user.created");
            broken.GetProperty("position").GetInt64().ShouldBe(report.GetProperty("verifiedCount").GetInt64() + 1);
        }
        finally
        {
            // Put it back, so the shared log verifies again for the tests that follow.
            await AroundTheTriggerAsync(
                "UPDATE access.audit_log SET details = @original::jsonb WHERE id = @id",
                ("original", original ?? "null"),
                ("id", target));
        }

        (await VerifyAsync(admin)).GetProperty("intact").GetBoolean().ShouldBeTrue();
    }

    [Fact]
    public async Task Verify_notices_an_entry_deleted_directly_in_the_database()
    {
        var admin = await AdminAsync();
        var target = await UserCreatedEntryAsync(await CreateUserAsync(admin));
        await CreateUserAsync(admin); // so the deleted entry isn't the last link
        await factory.SealAuditLogAsync();

        await AroundTheTriggerAsync(
            """
            CREATE TABLE public.removed_audit_entry AS SELECT * FROM access.audit_log WHERE id = @id;
            DELETE FROM access.audit_log WHERE id = @id
            """,
            ("id", target));
        try
        {
            var report = await VerifyAsync(admin);

            // The gap shows at the entry after it, whose prev_hash no longer matches.
            report.GetProperty("intact").GetBoolean().ShouldBeFalse();
            var broken = report.GetProperty("firstBreak");
            broken.GetProperty("reason").GetString().ShouldBe("link_broken");
            broken.GetProperty("entryId").GetGuid().ShouldNotBe(target);
        }
        finally
        {
            await AroundTheTriggerAsync(
                """
                INSERT INTO access.audit_log SELECT * FROM public.removed_audit_entry;
                DROP TABLE public.removed_audit_entry
                """);
        }

        (await VerifyAsync(admin)).GetProperty("intact").GetBoolean().ShouldBeTrue();
    }

    [Fact]
    public async Task Only_holders_of_the_audit_permission_can_verify()
    {
        var (nurse, _) = await factory.SignInWithRolesAsync("Chief Nurse");

        (await nurse.GetAsync(VerifyUrl, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}
