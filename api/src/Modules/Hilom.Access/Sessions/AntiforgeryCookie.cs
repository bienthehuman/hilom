using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http;

namespace Hilom.Access.Sessions;

/// <summary>
/// Antiforgery for the cookie session. The server keeps a secret in an HttpOnly cookie and hands the
/// browser a request token in <c>XSRF-TOKEN</c>, which Angular's HttpClient echoes back in the
/// <c>X-XSRF-TOKEN</c> header on every POST/PUT/PATCH/DELETE.
/// </summary>
public static class AntiforgeryCookie
{
    public const string RequestTokenCookie = "XSRF-TOKEN";
    public const string HeaderName = "X-XSRF-TOKEN";
    public const string SecretCookie = "hilom.antiforgery";

    /// <summary>
    /// Issues tokens for the request's current user. Call again after signing in or out,
    /// because tokens are tied to who is signed in.
    /// </summary>
    public static void Refresh(HttpContext httpContext, IAntiforgery antiforgery)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(antiforgery);

        var tokens = antiforgery.GetAndStoreTokens(httpContext);
        httpContext.Response.Cookies.Append(
            RequestTokenCookie,
            tokens.RequestToken!,
            new CookieOptions
            {
                HttpOnly = false,
                SameSite = SameSiteMode.Strict,
                Secure = httpContext.Request.IsHttps,
                Path = "/",
            });
    }
}
