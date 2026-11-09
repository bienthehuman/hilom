using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Hilom.Access.Sessions;

/// <summary>
/// Session cookie: HttpOnly (scripts can't read it), SameSite=Strict, HTTPS-only in production,
/// sliding idle timeout. API calls get 401/403 instead of login-page redirects. See docs/adr/0005-cookie-auth.md.
/// </summary>
internal sealed class SessionCookieOptions(IOptions<AccessOptions> access, IHostEnvironment environment)
    : IConfigureNamedOptions<CookieAuthenticationOptions>
{
    public void Configure(string? name, CookieAuthenticationOptions options)
    {
        if (name == Session.Scheme)
        {
            Configure(options);
        }
    }

    public void Configure(CookieAuthenticationOptions options)
    {
        options.Cookie.Name = Session.CookieName;
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Strict;
        options.Cookie.SecurePolicy = environment.IsProduction()
            ? CookieSecurePolicy.Always
            : CookieSecurePolicy.SameAsRequest;

        options.ExpireTimeSpan = access.Value.SessionIdleTimeout;
        options.SlidingExpiration = true;

        options.Events.OnRedirectToLogin = context =>
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        };
        options.Events.OnRedirectToAccessDenied = context =>
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        };
        options.Events.OnValidatePrincipal = SessionValidator.ValidateAsync;
    }
}
