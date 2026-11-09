using Hilom.Access.Domain;

namespace Hilom.Access.Features.Sessions;

/// <summary>The signed-in user, as the web app needs it.</summary>
/// <param name="Permissions">Permission → scope (e.g. <c>"clinical.vitals.record": "ward"</c>). Filled from slice 0.7.</param>
public sealed record CurrentUserResponse(
    Guid Id,
    string Email,
    string DisplayName,
    IReadOnlyDictionary<string, string> Permissions)
{
    internal static CurrentUserResponse From(User user) =>
        new(user.Id, user.Email, user.DisplayName, new Dictionary<string, string>());
}
