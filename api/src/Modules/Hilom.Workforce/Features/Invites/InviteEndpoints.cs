using Hilom.Access.Accounts;
using Hilom.Access.Domain;
using Hilom.Infrastructure.Auditing;
using Hilom.Infrastructure.Configuration;
using Hilom.Infrastructure.Email;
using Hilom.Infrastructure.Persistence;
using Hilom.Infrastructure.Web;
using Hilom.SharedKernel.Auditing;
using Hilom.SharedKernel.Results;
using Hilom.Workforce.Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Hilom.Workforce.Features.Invites;

/// <summary>
/// Staff onboarding: HR sends an invite to an employee, who sets a password and gets an account with the
/// invite's roles, linked to their employee record.
/// </summary>
internal static class InviteEndpoints
{
    public static async Task<Results<Ok<List<InviteResponse>>, ProblemHttpResult>> ListInvites(
        Guid employeeId,
        AppDbContext db,
        IAccountProvisioning accounts,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        if (!await db.Set<Employee>().AnyAsync(e => e.Id == employeeId, cancellationToken))
        {
            return WorkforceErrors.EmployeeNotFound.ToProblemResult();
        }

        var invites = await db.Set<StaffInvite>()
            .AsNoTracking()
            .Where(i => i.EmployeeId == employeeId)
            .OrderByDescending(i => i.CreatedAt)
            .ToListAsync(cancellationToken);

        var roles = (await accounts.RoleNamesAsync([.. invites.SelectMany(i => i.InitialRoleIds).Distinct()], cancellationToken))
            .ToDictionary(r => r.Id);
        var senderIds = invites.Select(i => i.CreatedBy).OfType<Guid>().Distinct().ToList();
        var senders = await db.Set<User>()
            .Where(u => senderIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.DisplayName, cancellationToken);

        var now = time.GetUtcNow();
        return TypedResults.Ok(invites.Select(i => ToResponse(i, now, roles, senders)).ToList());
    }

    public static async Task<Results<Created<InviteResponse>, ProblemHttpResult, ValidationProblem>> SendInvite(
        Guid employeeId,
        SendInviteRequest request,
        AppDbContext db,
        IAccountProvisioning accounts,
        IEmailSender email,
        IAuditLog auditLog,
        HospitalClock clock,
        TimeProvider time,
        IOptions<InviteOptions> inviteOptions,
        IOptions<EmailOptions> emailOptions,
        IOptions<HospitalOptions> hospitalOptions,
        CancellationToken cancellationToken)
    {
        var employee = await db.Set<Employee>().SingleOrDefaultAsync(e => e.Id == employeeId, cancellationToken);
        if (employee is null)
        {
            return WorkforceErrors.EmployeeNotFound.ToProblemResult();
        }

        if (employee.UserId is not null)
        {
            return WorkforceErrors.EmployeeHasAccount.ToProblemResult();
        }

        if (employee.Status == EmployeeStatus.Separated)
        {
            return WorkforceErrors.EmployeeSeparated.ToProblemResult();
        }

        var roleIds = (request.RoleIds ?? []).Distinct().ToList();
        if (await accounts.CheckGrantableAsync(roleIds, cancellationToken) is { } notGrantable)
        {
            return notGrantable.Type == ErrorType.Validation ? notGrantable.ToValidationResult() : notGrantable.ToProblemResult();
        }

        var address = request.Email.Trim();
        if (await accounts.EmailInUseAsync(address, cancellationToken))
        {
            return AccessErrors.EmailTaken.ToProblemResult();
        }

        var now = time.GetUtcNow();
        var open = await db.Set<StaffInvite>()
            .SingleOrDefaultAsync(i => i.EmployeeId == employeeId && i.AcceptedAt == null && i.RevokedAt == null, cancellationToken);
        if (open is not null && open.StateAt(now) == InviteState.Pending)
        {
            return WorkforceErrors.InviteAlreadyOpen.ToProblemResult();
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        // An expired invite still counts as open; close it so the new one can take its place.
        if (open is not null)
        {
            open.Revoke(now);
            await db.SaveChangesAsync(cancellationToken);
        }

        var (invite, token) = StaffInvite.Issue(employeeId, address, roleIds, now, inviteOptions.Value.ValidFor);
        db.Add(invite);
        var roleNames = await accounts.RoleNamesAsync(roleIds, cancellationToken);
        auditLog.Record(
            AuditEvents.Workforce.InviteSent,
            new AuditOptions
            {
                Target = new AuditTarget(nameof(Employee), employee.Id),
                Details = new
                {
                    inviteId = invite.Id,
                    employeeNo = employee.EmployeeNo,
                    email = invite.Email,
                    roles = roleNames.Select(r => r.Name),
                    expiresAt = invite.ExpiresAt,
                },
            });
        await db.SaveChangesAsync(cancellationToken);

        // Send before committing: if the email can't go out, nothing is saved and HR can simply retry.
        try
        {
            await email.SendAsync(
                InviteEmail.Compose(
                    invite.Email,
                    employee.FirstName,
                    employee.LastName,
                    hospitalOptions.Value.Name,
                    InviteEmail.AcceptUrl(emailOptions.Value.AppUrl, token),
                    invite.ExpiresAt,
                    clock.TimeZone),
                cancellationToken);
        }
        catch (EmailNotSentException)
        {
            return TypedResults.Problem(
                detail: WorkforceErrors.EmailUnavailable.Message,
                statusCode: StatusCodes.Status503ServiceUnavailable,
                extensions: new Dictionary<string, object?> { ["code"] = WorkforceErrors.EmailUnavailable.Code });
        }

        await transaction.CommitAsync(cancellationToken);

        var roles = roleNames.ToDictionary(r => r.Id);
        return TypedResults.Created(
            $"/api/workforce/employees/{employeeId}/invites",
            ToResponse(invite, now, roles, new Dictionary<Guid, string>()));
    }

    public static async Task<Results<NoContent, ProblemHttpResult>> RevokeInvite(
        Guid id,
        AppDbContext db,
        IAuditLog auditLog,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        var invite = await db.Set<StaffInvite>().SingleOrDefaultAsync(i => i.Id == id, cancellationToken);
        if (invite is null)
        {
            return WorkforceErrors.InviteNotFound.ToProblemResult();
        }

        var revoked = invite.Revoke(time.GetUtcNow());
        if (!revoked.IsSuccess)
        {
            return revoked.Error.ToProblemResult();
        }

        auditLog.Record(
            AuditEvents.Workforce.InviteRevoked,
            new AuditOptions
            {
                Target = new AuditTarget(nameof(Employee), invite.EmployeeId),
                Details = new { inviteId = invite.Id, email = invite.Email },
            });
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.NoContent();
    }

    public static async Task<Results<Ok<InviteLookupResponse>, ProblemHttpResult>> LookupInvite(
        InviteTokenRequest request,
        AppDbContext db,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        var hash = StaffInvite.HashToken(request.Token);
        var found = await (
            from i in db.Set<StaffInvite>().AsNoTracking()
            join e in db.Set<Employee>() on i.EmployeeId equals e.Id
            where i.TokenHash == hash
            select new { Invite = i, e.FirstName, e.LastName })
            .SingleOrDefaultAsync(cancellationToken);
        if (found is null)
        {
            return WorkforceErrors.InviteNotFound.ToProblemResult();
        }

        var usable = found.Invite.CheckUsable(time.GetUtcNow());
        return usable.IsSuccess
            ? TypedResults.Ok(new InviteLookupResponse(found.FirstName, found.LastName, found.Invite.Email, found.Invite.ExpiresAt))
            : usable.Error.ToProblemResult();
    }

    public static async Task<Results<Ok<AcceptInviteResponse>, ProblemHttpResult>> AcceptInvite(
        AcceptInviteRequest request,
        AppDbContext db,
        IAccountProvisioning accounts,
        IAuditLog auditLog,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        var hash = StaffInvite.HashToken(request.Token);
        var invite = await db.Set<StaffInvite>().SingleOrDefaultAsync(i => i.TokenHash == hash, cancellationToken);
        if (invite is null)
        {
            return WorkforceErrors.InviteNotFound.ToProblemResult();
        }

        var now = time.GetUtcNow();
        var accepted = invite.Accept(now);
        if (!accepted.IsSuccess)
        {
            return accepted.Error.ToProblemResult();
        }

        var employee = await db.Set<Employee>().SingleAsync(e => e.Id == invite.EmployeeId, cancellationToken);
        if (employee.Status == EmployeeStatus.Separated)
        {
            return WorkforceErrors.EmployeeSeparated.ToProblemResult();
        }

        var created = await accounts.CreateAsync(
            new NewAccount(
                invite.Email,
                $"{employee.FirstName} {employee.LastName}",
                request.Password,
                invite.InitialRoleIds,
                AssignedBy: invite.CreatedBy),
            now,
            cancellationToken);
        if (!created.IsSuccess)
        {
            return created.Error.ToProblemResult();
        }

        var linked = employee.LinkAccount(created.Value.UserId);
        if (!linked.IsSuccess)
        {
            return linked.Error.ToProblemResult();
        }

        auditLog.Record(
            AuditEvents.Auth.InviteAccepted,
            new AuditOptions
            {
                ActorId = created.Value.UserId,
                Target = new AuditTarget(nameof(User), created.Value.UserId),
                Details = new
                {
                    inviteId = invite.Id,
                    employeeId = employee.Id,
                    employeeNo = employee.EmployeeNo,
                    email = invite.Email,
                    roles = created.Value.GrantedRoles,
                },
            });

        // Two people opening the same link at once: the invite's version check lets only one through.
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.Ok(new AcceptInviteResponse(invite.Email));
    }

    private static InviteResponse ToResponse(
        StaffInvite invite,
        DateTimeOffset now,
        Dictionary<Guid, RoleName> roles,
        Dictionary<Guid, string> senders) =>
        new(
            invite.Id,
            invite.Email,
            invite.StateAt(now),
            invite.CreatedAt,
            invite.CreatedBy is { } sender && senders.TryGetValue(sender, out var name) ? name : null,
            invite.ExpiresAt,
            invite.AcceptedAt,
            invite.RevokedAt,
            [.. invite.InitialRoleIds.Where(roles.ContainsKey).Select(id => roles[id]).OrderBy(r => r.Name, StringComparer.Ordinal)]);
}
