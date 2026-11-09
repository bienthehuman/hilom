using System.Net.Http.Json;
using System.Text.Json;

namespace Hilom.IntegrationTests.Infrastructure;

internal static class ApiClientExtensions
{
    /// <summary>Fetches the antiforgery token, as the web app does on startup.</summary>
    public static async Task PrimeAntiforgeryAsync(this HttpClient client)
    {
        var response = await client.GetAsync("/api/access/antiforgery", TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public static async Task<HttpResponseMessage> LoginAsync(this HttpClient client, string email, string password) =>
        await client.PostAsJsonAsync(
            "/api/access/login",
            new { email, password },
            TestContext.Current.CancellationToken);

    /// <summary>Primes antiforgery and signs in, failing the test if sign-in doesn't succeed.</summary>
    public static async Task SignInAsync(this HttpClient client, string email, string password)
    {
        await client.PrimeAntiforgeryAsync();
        var response = await client.LoginAsync(email, password);
        response.EnsureSuccessStatusCode();
    }

    public static async Task<JsonElement> ReadJsonAsync(this HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);

    public static async Task<string?> ProblemCodeAsync(this HttpResponseMessage response)
    {
        var json = await response.ReadJsonAsync();
        return json.TryGetProperty("code", out var code) ? code.GetString() : null;
    }
}
