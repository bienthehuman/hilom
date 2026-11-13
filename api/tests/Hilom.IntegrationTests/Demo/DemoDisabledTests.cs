using System.Net;
using Hilom.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Hilom.IntegrationTests.Demo;

/// <summary>With demo mode off (the default), the sign-in page gets no accounts and nothing was generated.</summary>
[Collection(ApiTestGroup.Name)]
public sealed class DemoDisabledTests(HilomApiFactory factory)
{
    [Fact]
    public async Task Demo_accounts_are_not_offered_and_no_password_is_revealed()
    {
        var response = await factory.CreateBrowserClient().GetAsync("/api/demo/accounts", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.ReadJsonAsync();
        body.GetProperty("enabled").GetBoolean().ShouldBeFalse();
        body.GetProperty("accounts").GetArrayLength().ShouldBe(0);
        body.TryGetProperty("password", out var password).ShouldBeTrue();
        password.ValueKind.ShouldBe(System.Text.Json.JsonValueKind.Null);
    }

    [Fact]
    public void The_seeder_and_the_nightly_reset_are_not_registered()
    {
        factory.Services.GetServices<Hilom.Infrastructure.Seeding.IDataSeeder>()
            .Select(s => s.GetType().Name)
            .ShouldNotContain("DemoSeeder");
        factory.Services.GetServices<Microsoft.Extensions.Hosting.IHostedService>()
            .Select(s => s.GetType().Name)
            .ShouldNotContain("DemoResetService");
    }
}
