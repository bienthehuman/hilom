using System.Security.Claims;
using Hilom.Access.Domain;

namespace Hilom.Access.Sessions;

/// <summary>Names shared by the session cookie and its claims.</summary>
public static class Session
{
    public const string Scheme = "HilomSession";
    public const string CookieName = "hilom.session";
    public const string SecurityStampClaim = "hilom:stamp";

    public static ClaimsPrincipal CreatePrincipal(User user)
    {
        ArgumentNullException.ThrowIfNull(user);

        var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new Claim(ClaimTypes.Name, user.DisplayName),
                new Claim(ClaimTypes.Email, user.Email),
                new Claim(SecurityStampClaim, user.SecurityStamp.ToString()),
            ],
            Scheme);

        return new ClaimsPrincipal(identity);
    }
}
