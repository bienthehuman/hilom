using Hilom.Clinical.Domain;
using Hilom.Infrastructure.Auditing;
using Hilom.Scheduling.Domain;
using Hilom.SharedKernel.Auditing;
using Hilom.Workforce.Domain;

namespace Hilom.DemoData;

/// <summary>
/// Physicians publish availability; patients are booked, confirmed, checked in and seen; some are admitted from the
/// visit. Every step is made by the role that does it, and every status change goes through <see cref="Appointment"/>
/// and <see cref="Encounter"/>, so a booking that the rules wouldn't allow can't appear.
/// </summary>
internal sealed class ClinicGenerator(SeedSession s, DemoWorld w, DemoSize size, ClinicalRecords records, WardGenerator wards)
{
    private readonly Dictionary<DateOnly, List<Slot>> _slots = [];
    private readonly Dictionary<DateOnly, Script> _scripts = [];

    private enum Script
    {
        EarlierVisit,
        AdmittedFromVisit,
        FutureBooking,
    }

    private sealed record Slot(Guid Id, PhysicianInfo Physician, DateTimeOffset Start, DateTimeOffset End);

    public async Task PublishSlotsAsync(DateOnly historyStart, CancellationToken cancellationToken)
    {
        var last = s.Today.AddDays(DemoSize.FutureDays);
        var firstMonday = historyStart.AddDays(-(((int)historyStart.DayOfWeek + 6) % 7));
        var earliest = s.Local(historyStart.AddDays(-20), new TimeOnly(9, 0));
        for (var i = 0; i < w.Physicians.Count; i++)
        {
            var physician = w.Physicians[i];
            var days = new HashSet<DayOfWeek> { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday };
            days.Remove((DayOfWeek)(1 + (i % 5))); // one clinic-free weekday each
            var start = new TimeOnly(i % 2 == 0 ? 8 : 9, 0);
            var end = start.AddMinutes(size.ClinicSlotsPerPhysicianDay * physician.ConsultMinutes);
            for (var monday = firstMonday; monday <= last; monday = monday.AddDays(7))
            {
                var pattern = new WeeklyPattern(monday, monday.AddDays(4), days, start, end, physician.ConsultMinutes);
                var slots = pattern.Slots(s.Zone)
                    .Where(x => s.LocalDay(x.StartsAt) >= historyStart)
                    .Select(x => Ensure.Ok(AvailabilitySlot.Create(physician.Person.EmployeeId, x.StartsAt, x.EndsAt)))
                    .ToList();
                if (slots.Count == 0)
                {
                    continue;
                }

                var publishedAt = s.Local(monday.AddDays(-7), new TimeOnly(17, 0));
                s.Act(physician.Person.UserId, publishedAt < earliest ? earliest : publishedAt);
                s.Db.AddRange(slots);
                s.Audit.Event(AuditEvents.Scheduling.SlotsPublished, new AuditOptions { Details = new { weekOf = monday, slots = slots.Count } });
                await s.SaveAsync(cancellationToken);
                foreach (var slot in slots)
                {
                    var day = s.LocalDay(slot.StartsAt);
                    if (!_slots.TryGetValue(day, out var list))
                    {
                        _slots[day] = list = [];
                    }

                    list.Add(new Slot(slot.Id, physician, slot.StartsAt, slot.EndsAt));
                }
            }
        }
    }

    /// <summary>Picks the days the featured family's story happens on, among days Dr. Santos has clinic.</summary>
    public void PlanScripts(int historyDays)
    {
        if (w.FeaturedPatient is null)
        {
            return;
        }

        var santos = w.Physicians[0];
        var today = s.Today;
        Plan(Script.EarlierVisit, today.AddDays(-Math.Min(45, historyDays * 2 / 3)), -1);
        Plan(Script.AdmittedFromVisit, today.AddDays(-3), -1);
        Plan(Script.FutureBooking, today.AddDays(6), 1);
        wards.ReserveBed(w.Wards.FirstOrDefault(x => x.Code == "W3") ?? w.Wards[0]);

        void Plan(Script script, DateOnly target, int step)
        {
            for (var day = target; Math.Abs(day.DayNumber - target.DayNumber) < 7; day = day.AddDays(step))
            {
                if (_slots.TryGetValue(day, out var list) && list.Any(x => x.Physician == santos) && !_scripts.ContainsKey(day))
                {
                    _scripts[day] = script;
                    return;
                }
            }
        }
    }

    public async Task DayAsync(DateOnly day, CancellationToken cancellationToken)
    {
        if (!_slots.TryGetValue(day, out var slots))
        {
            return;
        }

        var offset = day.DayNumber - s.Today.DayNumber;
        var fill = offset <= 0 ? size.ClinicFill : size.ClinicFill * Math.Max(0.15, 1 - offset / 16.0);
        _scripts.TryGetValue(day, out var script);
        var scripted = false;
        foreach (var slot in slots.OrderBy(x => x.Start).ThenBy(x => x.Physician.Person.EmployeeId))
        {
            if (!scripted && _scripts.ContainsKey(day) && slot.Physician == w.Physicians[0])
            {
                scripted = true;
                await RunAppointmentAsync(slot, w.FeaturedPatient!, day, script, cancellationToken);
                continue;
            }

            if (!s.Random.Chance(fill))
            {
                continue;
            }

            for (var attempt = 0; attempt < 8; attempt++)
            {
                var patient = s.Random.Pick(w.Patients);
                if (patient.RegisteredAt < slot.Start.AddDays(-1) && !wards.IsAdmitted(patient) && patient != w.FeaturedPatient)
                {
                    if (await RunAppointmentAsync(slot, patient, day, null, cancellationToken))
                    {
                        break;
                    }
                }
            }
        }
    }

    private async Task<bool> RunAppointmentAsync(Slot slot, PatientInfo patient, DateOnly day, Script? script, CancellationToken cancellationToken)
    {
        var physician = slot.Physician;
        var reason = s.Random.Pick(DemoCatalog.ClinicReasons);
        var happens = slot.Start <= s.Now;

        var viaPortal = patient.PortalUserId is not null && patient.PortalCanBook && (script == Script.FutureBooking || s.Random.Chance(0.45));
        var bookerUser = viaPortal ? patient.PortalUserId!.Value : s.Random.Pick(w.FrontDesk).UserId;

        var bookedAt = script == Script.FutureBooking
            ? s.Now.AddHours(-s.Random.Next(3, 30))
            : s.Local(day.AddDays(-s.Random.Next(1, 11)), new TimeOnly(s.Random.Next(8, 17), s.Random.Next(0, 60)));
        var earliest = patient.RegisteredAt.AddMinutes(s.Random.Next(10, 180));
        var latest = Earlier(slot.Start.AddMinutes(-45), s.Now.AddMinutes(-5));
        if (bookedAt < earliest)
        {
            bookedAt = earliest;
        }

        if (bookedAt > latest)
        {
            bookedAt = latest.AddMinutes(-s.Random.Next(0, 240));
        }

        if (bookedAt < earliest)
        {
            return false;
        }

        s.Act(bookerUser, bookedAt);
        var appointment = new Appointment(slot.Id, patient.Id, bookerUser, reason);
        s.Db.Add(appointment);
        s.Keep(appointment);
        s.Audit.Event(AuditEvents.Scheduling.AppointmentBooked, Audit(appointment, patient, new { physicianId = physician.Person.EmployeeId, startsAt = slot.Start }));
        await s.SaveAsync(cancellationToken);

        try
        {
            // Dr. decides some time after the booking, never in the last half hour before it starts.
            var decideAt = script == Script.FutureBooking
                ? bookedAt.AddMinutes(45)
                : Later(bookedAt.AddMinutes(10), Earlier(bookedAt.AddHours(s.Random.Next(1, 20)).AddMinutes(s.Random.Next(0, 60)), slot.Start.AddMinutes(-30)));
            var pendingYet = decideAt > s.Now || (!happens && script != Script.FutureBooking && bookedAt > s.Now.AddHours(-60) && s.Random.Chance(0.6));
            if (pendingYet)
            {
                return true; // still waiting on the physician: shows on their dashboard
            }

            if (s.Random.Chance(happens ? 0.03 : 0.0) && script is null)
            {
                s.Act(physician.Person.UserId, decideAt);
                Ensure.Ok(appointment.Decline(physician.Person.UserId, decideAt, "Not available at that time. Please choose another slot."));
                s.Audit.Event(AuditEvents.Scheduling.AppointmentDeclined, Audit(appointment, patient, null));
                await s.SaveAsync(cancellationToken);
                return true;
            }

            s.Act(physician.Person.UserId, decideAt);
            Ensure.Ok(appointment.Confirm(physician.Person.UserId, decideAt, slot.Start, null));
            s.Audit.Event(AuditEvents.Scheduling.AppointmentConfirmed, Audit(appointment, patient, null));
            await s.SaveAsync(cancellationToken);

            if (script is null && s.Random.Chance(happens ? 0.06 : 0.04))
            {
                var cancelAt = Earlier(Later(decideAt.AddHours(1), slot.Start.AddHours(-s.Random.Next(2, 48))), s.Now);
                if (cancelAt > decideAt && cancelAt < slot.Start)
                {
                    var by = viaPortal ? bookerUser : w.FrontDesk[0].UserId;
                    s.Act(by, cancelAt);
                    Ensure.Ok(appointment.Cancel(by, cancelAt, slot.Start, "Can't make it that day."));
                    s.Audit.Event(AuditEvents.Scheduling.AppointmentCancelled, Audit(appointment, patient, null));
                    await s.SaveAsync(cancellationToken);
                    return true;
                }
            }

            if (!happens)
            {
                return true;
            }

            if (script is null && s.Random.Chance(0.05))
            {
                var noShowAt = slot.Start.AddMinutes(s.Random.Next(30, 61));
                if (noShowAt <= s.Now)
                {
                    s.Act(w.FrontDesk[0].UserId, noShowAt);
                    Ensure.Ok(appointment.MarkNoShow(noShowAt, slot.Start));
                    s.Audit.Event(AuditEvents.Scheduling.AppointmentNoShow, Audit(appointment, patient, null));
                    await s.SaveAsync(cancellationToken);
                    return true;
                }
            }

            await SeeAsync(slot, patient, appointment, day, reason, script, cancellationToken);
            return true;
        }
        finally
        {
            s.Release(appointment);
        }
    }

    private async Task SeeAsync(Slot slot, PatientInfo patient, Appointment appointment, DateOnly day, string reason, Script? script, CancellationToken cancellationToken)
    {
        var physician = slot.Physician;
        var checkInAt = slot.Start.AddMinutes(-s.Random.Next(5, 21));
        var frontDesk = s.Random.Pick(w.FrontDesk);
        s.Act(frontDesk.UserId, checkInAt);
        Ensure.Ok(appointment.CheckIn(day, day));
        var encounter = Encounter.CheckIn(patient.Id, appointment.Id, physician.Person.EmployeeId, reason, checkInAt);
        s.Db.Add(encounter);
        s.Keep(encounter);
        s.Audit.Event(AuditEvents.Scheduling.AppointmentCheckedIn, Audit(appointment, patient, null));
        s.Audit.Event(AuditEvents.Clinical.EncounterOpened, new AuditOptions
        {
            Target = new AuditTarget(nameof(Encounter), encounter.Id),
            PatientId = patient.Id,
            Details = new { type = encounter.Type, appointmentId = appointment.Id },
        });
        records.Viewed(frontDesk, patient, checkInAt.AddMinutes(1));
        s.Act(frontDesk.UserId, checkInAt.AddMinutes(1));
        await s.SaveAsync(cancellationToken);

        try
        {
            var ward = s.Random.Pick(w.Wards);
            var nurse = wards.NurseOn(ward, slot.Start, ShiftColor.Am, day);
            var vitalsAt = checkInAt.AddMinutes(s.Random.Next(4, 10));
            if (vitalsAt > s.Now)
            {
                return;
            }

            records.Vitals(encounter, patient, nurse, vitalsAt, acute: reason.Contains("ever", StringComparison.Ordinal), full: true);
            await s.SaveAsync(cancellationToken);

            var noteAt = slot.Start.AddMinutes(s.Random.Next(6, 14));
            if (noteAt > s.Now)
            {
                return;
            }

            records.Viewed(physician.Person, patient, noteAt.AddMinutes(-1));
            var diagnosis = s.Random.Pick(DemoCatalog.AdmissionDiagnoses);
            records.Note(encounter, patient, NoteType.Assessment, s.Random.Pick(DemoCatalog.AssessmentNotes), physician.Person, noteAt);
            var changed = Ensure.Ok(encounter.UpdateSummary(reason, script == Script.AdmittedFromVisit ? diagnosis : "See assessment note."));
            if (changed.Count > 0)
            {
                s.Audit.Event(AuditEvents.Clinical.EncounterChanged, new AuditOptions
                {
                    Target = new AuditTarget(nameof(Encounter), encounter.Id),
                    PatientId = patient.Id,
                    Details = new { fields = changed },
                });
            }

            if (s.Random.Chance(0.3))
            {
                records.MedicationOrder(encounter, patient, physician.Person, noteAt.AddMinutes(3), []);
            }

            s.Act(physician.Person.UserId, noteAt.AddMinutes(4));
            await s.SaveAsync(cancellationToken);

            var admitAt = noteAt.AddMinutes(6);
            if (admitAt <= s.Now && (script == Script.AdmittedFromVisit || (script is null && s.Random.Chance(0.1))))
            {
                var preferred = script == Script.AdmittedFromVisit ? w.Wards.FirstOrDefault(x => x.Code == "W3") : null;
                var scripted = script == Script.AdmittedFromVisit;
                if (wards.FindBed(physician, admitAt, preferred, useReserved: scripted) is { } place)
                {
                    await wards.AdmitAsync(patient, physician, place, encounter.Id, admitAt, diagnosis, cancellationToken, useReserved: scripted);
                }

                if (scripted)
                {
                    wards.ReleaseReservedBed();
                }
            }

            var closeAt = noteAt.AddMinutes(s.Random.Next(8, Math.Max(9, physician.ConsultMinutes - 6)));
            if (closeAt > s.Now)
            {
                return;
            }

            s.Act(physician.Person.UserId, closeAt);
            Ensure.Ok(encounter.Close(closeAt));
            Ensure.Ok(appointment.Complete());
            s.Audit.Event(AuditEvents.Clinical.EncounterClosed, new AuditOptions
            {
                Target = new AuditTarget(nameof(Encounter), encounter.Id),
                PatientId = patient.Id,
                Details = new { type = encounter.Type, appointmentId = appointment.Id },
            });
            await s.SaveAsync(cancellationToken);
        }
        finally
        {
            s.Release(encounter);
        }
    }

    private static AuditOptions Audit(Appointment appointment, PatientInfo patient, object? details) => new()
    {
        Target = new AuditTarget(nameof(Appointment), appointment.Id),
        PatientId = patient.Id,
        Details = details,
    };

    private static DateTimeOffset Earlier(DateTimeOffset a, DateTimeOffset b) => a < b ? a : b;

    private static DateTimeOffset Later(DateTimeOffset a, DateTimeOffset b) => a > b ? a : b;
}
