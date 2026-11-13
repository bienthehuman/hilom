using Hilom.Infrastructure.Persistence;
using Hilom.IntegrationTests.Infrastructure;
using Npgsql;

namespace Hilom.IntegrationTests.Auditing;

/// <summary>
/// The app's own database login (docs/AUDIT.md, rule 5): on <c>access.audit_log</c> it may read and append, and fill
/// the sealing columns once, nothing else. These run as the same login the API uses in every other test, and check
/// that the database refuses for lack of privilege ("permission denied"), not only because of the append-only trigger.
/// </summary>
[Collection(ApiTestGroup.Name)]
public sealed class AppRoleTests(HilomApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_api_connects_as_the_restricted_role()
    {
        (await AsAppAsync<string>("SELECT current_user")).ShouldBe(DatabaseConnection.AppRole);
        (await AsAppAsync<bool>("SELECT rolsuper OR rolcreaterole OR rolcreatedb FROM pg_roles WHERE rolname = current_user"))
            .ShouldBeFalse();
    }

    [Fact]
    public async Task It_can_append_to_and_read_the_audit_log()
    {
        var id = await AppendAsync();

        (await AsAppAsync<long>($"SELECT count(*) FROM access.audit_log WHERE id = '{id}'")).ShouldBe(1);
    }

    [Theory]
    [InlineData("UPDATE access.audit_log SET event = 'auth.logout' WHERE id = '{id}'")]
    [InlineData("UPDATE access.audit_log SET outcome = 'success', details = NULL WHERE id = '{id}'")]
    [InlineData("DELETE FROM access.audit_log WHERE id = '{id}'")]
    [InlineData("TRUNCATE access.audit_log")]
    public async Task It_cannot_change_or_remove_audit_entries(string sql)
    {
        var id = await AppendAsync();

        var error = await Should.ThrowAsync<PostgresException>(() => AsAppAsync<int>(sql.Replace("{id}", id.ToString(), StringComparison.Ordinal)));

        error.SqlState.ShouldBe(PostgresErrorCodes.InsufficientPrivilege);
        error.MessageText.ShouldStartWith("permission denied for table audit_log");
        (await AsOwnerAsync<string>($"SELECT event FROM access.audit_log WHERE id = '{id}'")).ShouldBe("access.audit.viewed");
    }

    [Theory]
    [InlineData("ALTER TABLE access.audit_log DISABLE TRIGGER audit_log_append_only", "must be owner of table audit_log")]
    [InlineData("DROP TABLE access.audit_log", "must be owner of table audit_log")]
    [InlineData("DROP SCHEMA access CASCADE", "must be owner of schema access")]
    public async Task It_cannot_switch_off_the_trigger_or_drop_the_log(string sql, string refusal)
    {
        var error = await Should.ThrowAsync<PostgresException>(() => AsAppAsync<int>(sql));

        error.MessageText.ShouldBe(refusal);
    }

    [Fact]
    public async Task Sealing_is_the_one_update_it_may_make_and_only_once()
    {
        var id = await AppendAsync();
        await factory.SealAuditLogAsync();
        (await AsOwnerAsync<bool>($"SELECT sealed_at IS NOT NULL FROM access.audit_log WHERE id = '{id}'")).ShouldBeTrue();

        // It holds UPDATE on the sealing columns, so it's the append-only trigger that refuses a second seal.
        var error = await Should.ThrowAsync<PostgresException>(() =>
            AsAppAsync<int>($"UPDATE access.audit_log SET hash = '\\x00', sealed_at = now() WHERE id = '{id}'"));

        error.SqlState.ShouldBe(PostgresErrorCodes.InsufficientPrivilege);
        error.MessageText.ShouldContain("append-only");
    }

    /// <summary>Writes an entry the way the app does (an INSERT as the app's login), and returns its id.</summary>
    private async Task<Guid> AppendAsync()
    {
        var id = Guid.CreateVersion7();
        await AsAppAsync<int>(
            $"""
            INSERT INTO access.audit_log (id, occurred_at, kind, event, category, severity, outcome, correlation_id)
            VALUES ('{id}', now(), 'event', 'access.audit.viewed', 'access', 'info', 'denied', 'app-role-test')
            """);
        return id;
    }

    private Task<T> AsAppAsync<T>(string sql) => ScalarAsync<T>(factory.AppConnectionString, sql);

    private Task<T> AsOwnerAsync<T>(string sql) => ScalarAsync<T>(factory.ConnectionString, sql);

    private static async Task<T> ScalarAsync<T>(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(Ct);
        await using var command = new NpgsqlCommand(sql, connection);
        var result = await command.ExecuteScalarAsync(Ct);
        return result is null or DBNull ? default! : (T)result;
    }
}
