using System.Net;
using Hilom.IntegrationTests.Infrastructure;

namespace Hilom.IntegrationTests;

/// <summary>Response headers that tell browsers what the API's origin may do (docs/ARCHITECTURE.md, security headers).</summary>
[Collection(ApiTestGroup.Name)]
public sealed class SecurityHeadersTests(HilomApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("/health/live", HttpStatusCode.OK)]
    [InlineData("/api/access/me", HttpStatusCode.Unauthorized)]
    [InlineData("/api/does-not-exist", HttpStatusCode.Unauthorized)]
    public async Task Every_response_carries_the_headers_even_errors(string path, HttpStatusCode status)
    {
        var response = await factory.CreateClient().GetAsync(path, Ct);

        response.StatusCode.ShouldBe(status);
        Header(response, "Content-Security-Policy")
            .ShouldBe("default-src 'none'; frame-ancestors 'none'; base-uri 'none'; form-action 'none'");
        Header(response, "X-Frame-Options").ShouldBe("DENY");
        Header(response, "X-Content-Type-Options").ShouldBe("nosniff");
        Header(response, "Referrer-Policy").ShouldBe("strict-origin-when-cross-origin");
        Header(response, "Permissions-Policy").ShouldNotBeNull().ShouldContain("camera=()");
        Header(response, "Permissions-Policy").ShouldNotBeNull().ShouldContain("geolocation=()");
    }

    [Fact]
    public async Task The_api_reference_page_is_left_without_a_content_security_policy_so_its_scripts_load()
    {
        var response = await factory.CreateClient().GetAsync("/scalar", Ct);

        Header(response, "Content-Security-Policy").ShouldBeNull();
        Header(response, "X-Frame-Options").ShouldBe("DENY");
    }

    [Fact]
    public async Task Hsts_is_off_unless_configured()
    {
        using var client = factory.CreateClient(new() { BaseAddress = new Uri("https://hilom.test") });

        var response = await client.GetAsync("/health/live", Ct);

        Header(response, "Strict-Transport-Security").ShouldBeNull();
    }

    [Fact]
    public async Task Hsts_is_sent_over_https_when_configured()
    {
        await using var withHsts = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Security:Hsts:Enabled", "true");
            builder.UseSetting("Security:Hsts:MaxAgeSeconds", "86400");
        });

        using var https = withHsts.CreateClient(new() { BaseAddress = new Uri("https://hilom.test") });
        using var http = withHsts.CreateClient(new() { BaseAddress = new Uri("http://hilom.test") });

        Header(await https.GetAsync("/health/live", Ct), "Strict-Transport-Security").ShouldBe("max-age=86400");
        Header(await http.GetAsync("/health/live", Ct), "Strict-Transport-Security").ShouldBeNull();
    }

    private static string? Header(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out var values) || response.Content.Headers.TryGetValues(name, out values)
            ? string.Join(", ", values)
            : null;
}
