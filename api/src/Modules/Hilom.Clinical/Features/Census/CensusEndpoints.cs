using Hilom.Clinical.Domain;
using Hilom.Clinical.Features.Admissions;
using Hilom.Clinical.Features.Vitals;
using Hilom.Clinical.Scopes;
using Hilom.Facility.Domain;
using Hilom.Infrastructure.Auditing;
using Hilom.Infrastructure.Authorization;
using Hilom.Infrastructure.Persistence;
using Hilom.Infrastructure.Web;
using Hilom.Patients.Domain;
using Hilom.SharedKernel.Auditing;
using Hilom.SharedKernel.Authorization;
using Hilom.Workforce.Scopes;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Hilom.Clinical.Features.Census;

/// <summary>
/// The ward board (<c>facility.census.view</c>): every bed of the wards the caller reaches, with who is in it
/// from <c>clinical.current_census</c> and two flags: severe allergies and critical vital signs. <c>all</c> =
/// every ward; <c>ward</c> = the wards the caller is assigned to today, evaluated live.
/// </summary>
internal static class CensusEndpoints
{
    public static async Task<Results<Ok<List<CensusWardResponse>>, ProblemHttpResult>> GetCensus(
        Guid? wardId,
        AppDbContext db,
        ICurrentPermissions permissions,
        IStaffScope staff,
        IAuditLog auditLog,
        IOptions<VitalRangeOptions> ranges,
        CancellationToken cancellationToken)
    {
        var scope = await permissions.ScopeForAsync(Perm.Facility.CensusView, cancellationToken);
        IReadOnlyList<Guid>? myWards = scope.IsAll ? null : await staff.MyWardIdsTodayAsync(cancellationToken);
        var census = db.Set<CensusEntry>().AsNoTracking();

        IQueryable<Ward> wards;
        if (wardId is { } id)
        {
            if (!await db.Set<Ward>().AnyAsync(w => w.Id == id, cancellationToken))
            {
                return FacilityErrors.WardNotFound.ToProblemResult();
            }

            if (myWards is not null && !myWards.Contains(id))
            {
                await auditLog.RecordOutOfScopeAsync(Perm.Facility.CensusView, new AuditTarget(nameof(Ward), id), cancellationToken);
                return FacilityErrors.WardNotFound.ToProblemResult();
            }

            wards = db.Set<Ward>().Where(w => w.Id == id);
        }
        else
        {
            // Active wards, and any inactive one that still has a patient in it.
            wards = db.Set<Ward>().Where(w => w.IsActive || census.Any(c => c.WardId == w.Id));
            if (myWards is not null)
            {
                wards = wards.Where(w => myWards.Contains(w.Id));
            }
        }

        var wardList = await wards.AsNoTracking().ToListAsync(cancellationToken);
        var wardIds = wardList.Select(w => w.Id).ToList();
        var occupants = await census.Where(c => wardIds.Contains(c.WardId)).ToListAsync(cancellationToken);
        var occupiedBeds = occupants.Select(c => c.BedId).ToList();
        var beds = await db.Set<Bed>().AsNoTracking()
            .Where(b => wardIds.Contains(b.WardId) && (b.IsActive || occupiedBeds.Contains(b.Id)))
            .ToListAsync(cancellationToken);

        var flags = await FlagsAsync(db, permissions, staff, occupants, ranges.Value, cancellationToken);
        var byBed = occupants.ToDictionary(c => c.BedId);

        return TypedResults.Ok(wardList
            .OrderBy(w => w.Code, AdmissionEndpoints.Natural)
            .Select(w =>
            {
                var wardBeds = beds
                    .Where(b => b.WardId == w.Id)
                    .OrderBy(b => b.Label, AdmissionEndpoints.Natural)
                    .Select(b => new CensusBedResponse(
                        b.Id,
                        b.Label,
                        byBed.TryGetValue(b.Id, out var c) ? ToPatient(c, flags) : null))
                    .ToList();
                return new CensusWardResponse(w.Id, w.Code, w.Name, w.WardType, wardBeds.Count(b => b.Patient is not null), wardBeds);
            })
            .ToList());
    }

    private sealed record Flags(
        ILookup<Guid, string> SevereAllergies,
        IReadOnlyDictionary<Guid, (string[] Critical, DateTimeOffset At)> LatestVitals,
        IReadOnlySet<Guid> CanOpen);

    private static async Task<Flags> FlagsAsync(
        AppDbContext db,
        ICurrentPermissions permissions,
        IStaffScope staff,
        List<CensusEntry> occupants,
        VitalRangeOptions ranges,
        CancellationToken cancellationToken)
    {
        var patientIds = occupants.Select(c => c.PatientId).ToList();
        var encounterIds = occupants.Select(c => c.EncounterId).ToList();

        // Same rule as the patient header's banner (PatientAllergy.IsActiveAndSevere).
        var severe = (await db.Set<PatientAllergy>().AsNoTracking()
                .Where(PatientAllergy.IsActiveAndSevere)
                .Where(a => patientIds.Contains(a.PatientId))
                .Select(a => new { a.PatientId, a.Allergen })
                .ToListAsync(cancellationToken))
            .OrderBy(a => a.Allergen, StringComparer.OrdinalIgnoreCase)
            .ToLookup(a => a.PatientId, a => a.Allergen);

        // The latest set on each stay, voided ones skipped, flagged against the shared ranges.
        var latest = await db.Set<VitalSigns>().AsNoTracking()
            .Where(v => encounterIds.Contains(v.EncounterId) && v.VoidedAt == null)
            .GroupBy(v => v.EncounterId)
            .Select(g => g.OrderByDescending(v => v.RecordedAt).ThenByDescending(v => v.Id).First())
            .ToListAsync(cancellationToken);
        var vitals = latest.ToDictionary(
            v => v.EncounterId,
            v => (VitalEndpoints.Critical(VitalEndpoints.Flags(v, ranges)), v.RecordedAt));

        // The tile links to the stay only when the caller can open it.
        var reach = await EncounterReach.ResolveAsync(db, permissions, staff, Perm.Clinical.EncountersView, cancellationToken);
        var canOpen = await reach.Filter(db.Set<Encounter>().Where(e => encounterIds.Contains(e.Id)))
            .Select(e => e.Id)
            .ToListAsync(cancellationToken);

        return new Flags(severe, vitals, canOpen.ToHashSet());
    }

    private static CensusPatientResponse ToPatient(CensusEntry c, Flags flags)
    {
        var vitals = flags.LatestVitals.TryGetValue(c.EncounterId, out var v) ? v : default;
        return new CensusPatientResponse(
            c.PatientId,
            c.Mrn,
            c.FirstName,
            c.LastName,
            c.EncounterId,
            flags.CanOpen.Contains(c.EncounterId),
            new CensusPhysicianRef(c.AttendingPhysicianId, c.PhysicianFirstName, c.PhysicianLastName),
            c.AdmittedHours,
            [.. flags.SevereAllergies[c.PatientId]],
            vitals.Critical ?? [],
            vitals.Critical is null ? null : vitals.At);
    }
}
