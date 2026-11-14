namespace Hilom.SharedKernel.Security;

/// <summary>Claims the session adds to the signed-in user for the length of one request.</summary>
public static class SessionClaims
{
    /// <summary>
    /// Present while the user still holds a temporary password. Read fresh from the database on every request,
    /// so changing the password lifts the block straight away.
    /// </summary>
    public const string PasswordChangeRequired = "hilom:password_change_required";

    /// <summary>Present while a self-registered account hasn't confirmed its email. Read fresh on every request.</summary>
    public const string EmailUnverified = "hilom:email_unverified";
}
