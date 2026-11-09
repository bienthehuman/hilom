using System.Security.Claims;
using Hilom.SharedKernel.Security;
using Microsoft.AspNetCore.Http;

namespace Hilom.Infrastructure.Security;

/// <summary>The signed-in user of the current request, read from the session cookie's claims.</summary>
internal sealed class HttpCurrentUser(IHttpContextAccessor httpContextAccessor) : ICurrentUser
{
    public Guid? UserId =>
        Guid.TryParse(httpContextAccessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier), out var id)
            ? id
            : null;

    public bool IsAuthenticated => httpContextAccessor.HttpContext?.User.Identity?.IsAuthenticated ?? false;
}
