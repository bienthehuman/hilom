using Hilom.Access.Domain;
using Hilom.SharedKernel.Authorization;

namespace Hilom.Access.Features.Sessions;

/// <summary>The signed-in user, as the web app needs it.</summary>
/// <param name="AccountType">Staff use the staff app; portal accounts use the patient portal.</param>
/// <param name="MustChangePassword">
/// An administrator set the password. The user must choose their own before the API lets them do anything else.
/// </param>
/// <param name="MustVerifyEmail">
/// A self-registered portal account that hasn't entered the code emailed to it. It can't request patient links yet.
/// </param>
/// <param name="Permissions">
/// Permission key → scopes granted across all the user's roles, widest first
/// (e.g. <c>"clinical.vitals.record": ["ward", "own"]</c>). The UI uses it to show or hide things;
/// the API enforces it regardless.
/// </param>
public sealed record CurrentUserResponse(
    Guid Id,
    string Email,
    string DisplayName,
    AccountType AccountType,
    bool MustChangePassword,
    bool MustVerifyEmail,
    IReadOnlyDictionary<string, IReadOnlyList<PermissionScope>> Permissions)
{
    internal static CurrentUserResponse From(User user, PermissionSet permissions) =>
        new(
            user.Id,
            user.Email,
            user.DisplayName,
            user.AccountType,
            user.MustChangePassword,
            user.MustVerifyEmail,
            permissions.Keys.Order(StringComparer.Ordinal).ToDictionary(key => key, permissions.ScopesFor));
}
