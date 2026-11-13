using Hilom.Clinical.Domain;
using Hilom.Infrastructure.Auditing;
using Hilom.SharedKernel.Auditing;
using Hilom.Workforce.Domain;
using Microsoft.EntityFrameworkCore;

namespace Hilom.DemoData;

internal sealed class StayOrder(PhysicianOrder order, int[] hours, bool asNeeded)
{
    public PhysicianOrder Order { get; } = order;

    public int[] Hours { get; } = hours;

    public bool AsNeeded { get; } = asNeeded;
}

/// <summary>A stay in the ward, kept in memory while it's open because discharge, transfer and orders change it later.</summary>
internal sealed class Stay(PatientInfo patient, PhysicianInfo physician, Encounter encounter, Admission admission, BedAssignment bed, WardInfo ward)
{
    public PatientInfo Patient { get; } = patient;

    public PhysicianInfo Physician { get; } = physician;

    public Encounter Encounter { get; } = encounter;

    public Admission Admission { get; } = admission;

    public BedAssignment Bed { get; set; } = bed;

    public WardInfo Ward { get; set; } = ward;

    /// <summary>Where the patient was and when: one entry per ward, so each ward's nurses record only their own time.</summary>
    public List<(WardInfo Ward, DateTimeOffset From, DateTimeOffset? To)> Segments { get; } = [(ward, admission.AdmittedAt, null)];

    public List<StayOrder> Orders { get; } = [];

    public DateTimeOffset PlannedDischarge { get; set; }

    public DateTimeOffset? TransferAt { get; set; }

    public bool Acute { get; set; } = true;

    public DateTimeOffset AdmittedAt => Admission.AdmittedAt;

    public bool IsIn(WardInfo ward, DateTimeOffset at) =>
        Segments.Any(x => x.Ward.Id == ward.Id && x.From <= at && (x.To is null || at < x.To));
}

/// <summary>Admissions, transfers, discharges and the rounds of every shift: vitals, medications and notes.</summary>
internal sealed class WardGenerator(SeedSession s, DemoWorld w, DemoSize size, ClinicalRecords records)
{
    private static readonly (ShiftColor Shift, int StartHour, int[] VitalHours)[] Shifts =
    [
        (ShiftColor.Am, 6, [6, 10]),
        (ShiftColor.Pm, 14, [14, 18]),
        (ShiftColor.Noc, 22, [22, 26]),
    ];

    private readonly List<Stay> _open = [];
    private readonly List<Stay> _closedToday = [];
    private readonly HashSet<Guid> _admitted = [];
    private readonly HashSet<Guid> _occupiedBeds = [];
    private readonly Dictionary<Guid, DateTimeOffset> _bedFreeAt = [];
    private Guid? _reservedBed;

    /// <summary>Holds a bed back from everyone but the scripted admission, so that story always has somewhere to happen.</summary>
    public void ReserveBed(WardInfo ward) => _reservedBed = ward.Beds[^1].Id;

    public void ReleaseReservedBed() => _reservedBed = null;

    public bool IsAdmitted(PatientInfo patient) => _admitted.Contains(patient.Id);

    /// <summary>A ward for this physician's patients: their own department's wards first, then any with a free bed.</summary>
    public (WardInfo Ward, BedInfo Bed)? FindBed(PhysicianInfo? physician, DateTimeOffset at, WardInfo? preferred = null, Guid? except = null, bool useReserved = false)
    {
        var wards = w.Wards.Where(x => x.Id != except).ToList();
        var ordered = preferred is not null
            ? [preferred, .. wards.Where(x => x.Id != preferred.Id)]
            : wards.OrderByDescending(x => physician is not null && x.Department == physician.Department).ThenBy(_ => s.Random.NextDouble()).ToList();
        foreach (var ward in ordered)
        {
            var free = ward.Beds.Where(b => IsFree(b.Id, at, useReserved)).ToList();
            if (free.Count > 0)
            {
                return (ward, s.Random.Pick(free));
            }
        }

        return null;
    }

    private bool IsFree(Guid bedId, DateTimeOffset at, bool useReserved = false) =>
        (useReserved || bedId != _reservedBed) && !_occupiedBeds.Contains(bedId) && (!_bedFreeAt.TryGetValue(bedId, out var freeAt) || freeAt <= at);

    /// <summary>Admits a patient into a free bed, writes the physician's admission note and first orders, and saves, as the physician.</summary>
    public async Task<Stay?> AdmitAsync(
        PatientInfo patient,
        PhysicianInfo physician,
        (WardInfo Ward, BedInfo Bed) place,
        Guid? parentEncounterId,
        DateTimeOffset at,
        string? diagnosis,
        CancellationToken cancellationToken,
        bool useReserved = false)
    {
        if (IsAdmitted(patient) || !IsFree(place.Bed.Id, at, useReserved) || at > s.Now)
        {
            return null;
        }

        diagnosis ??= s.Random.Pick(DemoCatalog.AdmissionDiagnoses);
        s.Act(physician.Person.UserId, at);
        var (encounter, admission, bed) = Admission.Admit(
            new AdmitPatient(patient.Id, parentEncounterId, physician.Person.EmployeeId, place.Bed.Id, diagnosis, diagnosis),
            place.Ward.Id,
            physician.Person.UserId,
            at);
        s.Db.Add(encounter);
        s.Db.Add(admission);
        s.Db.Add(bed);
        s.Keep(encounter);
        s.Keep(admission);
        s.Keep(bed);
        s.Audit.Event(AuditEvents.Clinical.PatientAdmitted, new AuditOptions
        {
            Target = new AuditTarget(nameof(Admission), admission.Id),
            PatientId = patient.Id,
            Details = new { ward = place.Ward.Code, bed = place.Bed.Label, attending = physician.Person.EmployeeId },
        });

        var stay = new Stay(patient, physician, encounter, admission, bed, place.Ward)
        {
            PlannedDischarge = PlannedDischarge(at),
        };
        MaybePlanTransfer(stay);
        _admitted.Add(patient.Id);
        _occupiedBeds.Add(place.Bed.Id);

        var orderedAt = at.AddMinutes(s.Random.Next(8, 35));
        records.Note(encounter, patient, NoteType.Assessment, s.Random.Pick(DemoCatalog.AssessmentNotes) + " Admitted for: " + diagnosis + ".", physician.Person, orderedAt);
        var ordered = new List<string>();
        for (var i = s.Random.Pick((1, 0.35), (2, 0.4), (3, 0.25)); i > 0; i--)
        {
            if (records.MedicationOrder(encounter, patient, physician.Person, orderedAt.AddMinutes(1), ordered) is { } m)
            {
                stay.Orders.Add(new StayOrder(m.Order, m.Hours, m.AsNeeded));
            }
        }

        if (s.Random.Chance(0.7))
        {
            records.OtherOrder(encounter, patient, physician.Person, orderedAt.AddMinutes(2));
        }

        records.Vitals(encounter, patient, NurseOn(place.Ward, at), at.AddMinutes(s.Random.Next(5, 20)), acute: true, full: true);
        s.Act(physician.Person.UserId, orderedAt.AddMinutes(3));
        await s.SaveAsync(cancellationToken);
        _open.Add(stay);
        return stay;
    }

    public async Task DayAsync(DateOnly day, CancellationToken cancellationToken)
    {
        var dayStart = s.Local(day, new TimeOnly(0, 0));
        var dayEnd = s.Local(day.AddDays(1), new TimeOnly(0, 0));
        _closedToday.Clear();

        // Discharges, transfers and walk-in admissions, in the order they happen, so beds free up before they fill.
        var events = new List<(DateTimeOffset At, int Kind, object? Subject)>();
        foreach (var stay in _open)
        {
            if (stay.PlannedDischarge >= dayStart && stay.PlannedDischarge < dayEnd)
            {
                events.Add((stay.PlannedDischarge, 2, stay));
            }
            else if (stay.TransferAt is { } transferAt && transferAt >= dayStart && transferAt < dayEnd)
            {
                events.Add((transferAt, 1, stay));
            }
        }

        foreach (var ward in w.Wards)
        {
            var expected = size.AdmissionsPerWardDay;
            for (var i = (int)expected + (s.Random.Chance(expected - Math.Floor(expected)) ? 1 : 0); i > 0; i--)
            {
                events.Add((s.Local(day, new TimeOnly(s.Random.Next(7, 21), s.Random.Next(0, 60))), 0, ward));
            }
        }

        foreach (var (at, kind, subject) in events.OrderBy(e => e.At))
        {
            if (at > s.Now)
            {
                continue;
            }

            switch (kind)
            {
                case 0:
                    await AdmitWalkInAsync((WardInfo)subject!, at, cancellationToken);
                    break;
                case 1:
                    await TransferAsync((Stay)subject!, at, cancellationToken);
                    break;
                default:
                    await DischargeAsync((Stay)subject!, at, cancellationToken);
                    break;
            }
        }

        await PhysicianRoundsAsync(day, cancellationToken);
        foreach (var (shift, startHour, vitalHours) in Shifts)
        {
            var from = s.Local(day, new TimeOnly(startHour, 0));
            foreach (var ward in w.Wards)
            {
                await NurseRoundsAsync(ward, day, shift, startHour, from, from.AddHours(8), vitalHours, cancellationToken);
            }
        }
    }

    private async Task AdmitWalkInAsync(WardInfo ward, DateTimeOffset at, CancellationToken cancellationToken)
    {
        var physician = s.Random.Pick(w.Physicians.Where(p => p.Department == ward.Department).DefaultIfEmpty(w.Physicians[0]).ToList());
        for (var attempt = 0; attempt < 12; attempt++)
        {
            var patient = s.Random.Pick(w.Patients);
            if (patient.RegisteredAt >= at.AddHours(-1) || IsAdmitted(patient) || patient == w.FeaturedPatient)
            {
                continue;
            }

            if (FindBed(physician, at, ward) is { } place)
            {
                await AdmitAsync(patient, physician, place, null, at, null, cancellationToken);
            }

            return;
        }
    }

    private async Task TransferAsync(Stay stay, DateTimeOffset at, CancellationToken cancellationToken)
    {
        stay.TransferAt = null;
        if (FindBed(null, at, null, stay.Ward.Id) is not { } place)
        {
            return;
        }

        var mover = stay.Ward.HeadNurse;
        s.Act(mover.UserId, at);
        var reason = s.Random.Pick(["Needs closer monitoring.", "Condition improved, step down.", "Isolation required.", "Bed needed for a surgical admission."]);
        var previous = stay.Bed;
        var next = Ensure.Ok(stay.Admission.Transfer(previous, place.Bed.Id, place.Ward.Id, reason, mover.UserId, at));
        s.Db.Add(next);
        s.Keep(next);
        s.Audit.Event(AuditEvents.Clinical.PatientTransferred, new AuditOptions
        {
            Target = new AuditTarget(nameof(Admission), stay.Admission.Id),
            PatientId = stay.Patient.Id,
            Details = new { from = stay.Ward.Code, to = place.Ward.Code, bed = place.Bed.Label },
        });
        await s.SaveAsync(cancellationToken);

        var movedAt = next.During.LowerBound;
        var last = stay.Segments[^1];
        stay.Segments[^1] = (last.Ward, last.From, new DateTimeOffset(movedAt, TimeSpan.Zero));
        stay.Segments.Add((place.Ward, new DateTimeOffset(movedAt, TimeSpan.Zero), null));
        _occupiedBeds.Remove(previous.BedId);
        _bedFreeAt[previous.BedId] = new DateTimeOffset(movedAt, TimeSpan.Zero);
        _occupiedBeds.Add(place.Bed.Id);
        s.Release(previous);
        stay.Bed = next;
        stay.Ward = place.Ward;
    }

    private async Task DischargeAsync(Stay stay, DateTimeOffset at, CancellationToken cancellationToken)
    {
        var physician = stay.Physician.Person;
        s.Act(physician.UserId, at);
        var disposition = s.Random.Pick(
            (DischargeDisposition.Home, 0.92), (DischargeDisposition.AgainstAdvice, 0.04), (DischargeDisposition.TransferredOut, 0.04));
        var summary = s.Random.Pick(DemoCatalog.DischargeSummaries);
        var note = Ensure.Ok(stay.Admission.Discharge(stay.Encounter, stay.Bed, disposition, summary, physician.UserId, physician.EmployeeId, at));
        s.Db.Add(note);
        s.Audit.Event(AuditEvents.Clinical.PatientDischarged, new AuditOptions
        {
            Target = new AuditTarget(nameof(Admission), stay.Admission.Id),
            PatientId = stay.Patient.Id,
            Details = new { disposition, ward = stay.Ward.Code, summaryNoteId = note.Id },
        });
        s.Audit.Event(AuditEvents.Clinical.NoteWritten, new AuditOptions
        {
            Target = new AuditTarget(nameof(ClinicalNote), note.Id),
            PatientId = stay.Patient.Id,
            Details = new { encounterId = stay.Encounter.Id, type = note.NoteType },
        });
        s.Audit.Event(AuditEvents.Clinical.EncounterClosed, new AuditOptions
        {
            Target = new AuditTarget(nameof(Encounter), stay.Encounter.Id),
            PatientId = stay.Patient.Id,
            Details = new { type = stay.Encounter.Type },
        });
        await s.SaveAsync(cancellationToken);

        var leftAt = stay.Admission.DischargedAt ?? at;
        var last = stay.Segments[^1];
        stay.Segments[^1] = (last.Ward, last.From, leftAt);
        _open.Remove(stay);
        _closedToday.Add(stay); // still on today's rounds, up to the moment they left
        _admitted.Remove(stay.Patient.Id);
        _occupiedBeds.Remove(stay.Bed.BedId);
        _bedFreeAt[stay.Bed.BedId] = leftAt;
        s.Release(stay.Encounter);
        s.Release(stay.Admission);
        s.Release(stay.Bed);
    }

    /// <summary>Each attending sees their patients on morning rounds: a progress note, now and then a changed order.</summary>
    private async Task PhysicianRoundsAsync(DateOnly day, CancellationToken cancellationToken)
    {
        foreach (var group in _open.Concat(_closedToday).GroupBy(x => x.Physician).OrderBy(g => g.Key.Person.EmployeeId))
        {
            var physician = group.Key.Person;
            var max = DateTimeOffset.MinValue;
            var any = false;
            foreach (var stay in group)
            {
                var at = s.Local(day, new TimeOnly(9, 0)).AddMinutes(s.Random.Next(0, 150));
                if (at <= stay.AdmittedAt.AddHours(2) || at > s.Now || at.AddMinutes(10) >= stay.PlannedDischarge)
                {
                    continue;
                }

                records.Viewed(physician, stay.Patient, at);
                if (s.Random.Chance(0.8))
                {
                    records.Note(stay.Encounter, stay.Patient, NoteType.Progress, s.Random.Pick(DemoCatalog.ProgressNotes), physician, at.AddMinutes(4));
                }

                if (stay.Orders.Count > 1 && s.Random.Chance(0.07) && stay.Orders.FirstOrDefault(o => o.Order.IsActive && !o.AsNeeded) is { } stop)
                {
                    s.Act(physician.UserId, at.AddMinutes(6));
                    s.Db.Attach(stop.Order);
                    Ensure.Ok(stop.Order.Discontinue(physician.UserId, "Course completed; no longer needed.", s.Clock));
                    s.Audit.Event(AuditEvents.Clinical.OrderDiscontinued, new AuditOptions
                    {
                        Target = new AuditTarget(nameof(PhysicianOrder), stop.Order.Id),
                        PatientId = stay.Patient.Id,
                        Details = new { encounterId = stay.Encounter.Id, medicationName = stop.Order.MedicationName },
                    });
                }

                any = true;
                max = at.AddMinutes(6) > max ? at.AddMinutes(6) : max;
            }

            if (any)
            {
                s.Act(physician.UserId, max);
                await s.SaveAsync(cancellationToken);
            }
        }
    }

    /// <summary>One nurse on one ward for one shift: vitals every four hours, the doses that fall due, a note, all as that nurse.</summary>
    private async Task NurseRoundsAsync(WardInfo ward, DateOnly day, ShiftColor shift, int startHour, DateTimeOffset from, DateTimeOffset to, int[] vitalHours, CancellationToken cancellationToken)
    {
        var stays = _open.Concat(_closedToday).Where(x => x.Segments.Any(seg => seg.Ward.Id == ward.Id && seg.From < to && (seg.To is null || seg.To > from))).ToList();
        if (stays.Count == 0)
        {
            return;
        }

        var nurse = NurseOn(ward, from, shift, day);
        var max = DateTimeOffset.MinValue;
        var any = false;
        foreach (var stay in stays)
        {
            var seen = false;
            foreach (var hour in vitalHours)
            {
                var at = s.Local(day, new TimeOnly(0, 0)).AddHours(hour).AddMinutes(s.Random.Next(0, 25));
                if (!Present(stay, ward, at))
                {
                    continue;
                }

                seen = true;
                records.Vitals(stay.Encounter, stay.Patient, nurse, at, stay.Acute && at < stay.AdmittedAt.AddDays(2), full: false);
                max = Later(max, at);
            }

            foreach (var item in stay.Orders)
            {
                foreach (var hour in item.Hours)
                {
                    var due = s.Local(day, new TimeOnly(0, 0)).AddHours(hour < startHour ? hour + 24 : hour).AddMinutes(s.Random.Next(-5, 40));
                    if (due < from || due >= to || !Present(stay, ward, due) || due < item.Order.OrderedAt.AddMinutes(5))
                    {
                        continue;
                    }

                    if (!item.Order.IsActive && item.Order.DiscontinuedAt is { } stopped && due >= stopped)
                    {
                        continue;
                    }

                    if (item.AsNeeded && !s.Random.Chance(0.3))
                    {
                        continue;
                    }

                    seen = true;
                    records.Dose(item.Order, stay.Patient, nurse, due);
                    max = Later(max, due);
                }
            }

            if (!seen)
            {
                continue;
            }

            any = true;
            var noteAt = from.AddHours(s.Random.Next(1, 7));
            if (Present(stay, ward, noteAt) && s.Random.Chance(0.55))
            {
                var type = s.Random.Chance(0.06) ? NoteType.Diet : NoteType.Nursing;
                records.Note(stay.Encounter, stay.Patient, type, s.Random.Pick(type == NoteType.Diet ? DemoCatalog.DietNotes : DemoCatalog.NursingNotes), nurse, noteAt);
                max = Later(max, noteAt);
            }

            var viewedAt = from.AddMinutes(s.Random.Next(5, 40));
            if (Present(stay, ward, viewedAt))
            {
                records.Viewed(nurse, stay.Patient, viewedAt);
            }
        }

        if (any)
        {
            s.Act(nurse.UserId, max);
            await s.SaveAsync(cancellationToken);
        }
    }

    private bool Present(Stay stay, WardInfo ward, DateTimeOffset at) =>
        at <= s.Now && at > stay.AdmittedAt.AddMinutes(10) && stay.IsIn(ward, at)
        && (stay.Admission.DischargedAt is not { } discharged || at < discharged);

    /// <summary>Someone on this ward's roster for that shift, or the Head Nurse covering.</summary>
    public Person NurseOn(WardInfo ward, DateTimeOffset at, ShiftColor? shift = null, DateOnly? day = null)
    {
        var local = TimeZoneInfo.ConvertTime(at, s.Zone);
        var chosenShift = shift ?? (local.Hour is >= 6 and < 14 ? ShiftColor.Am : local.Hour is >= 14 and < 22 ? ShiftColor.Pm : ShiftColor.Noc);
        var chosenDay = day ?? (shift is null && local.Hour < 6 && chosenShift == ShiftColor.Noc ? DateOnly.FromDateTime(local.DateTime).AddDays(-1) : DateOnly.FromDateTime(local.DateTime));
        return w.Roster.TryGetValue((ward.Id, chosenDay, chosenShift), out var onDuty) && onDuty.Count > 0
            ? s.Random.Pick(onDuty)
            : ward.HeadNurse;
    }

    private DateTimeOffset PlannedDischarge(DateTimeOffset admittedAt)
    {
        var days = s.Random.Pick((s.Random.Next(1, 3), 0.2), (s.Random.Next(3, 5), 0.4), (s.Random.Next(5, 8), 0.3), (s.Random.Next(8, 15), 0.1));
        var day = s.LocalDay(admittedAt).AddDays(days);
        return s.Local(day, new TimeOnly(s.Random.Next(9, 17), s.Random.Next(0, 60)));
    }

    /// <summary>Marks some new stays for a move to another ward partway through.</summary>
    public void MaybePlanTransfer(Stay stay)
    {
        if (s.Random.Chance(0.07) && stay.PlannedDischarge > stay.AdmittedAt.AddDays(2))
        {
            stay.TransferAt = stay.AdmittedAt.AddDays(1).AddHours(s.Random.Next(4, 14));
        }
    }

    private static DateTimeOffset Later(DateTimeOffset a, DateTimeOffset b) => a > b ? a : b;
}
