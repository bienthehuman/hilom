namespace Hilom.SharedKernel.Security;

/// <summary>The user making the current request. Implemented by the Access module once login exists (slice 0.6).</summary>
public interface ICurrentUser
{
    Guid? UserId { get; }

    bool IsAuthenticated { get; }
}
