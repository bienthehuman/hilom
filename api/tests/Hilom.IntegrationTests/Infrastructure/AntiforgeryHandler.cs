using Microsoft.Net.Http.Headers;

namespace Hilom.IntegrationTests.Infrastructure;

/// <summary>
/// Behaves like Angular's HttpClient: remembers the latest XSRF-TOKEN cookie the API sets
/// and sends it back as the X-XSRF-TOKEN header on state-changing requests.
/// </summary>
internal sealed class AntiforgeryHandler : DelegatingHandler
{
    private const string CookieName = "XSRF-TOKEN";
    private const string HeaderName = "X-XSRF-TOKEN";
    private string? _token;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (_token is not null && request.Method != HttpMethod.Get && request.Method != HttpMethod.Head)
        {
            request.Headers.Remove(HeaderName);
            request.Headers.Add(HeaderName, _token);
        }

        var response = await base.SendAsync(request, cancellationToken);

        if (response.Headers.TryGetValues(HeaderNames.SetCookie, out var setCookies))
        {
            foreach (var cookie in SetCookieHeaderValue.ParseList(setCookies.ToList()))
            {
                if (cookie.Name == CookieName)
                {
                    _token = cookie.Value.ToString();
                }
            }
        }

        return response;
    }
}
