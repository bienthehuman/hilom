using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Hilom.DemoData;

/// <summary>Whether the nightly reset is running right now.</summary>
public sealed class DemoState
{
    private volatile bool _resetting;

    public bool Resetting
    {
        get => _resetting;
        set => _resetting = value;
    }
}

/// <summary>While the database is being rebuilt, API calls get a clear "try again in a few minutes" instead of errors.</summary>
internal sealed class DemoResetGate(RequestDelegate next, DemoState state)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (state.Resetting && context.Request.Path.StartsWithSegments("/api", StringComparison.Ordinal)
            && !context.Request.Path.StartsWithSegments("/api/demo", StringComparison.Ordinal))
        {
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            context.Response.Headers.RetryAfter = "120";
            var problem = new ProblemDetails
            {
                Status = StatusCodes.Status503ServiceUnavailable,
                Title = "The demo is resetting",
                Detail = "The demo resets itself every night. It's back in a few minutes.",
            };
            problem.Extensions["code"] = "demo_resetting";
            await context.Response.WriteAsJsonAsync(problem, options: null, contentType: "application/problem+json", context.RequestAborted);
            return;
        }

        await next(context);
    }
}

public static class DemoApplicationBuilderExtensions
{
    public static IApplicationBuilder UseDemoMode(this IApplicationBuilder app) => app.UseMiddleware<DemoResetGate>();
}
