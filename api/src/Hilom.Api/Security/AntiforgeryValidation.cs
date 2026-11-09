using Microsoft.AspNetCore.Antiforgery;

namespace Hilom.Api.Security;

/// <summary>
/// Rejects state-changing /api requests (POST, PUT, PATCH, DELETE) without a valid antiforgery token.
/// With cookie sessions, this is what stops another site from submitting requests as the user.
/// Minimal APIs only check form posts on their own, so JSON endpoints need this middleware.
/// </summary>
internal sealed class AntiforgeryValidation(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, IAntiforgery antiforgery)
    {
        if (IsUnsafeApiRequest(context.Request) && !await antiforgery.IsRequestValidAsync(context))
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.RequestServices.GetRequiredService<IProblemDetailsService>().WriteAsync(new ProblemDetailsContext
            {
                HttpContext = context,
                ProblemDetails =
                {
                    Status = StatusCodes.Status400BadRequest,
                    Title = "Missing or invalid antiforgery token",
                    Detail = "Reload the page and try again.",
                    Extensions = { ["code"] = "invalid_antiforgery_token" },
                },
            });
            return;
        }

        await next(context);
    }

    private static bool IsUnsafeApiRequest(HttpRequest request) =>
        request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase)
        && !(HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method) || HttpMethods.IsOptions(request.Method));
}
