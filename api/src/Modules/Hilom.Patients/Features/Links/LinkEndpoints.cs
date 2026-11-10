using Hilom.Access.Domain;
using Hilom.Infrastructure.Auditing;
using Hilom.Infrastructure.Persistence;
using Hilom.Infrastructure.Web;
using Hilom.Patients.Domain;
using Hilom.SharedKernel.Auditing;
using Hilom.SharedKernel.Results;
using Hilom.SharedKernel.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Hilom.Patients.Features.Links;

/// <summary>
/// Patient account links (docs/adr/0003-patient-account-links.md): portal users request, front desk staff
/// verify, reject, adjust and revoke. Links are evaluated on every request, so verifying or revoking one
/// changes access immediately.
/// </summary>
internal static class LinkEndpoints
{
    // ---- Portal ----

    /// <summary>The signed-in portal user's links and requests, newest first.</summary>
    public static async Task<Ok<List<MyLinkResponse>>> ListMyLinks(
        AppDbContext db,
        ICurrentUser currentUser,
        CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId;
        var links = await (
            from l in db.Set<PatientAccountLink>().AsNoTracking()
            where l.UserId == userId
            join p in db.Set<Patient>() on l.PatientId equals p.Id into patients
            from p in patients.DefaultIfEmpty()
            orderby l.Status == LinkStatus.Active descending, l.RequestedAt descending
            select new { Link = l, Patient = p })
            .ToListAsync(cancellationToken);

        return TypedResults.Ok(links
            .Select(x => new MyLinkResponse(
                x.Link.Id,
                x.Link.Status,
                x.Link.Relationship,
                x.Link.RequestedPatientName,
                x.Link.RequestedBirthDate,
                x.Link.RequestedAt,
                x.Link.Status == LinkStatus.Active && x.Patient is not null ? PatientRef(x.Patient) : null,
                x.Link.CanBook,
                x.Link.CanViewRecords,
                x.Link.Status == LinkStatus.Rejected ? x.Link.ReviewNote : null))
            .ToList());
    }

    public static async Task<Results<Created<MyLinkResponse>, ProblemHttpResult>> RequestLink(
        RequestLinkRequest request,
        AppDbContext db,
        ICurrentUser currentUser,
        IAuditLog auditLog,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId!.Value;
        var pending = await db.Set<PatientAccountLink>()
            .CountAsync(l => l.UserId == userId && l.Status == LinkStatus.Pending, cancellationToken);
        if (pending >= PatientAccountLink.MaxPendingPerUser)
        {
            return PatientErrors.TooManyPendingLinks.ToProblemResult();
        }

        var link = new PatientAccountLink(
            userId,
            new LinkRequestDetails(
                request.PatientName,
                request.PatientBirthDate!.Value,
                request.PatientMrn,
                request.Relationship!.Value,
                request.Note),
            time.GetUtcNow());
        db.Add(link);
        auditLog.Record(
            AuditEvents.Patients.LinkRequested,
            new AuditOptions
            {
                Target = Target(link),
                Details = new
                {
                    patientName = link.RequestedPatientName,
                    birthDate = link.RequestedBirthDate,
                    mrn = link.RequestedMrn,
                    relationship = EnumText.ToText(link.Relationship),
                },
            });
        await db.SaveChangesAsync(cancellationToken);

        return TypedResults.Created(
            $"/api/patients/links/mine",
            new MyLinkResponse(
                link.Id,
                link.Status,
                link.Relationship,
                link.RequestedPatientName,
                link.RequestedBirthDate,
                link.RequestedAt,
                null,
                false,
                false,
                null));
    }

    // ---- Staff ----

    /// <summary>The review queue (pending, oldest first) and other links by status, newest first.</summary>
    public static async Task<Ok<PagedResponse<LinkResponse>>> ListLinks(
        AppDbContext db,
        int? page,
        int? pageSize,
        LinkStatusFilter? status,
        CancellationToken cancellationToken)
    {
        var (pageNumber, size) = Paging.Normalize(page, pageSize);
        var wanted = status ?? LinkStatusFilter.Pending;
        var linkStatus = Enum.Parse<LinkStatus>(wanted.ToString());

        var query = db.Set<PatientAccountLink>().AsNoTracking().Where(l => l.Status == linkStatus);
        query = linkStatus == LinkStatus.Pending
            ? query.OrderBy(l => l.RequestedAt)
            : query.OrderByDescending(l => l.UpdatedAt);

        var total = await query.CountAsync(cancellationToken);
        var links = await query.Skip((pageNumber - 1) * size).Take(size).ToListAsync(cancellationToken);
        return TypedResults.Ok(new PagedResponse<LinkResponse>(await ToResponsesAsync(db, links, cancellationToken), total, pageNumber, size));
    }

    /// <summary>Every link and request matched to this patient: active first, then history.</summary>
    public static async Task<Results<Ok<List<LinkResponse>>, ProblemHttpResult>> ListPatientLinks(
        Guid patientId,
        AppDbContext db,
        CancellationToken cancellationToken)
    {
        if (!await db.Set<Patient>().AnyAsync(p => p.Id == patientId, cancellationToken))
        {
            return PatientErrors.PatientNotFound.ToProblemResult();
        }

        var links = await db.Set<PatientAccountLink>().AsNoTracking()
            .Where(l => l.PatientId == patientId)
            .OrderByDescending(l => l.Status == LinkStatus.Active)
            .ThenByDescending(l => l.UpdatedAt)
            .ToListAsync(cancellationToken);
        return TypedResults.Ok(await ToResponsesAsync(db, links, cancellationToken));
    }

    public static async Task<Results<Ok<LinkResponse>, ProblemHttpResult, ValidationProblem>> VerifyLink(
        Guid id,
        VerifyLinkRequest request,
        AppDbContext db,
        ICurrentUser currentUser,
        IAuditLog auditLog,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        var link = await db.Set<PatientAccountLink>().SingleOrDefaultAsync(l => l.Id == id, cancellationToken);
        if (link is null)
        {
            return PatientErrors.LinkNotFound.ToProblemResult();
        }

        var patient = await db.Set<Patient>().AsNoTracking().SingleOrDefaultAsync(p => p.Id == request.PatientId, cancellationToken);
        if (patient is null)
        {
            return Error.Validation(
                "unknown_patient",
                "Choose the patient record that matches.",
                new Dictionary<string, string[]> { ["patientId"] = ["Choose the patient record that matches."] })
                .ToValidationResult();
        }

        var relationship = request.Relationship!.Value;
        if (await db.Set<PatientAccountLink>().AnyAsync(
                l => l.Id != link.Id && l.UserId == link.UserId && l.PatientId == patient.Id
                    && (l.Status == LinkStatus.Pending || l.Status == LinkStatus.Active),
                cancellationToken))
        {
            return PatientErrors.AlreadyLinked.ToProblemResult();
        }

        if (relationship == LinkRelationship.Self
            && await db.Set<PatientAccountLink>().AnyAsync(
                l => l.Id != link.Id && l.PatientId == patient.Id && l.Relationship == LinkRelationship.Self && l.Status == LinkStatus.Active,
                cancellationToken))
        {
            return PatientErrors.SelfLinkExists.ToProblemResult();
        }

        var verified = link.Verify(
            patient.Id,
            relationship,
            request.CanBook,
            request.CanViewRecords,
            request.Note,
            currentUser.UserId!.Value,
            time.GetUtcNow());
        if (!verified.IsSuccess)
        {
            return verified.Error.ToProblemResult();
        }

        auditLog.Record(
            AuditEvents.Patients.LinkVerified,
            new AuditOptions
            {
                Target = Target(link),
                PatientId = patient.Id,
                Details = new
                {
                    account = link.UserId,
                    mrn = patient.Mrn,
                    relationship = EnumText.ToText(link.Relationship),
                    canBook = link.CanBook,
                    canViewRecords = link.CanViewRecords,
                },
            });
        await SaveWithVersionAsync(db, link, request.Version, cancellationToken);
        return TypedResults.Ok((await ToResponsesAsync(db, [link], cancellationToken))[0]);
    }

    public static async Task<Results<Ok<LinkResponse>, ProblemHttpResult>> RejectLink(
        Guid id,
        RejectLinkRequest request,
        AppDbContext db,
        ICurrentUser currentUser,
        IAuditLog auditLog,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        var link = await db.Set<PatientAccountLink>().SingleOrDefaultAsync(l => l.Id == id, cancellationToken);
        if (link is null)
        {
            return PatientErrors.LinkNotFound.ToProblemResult();
        }

        var rejected = link.Reject(request.Reason, currentUser.UserId!.Value, time.GetUtcNow());
        if (!rejected.IsSuccess)
        {
            return rejected.Error.ToProblemResult();
        }

        auditLog.Record(
            AuditEvents.Patients.LinkRejected,
            new AuditOptions
            {
                Target = Target(link),
                Details = new { account = link.UserId, patientName = link.RequestedPatientName, reason = link.ReviewNote },
            });
        await SaveWithVersionAsync(db, link, request.Version, cancellationToken);
        return TypedResults.Ok((await ToResponsesAsync(db, [link], cancellationToken))[0]);
    }

    public static async Task<Results<Ok<LinkResponse>, ProblemHttpResult>> SetLinkPermissions(
        Guid id,
        SetLinkPermissionsRequest request,
        AppDbContext db,
        IAuditLog auditLog,
        CancellationToken cancellationToken)
    {
        var link = await db.Set<PatientAccountLink>().SingleOrDefaultAsync(l => l.Id == id, cancellationToken);
        if (link is null)
        {
            return PatientErrors.LinkNotFound.ToProblemResult();
        }

        var before = new { link.CanBook, link.CanViewRecords };
        var changed = link.SetPermissions(request.CanBook, request.CanViewRecords);
        if (!changed.IsSuccess)
        {
            return changed.Error.ToProblemResult();
        }

        if (changed.Value)
        {
            auditLog.Record(
                AuditEvents.Patients.LinkPermissionsChanged,
                new AuditOptions
                {
                    Target = Target(link),
                    PatientId = link.PatientId,
                    Details = new { account = link.UserId, before, after = new { link.CanBook, link.CanViewRecords } },
                });
        }
        else
        {
            auditLog.NothingToRecord();
        }

        await SaveWithVersionAsync(db, link, request.Version, cancellationToken);
        return TypedResults.Ok((await ToResponsesAsync(db, [link], cancellationToken))[0]);
    }

    public static async Task<Results<Ok<LinkResponse>, ProblemHttpResult>> RevokeLink(
        Guid id,
        RevokeLinkRequest request,
        AppDbContext db,
        ICurrentUser currentUser,
        IAuditLog auditLog,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        var link = await db.Set<PatientAccountLink>().SingleOrDefaultAsync(l => l.Id == id, cancellationToken);
        if (link is null)
        {
            return PatientErrors.LinkNotFound.ToProblemResult();
        }

        var revoked = link.Revoke(request.Reason, currentUser.UserId!.Value, time.GetUtcNow());
        if (!revoked.IsSuccess)
        {
            return revoked.Error.ToProblemResult();
        }

        auditLog.Record(
            AuditEvents.Patients.LinkRevoked,
            new AuditOptions
            {
                Target = Target(link),
                PatientId = link.PatientId,
                Details = new { account = link.UserId, relationship = EnumText.ToText(link.Relationship), reason = link.RevokeReason },
            });
        await SaveWithVersionAsync(db, link, request.Version, cancellationToken);
        return TypedResults.Ok((await ToResponsesAsync(db, [link], cancellationToken))[0]);
    }

    /// <summary>Always writes the row, so a stale version is caught even when nothing changed.</summary>
    private static Task<int> SaveWithVersionAsync(AppDbContext db, PatientAccountLink link, uint version, CancellationToken cancellationToken)
    {
        var entry = db.Entry(link);
        entry.Property(l => l.Version).OriginalValue = version;
        entry.Property(l => l.UpdatedAt).IsModified = true;
        return db.SaveChangesAsync(cancellationToken);
    }

    private static async Task<List<LinkResponse>> ToResponsesAsync(
        AppDbContext db,
        IReadOnlyList<PatientAccountLink> links,
        CancellationToken cancellationToken)
    {
        var userIds = links
            .SelectMany(l => new[] { (Guid?)l.UserId, l.ReviewedBy, l.RevokedBy })
            .OfType<Guid>()
            .Distinct()
            .ToList();
        var users = await db.Set<User>().AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, cancellationToken);
        var patientIds = links.Select(l => l.PatientId).OfType<Guid>().Distinct().ToList();
        var patients = await db.Set<Patient>().AsNoTracking()
            .Where(p => patientIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, cancellationToken);

        string? NameOf(Guid? id) => id is { } userId && users.TryGetValue(userId, out var user) ? user.DisplayName : null;

        return
        [
            .. links.Select(l =>
            {
                var requester = users[l.UserId];
                return new LinkResponse(
                    l.Id,
                    l.Status,
                    l.Relationship,
                    new LinkRequesterRef(requester.Id, requester.DisplayName, requester.Email),
                    l.RequestedPatientName,
                    l.RequestedBirthDate,
                    l.RequestedMrn,
                    l.RequestNote,
                    l.RequestedAt,
                    l.PatientId is { } patientId && patients.TryGetValue(patientId, out var patient) ? PatientRef(patient) : null,
                    l.CanBook,
                    l.CanViewRecords,
                    NameOf(l.ReviewedBy),
                    l.ReviewedAt,
                    l.ReviewNote,
                    NameOf(l.RevokedBy),
                    l.RevokedAt,
                    l.RevokeReason,
                    l.Version);
            }),
        ];
    }

    private static LinkedPatientRef PatientRef(Patient p) =>
        new(p.Id, p.Mrn, p.FirstName, p.MiddleName, p.LastName, p.BirthDate);

    private static AuditTarget Target(PatientAccountLink link) => new(nameof(PatientAccountLink), link.Id);
}
