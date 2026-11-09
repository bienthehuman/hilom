using System.Globalization;
using System.Threading.RateLimiting;
using Hilom.Access.Domain;
using Hilom.Access.Features.Sessions;
using Hilom.Access.Seeding;
using Hilom.Access.Sessions;
using Hilom.Infrastructure.Modules;
using Hilom.Infrastructure.Seeding;
using Hilom.Infrastructure.Web;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Hilom.Access;

/// <summary>Users, sign-in sessions, roles, permissions and the audit log.</summary>
public sealed class AccessModule : IModule
{
    public const string LoginRateLimitPolicy = "login";

    public string Name => "Access";

    public string Schema => "access";

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<AccessOptions>()
            .Bind(configuration.GetSection(AccessOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddSingleton<IPasswordHasher<User>, PasswordHasher<User>>();
        services.AddScoped<IDataSeeder, SystemAdministratorSeeder>();

        services.AddAuthentication(Session.Scheme).AddCookie(Session.Scheme);
        services.AddSingleton<IConfigureOptions<CookieAuthenticationOptions>, SessionCookieOptions>();

        // Every endpoint requires a signed-in user unless it opts out with AllowAnonymous().
        // Permission checks are added on top in slice 0.7.
        services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new AuthorizationPolicyBuilder(Session.Scheme).RequireAuthenticatedUser().Build());

        services.AddAntiforgery(options =>
        {
            options.HeaderName = AntiforgeryCookie.HeaderName;
            options.Cookie.Name = AntiforgeryCookie.SecretCookie;
            options.Cookie.SameSite = SameSiteMode.Strict;
        });

        services.AddRateLimiter(limiter =>
        {
            limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            limiter.OnRejected = async (context, _) =>
            {
                var http = context.HttpContext;
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    http.Response.Headers.RetryAfter = ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
                }

                await http.RequestServices.GetRequiredService<IProblemDetailsService>().WriteAsync(new ProblemDetailsContext
                {
                    HttpContext = http,
                    ProblemDetails =
                    {
                        Status = StatusCodes.Status429TooManyRequests,
                        Title = "Too many sign-in attempts",
                        Detail = "Too many sign-in attempts from this network. Wait a minute and try again.",
                        Extensions = { ["code"] = "too_many_attempts" },
                    },
                });
            };
            limiter.AddPolicy(LoginRateLimitPolicy, httpContext =>
            {
                var perMinute = httpContext.RequestServices.GetRequiredService<IOptions<AccessOptions>>()
                    .Value.LoginAttemptsPerMinute;
                return RateLimitPartition.GetFixedWindowLimiter(
                    httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => new FixedWindowRateLimiterOptions { PermitLimit = perMinute, Window = TimeSpan.FromMinutes(1) });
            });
        });
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/antiforgery", SessionEndpoints.GetAntiforgeryToken)
            .AllowAnonymous()
            .WithName("GetAntiforgeryToken")
            .WithSummary("Issue the XSRF-TOKEN cookie");

        endpoints.MapPost("/login", Login.Handle)
            .AllowAnonymous()
            .RequireRateLimiting(LoginRateLimitPolicy)
            .WithValidation<LoginRequest>()
            .WithName("Login")
            .WithSummary("Sign in with email and password");

        endpoints.MapPost("/logout", SessionEndpoints.Logout)
            .AllowAnonymous()
            .WithName("Logout")
            .WithSummary("End the current session");

        endpoints.MapGet("/me", SessionEndpoints.GetCurrentUser)
            .WithName("GetCurrentUser")
            .WithSummary("The signed-in user and their permissions");
    }
}
