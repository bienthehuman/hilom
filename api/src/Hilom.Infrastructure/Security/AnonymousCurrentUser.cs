using Hilom.SharedKernel.Security;

namespace Hilom.Infrastructure.Security;

/// <summary>Placeholder until login exists (slice 0.6). Every action is recorded as "no user".</summary>
internal sealed class AnonymousCurrentUser : ICurrentUser
{
    public Guid? UserId => null;

    public bool IsAuthenticated => false;
}
