using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Hilom.Infrastructure.Persistence;
using Hilom.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Hilom.IntegrationTests;

[Collection(ApiTestGroup.Name)]
public sealed class HostTests(HilomApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Liveness_check_is_healthy()
    {
        var response = await _client.GetAsync("/health/live", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync(Ct)).ShouldBe("Healthy");
    }

    [Fact]
    public async Task Readiness_check_reaches_the_database()
    {
        var response = await _client.GetAsync("/health/ready", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync(Ct)).ShouldBe("Healthy");
    }

    [Fact]
    public async Task Anonymous_api_requests_get_401_problem_details_without_revealing_routes()
    {
        var response = await _client.GetAsync("/api/does-not-exist", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        problem.GetProperty("status").GetInt32().ShouldBe(401);
        problem.TryGetProperty("traceId", out _).ShouldBeTrue();
    }

    [Fact]
    public async Task OpenApi_document_is_published()
    {
        var document = await _client.GetFromJsonAsync<JsonElement>("/openapi/v1.json", Ct);

        document.GetProperty("openapi").GetString().ShouldStartWith("3.");
    }
}

[Collection(ApiTestGroup.Name)]
public sealed class DatabaseTests(HilomApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task All_migrations_are_applied()
    {
        // The migration history is the owner's: the app's restricted login has no access to it.
        await using var scope = factory.Services.CreateOwnerScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        (await db.Database.GetPendingMigrationsAsync(Ct)).ShouldBeEmpty();
        (await db.Database.GetAppliedMigrationsAsync(Ct)).ShouldNotBeEmpty();
    }

    [Fact]
    public async Task Required_extensions_are_installed()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var extensions = await db.Database
            .SqlQueryRaw<string>("select extname as \"Value\" from pg_extension")
            .ToListAsync(Ct);

        extensions.ShouldContain("citext");
        extensions.ShouldContain("btree_gist");
    }

    [Fact]
    public async Task Database_reset_runs_cleanly()
    {
        await Should.NotThrowAsync(factory.ResetDatabaseAsync);
    }
}
