using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using Hilom.SharedKernel.Auditing;
using Hilom.SharedKernel.Domain;
using Hilom.SharedKernel.Results;

namespace Hilom.Workforce.Domain;

public enum InviteState
{
    Pending,
    Expired,
    Accepted,
    Revoked,
}

/// <summary>
/// An emailed link that lets an employee set a password and get a sign-in account with the chosen roles
/// (docs/DATA_MODEL.md, <c>staff_invites</c>). Only a hash of the token is stored; the token itself exists
/// in the email alone.
/// </summary>
public sealed class StaffInvite : Entity
{
    private const int TokenBytes = 32;

    private StaffInvite()
    {
    }

    private StaffInvite(Guid employeeId, string email, IEnumerable<Guid> roleIds, byte[] tokenHash, DateTimeOffset expiresAt)
    {
        EmployeeId = employeeId;
        Email = email.Trim();
        InitialRoleIds = [.. roleIds.Distinct()];
        TokenHash = tokenHash;
        ExpiresAt = expiresAt;
    }

    public Guid EmployeeId { get; private set; }

    /// <summary>Where the invite went. Becomes the sign-in email.</summary>
    public string Email { get; private set; } = "";

    [SensitiveData]
    public byte[] TokenHash { get; private set; } = [];

    /// <summary>Roles granted on acceptance. Roles deleted in the meantime are skipped.</summary>
    public List<Guid> InitialRoleIds { get; private set; } = [];

    public DateTimeOffset ExpiresAt { get; private set; }

    public DateTimeOffset? AcceptedAt { get; private set; }

    public DateTimeOffset? RevokedAt { get; private set; }

    /// <summary>Creates an invite and the token to send. The token can't be recovered later.</summary>
    public static (StaffInvite Invite, string Token) Issue(
        Guid employeeId,
        string email,
        IEnumerable<Guid> roleIds,
        DateTimeOffset now,
        TimeSpan validFor)
    {
        var token = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(TokenBytes));
        return (new StaffInvite(employeeId, email, roleIds, HashToken(token), now + validFor), token);
    }

    public static byte[] HashToken(string token) => SHA256.HashData(Encoding.UTF8.GetBytes(token));

    public InviteState StateAt(DateTimeOffset now) =>
        AcceptedAt is not null ? InviteState.Accepted
        : RevokedAt is not null ? InviteState.Revoked
        : now >= ExpiresAt ? InviteState.Expired
        : InviteState.Pending;

    /// <summary>Not accepted or revoked yet, whether or not it has expired. At most one per employee.</summary>
    public bool IsOpen => AcceptedAt is null && RevokedAt is null;

    public Result Revoke(DateTimeOffset now)
    {
        if (!IsOpen)
        {
            return Result.Failure(WorkforceErrors.InviteNotOpen);
        }

        RevokedAt = now;
        return Result.Success();
    }

    /// <summary>Fails with the reason a link no longer works: used, revoked or expired.</summary>
    public Result CheckUsable(DateTimeOffset now) =>
        StateAt(now) switch
        {
            InviteState.Accepted => Result.Failure(WorkforceErrors.InviteUsed),
            InviteState.Revoked => Result.Failure(WorkforceErrors.InviteRevoked),
            InviteState.Expired => Result.Failure(WorkforceErrors.InviteExpired),
            _ => Result.Success(),
        };

    public Result Accept(DateTimeOffset now)
    {
        var usable = CheckUsable(now);
        if (!usable.IsSuccess)
        {
            return usable;
        }

        AcceptedAt = now;
        return Result.Success();
    }
}
