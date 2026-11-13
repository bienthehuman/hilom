using System.ComponentModel.DataAnnotations;

namespace Hilom.Api.Security;

/// <summary>
/// Response headers that tell browsers what this origin may do. The API answers JSON only, so its policy allows
/// nothing to load, run or frame it. The Scalar reference (outside production) is a page that loads its own scripts,
/// so it's left without a content security policy rather than half-broken by one.
/// The Angular app gets the same headers from its server; see <c>web/angular.json</c> and docs/ARCHITECTURE.md.
/// </summary>
internal sealed class SecurityHeaders(RequestDelegate next)
{
    public const string ApiContentSecurityPolicy =
        "default-src 'none'; frame-ancestors 'none'; base-uri 'none'; form-action 'none'";

    public const string PermissionsPolicy =
        "camera=(), microphone=(), geolocation=(), payment=(), usb=()";

    public Task InvokeAsync(HttpContext context)
    {
        var headers = context.Response.Headers;
        var isApiReference = context.Request.Path.StartsWithSegments("/scalar", StringComparison.OrdinalIgnoreCase);

        // Set up front, so error responses (problem details, 401s, 404s) carry them too.
        if (!isApiReference)
        {
            headers.ContentSecurityPolicy = ApiContentSecurityPolicy;
        }

        headers.XContentTypeOptions = "nosniff";
        headers.XFrameOptions = "DENY";
        headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
        headers["Permissions-Policy"] = PermissionsPolicy;
        headers["Cross-Origin-Opener-Policy"] = "same-origin";
        headers["Cross-Origin-Resource-Policy"] = "same-origin";
        return next(context);
    }
}

/// <summary>
/// Strict-Transport-Security. Off by default, so local HTTP development never pins a browser to HTTPS for
/// <c>localhost</c>; a deployment behind HTTPS turns it on with <c>Security__Hsts__Enabled=true</c>.
/// </summary>
internal sealed class HstsSettings
{
    public const string SectionName = "Security:Hsts";

    public bool Enabled { get; set; }

    [Range(0, 63_072_000)]
    public int MaxAgeSeconds { get; set; } = 31_536_000;

    public bool IncludeSubDomains { get; set; }
}

internal static class SecurityHeadersExtensions
{
    public static IServiceCollection AddSecurityHeaders(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<HstsSettings>()
            .Bind(configuration.GetSection(HstsSettings.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        var settings = configuration.GetSection(HstsSettings.SectionName).Get<HstsSettings>() ?? new HstsSettings();
        services.AddHsts(options =>
        {
            options.MaxAge = TimeSpan.FromSeconds(settings.MaxAgeSeconds);
            options.IncludeSubDomains = settings.IncludeSubDomains;
        });
        return services;
    }

    public static WebApplication UseSecurityHeaders(this WebApplication app)
    {
        app.UseMiddleware<SecurityHeaders>();
        if (app.Configuration.GetSection(HstsSettings.SectionName).Get<HstsSettings>()?.Enabled == true)
        {
            // Sent on HTTPS responses only (and never for localhost), as the middleware itself decides.
            app.UseHsts();
        }

        return app;
    }
}
