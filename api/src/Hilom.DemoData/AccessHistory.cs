using Hilom.Infrastructure.Auditing;
using Hilom.SharedKernel.Auditing;
using Hilom.Workforce.Domain;

namespace Hilom.DemoData;

/// <summary>Who signed in, when, and the occasional wrong password or refused request: what the audit viewer and the admin dashboard show.</summary>
internal sealed class AccessHistory(SeedSession s, DemoWorld w)
{
    private static readonly string[] Strangers =
    [
        "j.delacruz@mail.test", "admin@hospital.test", "nurse1@mail.test", "info@mail.test", "r.santos@mail.test", "guest@mail.test",
    ];

    public async Task DayAsync(DateOnly day, CancellationToken cancellationToken)
    {
        var morning = s.Local(day, new TimeOnly(7, 0));
        var weekday = day.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday);

        foreach (var ((_, rosterDay, shift), nurses) in w.Roster)
        {
            if (rosterDay != day)
            {
                continue;
            }

            var start = shift switch { ShiftColor.Am => 6, ShiftColor.Pm => 14, _ => 22 };
            foreach (var nurse in nurses)
            {
                SignIn(nurse.UserId, s.Local(day, new TimeOnly(start, 0)).AddMinutes(-s.Random.Next(0, 25)));
            }
        }

        if (weekday)
        {
            foreach (var physician in w.Physicians.Where(_ => s.Random.Chance(0.85)))
            {
                SignIn(physician.Person.UserId, morning.AddMinutes(s.Random.Next(0, 75)));
            }

            foreach (var person in w.FrontDesk.Append(w.Hr).Append(w.ChiefNurse))
            {
                SignIn(person.UserId, morning.AddMinutes(s.Random.Next(0, 60)));
            }

            if (s.Random.Chance(0.25))
            {
                SignIn(w.AdminUserId, morning.AddHours(2).AddMinutes(s.Random.Next(0, 120)));
                s.Audit.Event(AuditEvents.Access.AuditViewed, new AuditOptions());
            }
        }

        // Someone mistypes a password now and then, or tries an address that isn't theirs.
        if (s.Random.Chance(0.4))
        {
            Failed(s.Local(day, new TimeOnly(s.Random.Next(0, 23), s.Random.Next(0, 60))));
        }

        // Sign-ins from patients' families through the portal.
        foreach (var patient in w.Patients.Where(p => p.PortalUserId is not null && s.Random.Chance(0.03)))
        {
            SignIn(patient.PortalUserId!.Value, s.Local(day, new TimeOnly(s.Random.Next(8, 22), s.Random.Next(0, 60))));
        }

        s.Act(w.AdminUserId, s.Local(day, new TimeOnly(23, 59)));
        await s.SaveAsync(cancellationToken);
    }

    /// <summary>A few critical events in the last 24 hours, so the administrator's dashboard has something to show.</summary>
    public async Task RecentSecurityEventsAsync(CancellationToken cancellationToken)
    {
        Failed(s.Now.AddHours(-3).AddMinutes(-s.Random.Next(0, 30)));
        Failed(s.Now.AddHours(-9));
        var nurse = s.Random.Pick(w.Wards).Nurses[^1];
        for (var i = 0; i < 5; i++)
        {
            s.Act(null, s.Now.AddHours(-14).AddMinutes(i));
            s.Audit.Event(AuditEvents.Auth.LoginFailed, new AuditOptions { Outcome = AuditOutcome.Failed, AttemptedIdentifier = "nurse.demo@hilom.test" });
        }

        s.Act(null, s.Now.AddHours(-14).AddMinutes(6));
        s.Audit.Event(AuditEvents.Auth.AccountLockedOut, new AuditOptions { Outcome = AuditOutcome.Failed, AttemptedIdentifier = "nurse.demo@hilom.test", ActorId = nurse.UserId });

        // Front desk tries to open something that isn't theirs to see.
        s.Act(w.FrontDesk[0].UserId, s.Now.AddHours(-6));
        s.Audit.Event(AuditEvents.Access.Denied, new AuditOptions
        {
            Outcome = AuditOutcome.Denied,
            Details = new { permission = "clinical.notes.view", reason = "missing_permission" },
        });
        s.Act(w.AdminUserId, s.Now.AddMinutes(-1));
        await s.SaveAsync(cancellationToken);
    }

    private void SignIn(Guid userId, DateTimeOffset at)
    {
        s.Act(userId, at);
        s.Audit.Event(AuditEvents.Auth.LoginSucceeded, new AuditOptions { Target = new AuditTarget("User", userId) });
    }

    private void Failed(DateTimeOffset at)
    {
        s.Act(null, at);
        s.Audit.Event(AuditEvents.Auth.LoginFailed, new AuditOptions { Outcome = AuditOutcome.Failed, AttemptedIdentifier = s.Random.Pick(Strangers) });
    }
}
