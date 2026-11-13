using System.Globalization;
using Hilom.Access.Domain;
using Hilom.Facility.Domain;
using Hilom.Infrastructure.Auditing;
using Hilom.SharedKernel.Auditing;
using Hilom.SharedKernel.Domain;
using Hilom.Workforce.Domain;
using Microsoft.EntityFrameworkCore;

namespace Hilom.DemoData;

/// <summary>
/// Departments, wards and beds, then the people: accounts, employees, ward assignments, rosters and shift swaps.
/// Each is made by the role that would make it (HR hires, the Chief Nurse assigns and rosters), through the
/// domain types, so the audit trail shows who did what.
/// </summary>
internal sealed class StaffGenerator(SeedSession s, DemoWorld w, DemoSize size)
{
    private static readonly ShiftColor[] ShiftOrder = [ShiftColor.Am, ShiftColor.Pm, ShiftColor.Noc];

    private readonly Dictionary<string, Guid> _departments = [];
    private readonly Dictionary<string, Guid> _positions = [];
    private readonly Dictionary<string, Guid> _specializations = [];
    private readonly HashSet<string> _emails = [];
    private readonly Dictionary<(Guid Employee, DateOnly Day), RosterEntry> _entries = [];
    private DateOnly _historyStart;
    private int _phoneCounter;

    public async Task RunAsync(DateOnly historyStart, CancellationToken cancellationToken)
    {
        _historyStart = historyStart;
        var setupAt = s.Local(historyStart.AddDays(-30), new TimeOnly(8, 0));
        foreach (var role in await s.Db.Set<Role>().AsNoTracking().ToListAsync(cancellationToken))
        {
            w.RoleIds[role.Name] = role.Id;
        }

        await CreateAdministratorAsync(setupAt, cancellationToken);
        await CreateLookupsShiftsAndWardsAsync(setupAt.AddMinutes(30), cancellationToken);
        await HireEveryoneAsync(setupAt.AddHours(2), cancellationToken);
        await AssignNursesAsync(setupAt.AddDays(1), cancellationToken);
        await BuildRosterAsync(setupAt.AddDays(2), cancellationToken);
        await GenerateSwapsAsync(cancellationToken);
    }

    private async Task CreateAdministratorAsync(DateTimeOffset at, CancellationToken cancellationToken)
    {
        s.Act(null, at);
        var user = NewUser("admin@demo.hilom.test", "Alex Rivera", w.DemoPasswordHash, Role.SystemAdministratorName, null, at);
        w.AdminUserId = user.Id;
        await s.SaveAsync(cancellationToken);
    }

    private async Task CreateLookupsShiftsAndWardsAsync(DateTimeOffset at, CancellationToken cancellationToken)
    {
        s.Act(w.AdminUserId, at);
        foreach (var name in DemoCatalog.Departments)
        {
            var department = Department.Create(name);
            s.Db.Add(department);
            _departments[name] = department.Id;
        }

        foreach (var name in DemoCatalog.Positions)
        {
            var position = Position.Create(name);
            s.Db.Add(position);
            _positions[name] = position.Id;
        }

        foreach (var name in DemoCatalog.Specializations)
        {
            var specialization = Specialization.Create(name);
            s.Db.Add(specialization);
            _specializations[name] = specialization.Id;
        }

        var shifts = new[]
        {
            Ensure.Ok(Shift.Create("AM", "Day shift", new TimeOnly(6, 0), new TimeOnly(14, 0), ShiftColor.Am, 1)),
            Ensure.Ok(Shift.Create("PM", "Evening shift", new TimeOnly(14, 0), new TimeOnly(22, 0), ShiftColor.Pm, 2)),
            Ensure.Ok(Shift.Create("NOC", "Night shift", new TimeOnly(22, 0), new TimeOnly(6, 0), ShiftColor.Noc, 3)),
        };
        s.Db.AddRange(shifts);
        foreach (var shift in shifts)
        {
            w.ShiftIds[shift.Color] = shift.Id;
        }

        await s.SaveAsync(cancellationToken);
    }

    private async Task HireEveryoneAsync(DateTimeOffset at, CancellationToken cancellationToken)
    {
        // HR is hired by the administrator; everyone else by HR.
        s.Act(w.AdminUserId, at);
        w.Hr = await HireAsync(new Hire("Lourdes", "Navarro", Sex.Female, 38, 50, "Human Resources", "HR Officer", ProfessionalType.Administrative, "HR Officer", "hr@demo.hilom.test"), w.AdminUserId, at, cancellationToken);

        s.Act(w.Hr.UserId, at.AddMinutes(20));
        w.ChiefNurse = await HireAsync(new Hire("Teresita", "Villanueva", Sex.Female, 45, 58, "Nursing Administration", "Chief Nurse", ProfessionalType.Nurse, "Chief Nurse", "chief.nurse@demo.hilom.test"), w.Hr.UserId, at.AddMinutes(20), cancellationToken);

        for (var i = 0; i < 4; i++)
        {
            var demo = i == 0;
            w.FrontDesk.Add(await HireAsync(
                demo
                    ? new Hire("Joy", "Bautista", Sex.Female, 25, 35, "Admitting and Records", "Front Desk Clerk", ProfessionalType.Administrative, "Front Desk", "front.desk@demo.hilom.test")
                    : RandomHire("Admitting and Records", "Front Desk Clerk", ProfessionalType.Administrative, "Front Desk", 22, 45),
                w.Hr.UserId, at.AddMinutes(40 + i), cancellationToken));
        }

        for (var i = 0; i < size.Physicians; i++)
        {
            var (specialization, department) = DemoCatalog.PhysicianPlacement[i % DemoCatalog.PhysicianPlacement.Length];
            var hire = i == 0
                ? new Hire("Elena", "Santos", Sex.Female, 42, 52, department, "Consultant Physician", ProfessionalType.Physician, "Physician", "physician@demo.hilom.test")
                : RandomHire(department, i % 3 == 0 ? "Resident Physician" : "Consultant Physician", ProfessionalType.Physician, "Physician", 30, 62);
            var minutes = i % 4 == 3 ? 20 : 30;
            var person = await HireAsync(
                hire with { Physician = new PhysicianProfile(_specializations[specialization], true, minutes), DisplayPrefix = "Dr. " },
                w.Hr.UserId, at.AddMinutes(60 + i), cancellationToken);
            w.Physicians.Add(new PhysicianInfo(person, _specializations[specialization], department, minutes));
        }

        for (var i = 0; i < size.Wards; i++)
        {
            var (code, name, type, department) = DemoCatalog.Wards[i % DemoCatalog.Wards.Length];
            var ward = new Ward(code, name, type, _departments[department]);
            var beds = Enumerable.Range(1, size.BedsPerWard).Select(n => new Bed(ward.Id, string.Create(CultureInfo.InvariantCulture, $"{code}-{n:D2}"))).ToList();
            s.Act(w.ChiefNurse.UserId, at.AddHours(1).AddMinutes(i));
            s.Db.Add(ward);
            s.Db.AddRange(beds);
            s.Audit.Event(AuditEvents.Facility.WardChanged, new AuditOptions { Target = new AuditTarget(nameof(Ward), ward.Id), Details = new { code, created = true } });
            await s.SaveAsync(cancellationToken);

            var isWard3 = code == "W3";
            s.Act(w.Hr.UserId, at.AddHours(2).AddMinutes(i));
            var head = await HireAsync(
                isWard3
                    ? new Hire("Ramon", "Dizon", Sex.Male, 40, 52, "Nursing Administration", "Head Nurse", ProfessionalType.Nurse, "Head Nurse", "head.nurse@demo.hilom.test")
                    : RandomHire("Nursing Administration", "Head Nurse", ProfessionalType.Nurse, "Head Nurse", 36, 55),
                w.Hr.UserId, at.AddHours(2).AddMinutes(i), cancellationToken);
            var nurses = new List<Person>();
            for (var n = 0; n < size.NursesPerWard; n++)
            {
                var demoNurse = isWard3 && n == 0;
                nurses.Add(await HireAsync(
                    demoNurse
                        ? new Hire("Maria", "Reyes", Sex.Female, 28, 34, "Nursing Administration", "Staff Nurse", ProfessionalType.Nurse, "Staff Nurse", "nurse@demo.hilom.test")
                        : RandomHire("Nursing Administration", "Staff Nurse", ProfessionalType.Nurse, "Staff Nurse", 23, 52),
                    w.Hr.UserId, at.AddHours(3).AddMinutes(i * 10 + n), cancellationToken));
            }

            w.Wards.Add(new WardInfo(ward.Id, code, name, department, [.. beds.Select(b => new BedInfo(b.Id, b.Label))], head, nurses));
        }
    }

    private async Task AssignNursesAsync(DateTimeOffset at, CancellationToken cancellationToken)
    {
        s.Act(w.ChiefNurse.UserId, at);
        var from = _historyStart.AddDays(-14);
        foreach (var ward in w.Wards)
        {
            foreach (var person in ward.Nurses.Append(ward.HeadNurse))
            {
                var assignment = Ensure.Ok(WardAssignment.Create(person.EmployeeId, ward.Id, from, null, "Initial assignment", w.ChiefNurse.UserId));
                s.Db.Add(assignment);
                s.Audit.Event(AuditEvents.Workforce.WardAssignmentChanged, new AuditOptions
                {
                    Target = new AuditTarget(nameof(WardAssignment), assignment.Id),
                    Details = new { ward = ward.Code, employeeId = person.EmployeeId, startsOn = from },
                });
            }

            await s.SaveAsync(cancellationToken);
        }
    }

    private async Task BuildRosterAsync(DateTimeOffset at, CancellationToken cancellationToken)
    {
        var last = s.Today.AddDays(DemoSize.FutureDays);
        var monday = StartOfWeek(_historyStart);
        for (var weekStart = monday; weekStart <= last; weekStart = weekStart.AddDays(7))
        {
            var week = (weekStart.DayNumber - monday.DayNumber) / 7;
            foreach (var ward in w.Wards)
            {
                var entries = new List<RosterEntry>();
                for (var d = 0; d < 7; d++)
                {
                    var day = weekStart.AddDays(d);
                    if (day < _historyStart || day > last)
                    {
                        continue;
                    }

                    // The head nurse works weekday days. Staff nurses work five days of seven, rotating through the shifts weekly.
                    if (d < 5)
                    {
                        entries.Add(Rostered(ward, ward.HeadNurse, day, ShiftColor.Am));
                    }

                    for (var i = 0; i < ward.Nurses.Count; i++)
                    {
                        if ((d + i) % 7 < 5)
                        {
                            entries.Add(Rostered(ward, ward.Nurses[i], day, ShiftOrder[(i + week) % 3]));
                        }
                    }
                }

                var published = weekStart.AddDays(-7);
                s.Act(w.ChiefNurse.UserId, published < _historyStart ? at : s.Local(published, new TimeOnly(16, 0)));
                s.Db.AddRange(entries);
                s.Audit.Event(AuditEvents.Workforce.RosterChanged, new AuditOptions { Details = new { ward = ward.Code, weekOf = weekStart, entries = entries.Count } });
                await s.SaveAsync(cancellationToken);
            }
        }
    }

    private RosterEntry Rostered(WardInfo ward, Person person, DateOnly day, ShiftColor shift)
    {
        var entry = new RosterEntry(person.EmployeeId, ward.Id, w.ShiftIds[shift], day);
        _entries[(person.EmployeeId, day)] = entry;
        if (!w.Roster.TryGetValue((ward.Id, day, shift), out var onDuty))
        {
            w.Roster[(ward.Id, day, shift)] = onDuty = [];
        }

        onDuty.Add(person);
        return entry;
    }

    /// <summary>
    /// Shift swaps in every state: one waiting for the demo nurse to answer, one waiting for the Head Nurse, one approved
    /// (with the roster changed as the endpoint does), one declined and one rejected.
    /// </summary>
    private async Task GenerateSwapsAsync(CancellationToken cancellationToken)
    {
        var ward = w.Wards.FirstOrDefault(x => x.Code == "W3") ?? w.Wards[0];
        if (ward.Nurses.Count < 4)
        {
            return;
        }

        var maria = ward.Nurses[0];
        var jose = ward.Nurses[1];
        var ana = ward.Nurses[2];
        var carlo = ward.Nurses[3];
        var today = s.Today;

        // Waiting for Maria to accept or decline (her swap inbox).
        if (FindPair(jose, maria, today.AddDays(2), today.AddDays(12)) is { } p1)
        {
            await RequestAsync(jose, p1, s.Now.AddHours(-5), "Family event, could you cover this one? Happy to take yours.", cancellationToken);
        }

        // Maria asked Ana, who accepted. Waiting for the Head Nurse.
        if (FindPair(maria, ana, today.AddDays(3), today.AddDays(13)) is { } p2)
        {
            var swap = await RequestAsync(maria, p2, s.Now.AddHours(-30), "Clinic appointment that morning.", cancellationToken);
            s.Act(ana.UserId, s.Now.AddHours(-20));
            Ensure.Ok(swap.Accept(s.Clock, "Sure, that works for me."));
            s.Audit.Event(AuditEvents.Workforce.SwapAccepted, SwapAudit(swap));
            await s.SaveAsync(cancellationToken);
        }

        // Approved last week.
        if (FindPair(ana, carlo, today.AddDays(-9), today.AddDays(-3)) is { } p3)
        {
            var swap = await RequestAsync(ana, p3, s.Now.AddDays(-12), "Exam preparation.", cancellationToken);
            s.Act(carlo.UserId, s.Now.AddDays(-11));
            Ensure.Ok(swap.Accept(s.Clock, null));
            s.Audit.Event(AuditEvents.Workforce.SwapAccepted, SwapAudit(swap));
            await s.SaveAsync(cancellationToken);

            s.Act(ward.HeadNurse.UserId, s.Now.AddDays(-10));
            var requesterEntry = s.Db.Attach(_entries[(ana.EmployeeId, p3.RequesterDay)]).Entity;
            var targetEntry = s.Db.Attach(_entries[(carlo.EmployeeId, p3.TargetDay)]).Entity;
            requesterEntry.MarkSwappedOut();
            targetEntry.MarkSwappedOut();
            await s.SaveAsync(cancellationToken); // free both days first, as the endpoint does
            s.Act(ward.HeadNurse.UserId, s.Now.AddDays(-10).AddMinutes(1));
            var targetTakes = new RosterEntry(carlo.EmployeeId, ward.Id, p3.RequesterShift, p3.RequesterDay);
            var requesterTakes = new RosterEntry(ana.EmployeeId, ward.Id, p3.TargetShift, p3.TargetDay);
            s.Db.Attach(swap);
            s.Db.AddRange(targetTakes, requesterTakes);
            Ensure.Ok(swap.Approve(ward.HeadNurse.UserId, s.Clock, "Approved."));
            s.Audit.Event(AuditEvents.Workforce.SwapApproved, SwapAudit(swap));
            await s.SaveAsync(cancellationToken);
        }

        // Declined by the colleague.
        if (FindPair(carlo, jose, today.AddDays(5), today.AddDays(13)) is { } p4)
        {
            var swap = await RequestAsync(carlo, p4, s.Now.AddDays(-2), "Any chance?", cancellationToken);
            s.Act(jose.UserId, s.Now.AddDays(-2).AddHours(3));
            Ensure.Ok(swap.Decline(s.Clock, "Sorry, I can't that day."));
            s.Audit.Event(AuditEvents.Workforce.SwapDeclined, SwapAudit(swap));
            await s.SaveAsync(cancellationToken);
        }
    }

    private async Task<ShiftSwapRequest> RequestAsync(Person requester, SwapPair pair, DateTimeOffset at, string message, CancellationToken cancellationToken)
    {
        s.Act(requester.UserId, at);
        var mine = _entries[(requester.EmployeeId, pair.RequesterDay)];
        var theirs = _entries[(pair.TargetEmployeeId, pair.TargetDay)];
        var swap = new ShiftSwapRequest(mine, theirs, message);
        s.Db.Add(swap);
        s.Keep(swap); // answered and reviewed later
        s.Audit.Event(AuditEvents.Workforce.SwapRequested, SwapAudit(swap));
        await s.SaveAsync(cancellationToken);
        return swap;
    }

    private static AuditOptions SwapAudit(ShiftSwapRequest swap) => new()
    {
        Target = new AuditTarget(nameof(ShiftSwapRequest), swap.Id),
        Details = new { requesterDay = swap.RequesterDutyDate, targetDay = swap.TargetDutyDate },
    };

    /// <summary>Two scheduled shifts on different days that can be exchanged: neither person works the other's day.</summary>
    private SwapPair? FindPair(Person requester, Person target, DateOnly from, DateOnly to)
    {
        for (var a = from; a <= to; a = a.AddDays(1))
        {
            if (!_entries.TryGetValue((requester.EmployeeId, a), out var mine) || _entries.ContainsKey((target.EmployeeId, a)))
            {
                continue;
            }

            for (var b = from; b <= to; b = b.AddDays(1))
            {
                if (b != a && _entries.TryGetValue((target.EmployeeId, b), out var theirs) && !_entries.ContainsKey((requester.EmployeeId, b)))
                {
                    return new SwapPair(a, mine.ShiftId, target.EmployeeId, b, theirs.ShiftId);
                }
            }
        }

        return null;
    }

    private sealed record SwapPair(DateOnly RequesterDay, Guid RequesterShift, Guid TargetEmployeeId, DateOnly TargetDay, Guid TargetShift);

    private User NewUser(string email, string displayName, string passwordHash, string role, Guid? assignedBy, DateTimeOffset at)
    {
        var user = new User(email, displayName);
        user.ChangePassword(passwordHash);
        s.Db.Add(user);
        s.Db.Add(new UserRole(user.Id, w.RoleIds[role], assignedBy, at));
        s.Audit.Event(AuditEvents.Access.UserCreated, new AuditOptions
        {
            Target = new AuditTarget(nameof(User), user.Id),
            Details = new { roles = new[] { role } },
        });
        return user;
    }

    private async Task<Person> HireAsync(Hire hire, Guid hiredBy, DateTimeOffset at, CancellationToken cancellationToken)
    {
        s.Act(hiredBy, at);
        var number = await s.Db.Database
            .SqlQueryRaw<long>("SELECT nextval('workforce.employee_number_seq') AS \"Value\"")
            .SingleAsync(cancellationToken);
        var email = hire.DemoEmail ?? UniqueEmail(hire.First, hire.Last);
        var age = s.Random.Next(hire.AgeFrom, hire.AgeTo + 1);
        var birth = s.Today.AddYears(-age).AddDays(-s.Random.Next(0, 365));
        var hired = _historyStart.AddDays(-s.Random.Next(200, 3000));
        var details = new EmployeeDetails(
            hire.First, null, hire.Last, hire.Sex, birth,
            Phone(), email, _departments[hire.Department], _positions[hire.Position], hire.Type,
            hire.Type is ProfessionalType.Physician or ProfessionalType.Nurse ? string.Create(CultureInfo.InvariantCulture, $"PRC-{s.Random.Next(1_000_000, 9_999_999)}") : null,
            EmploymentType.FullTime, hired, hire.Physician);
        var employee = Ensure.Ok(Employee.Create(string.Create(CultureInfo.InvariantCulture, $"EMP-{s.Today.Year}-{number:D4}"), details));
        s.Db.Add(employee);

        var displayName = $"{hire.DisplayPrefix}{hire.First} {hire.Last}";
        var isDemo = hire.DemoEmail is not null;
        var user = NewUser(email, displayName, isDemo ? w.DemoPasswordHash : w.UnusablePasswordHash, hire.Role, hiredBy, at);
        Ensure.Ok(employee.LinkAccount(user.Id));
        s.Audit.Event(AuditEvents.Workforce.EmployeeCreated, new AuditOptions
        {
            Target = new AuditTarget(nameof(Employee), employee.Id),
            Details = new { employeeNo = employee.EmployeeNo, professionalType = hire.Type },
        });
        await s.SaveAsync(cancellationToken);
        return new Person(user.Id, employee.Id, displayName);
    }

    private Hire RandomHire(string department, string position, ProfessionalType type, string role, int ageFrom, int ageTo)
    {
        var sex = s.Random.Chance(0.62) ? Sex.Female : Sex.Male;
        var first = s.Random.Pick(sex == Sex.Female ? DemoCatalog.FemaleNames : DemoCatalog.MaleNames);
        return new Hire(first, s.Random.Pick(DemoCatalog.Surnames), sex, ageFrom, ageTo, department, position, type, role);
    }

    private string UniqueEmail(string first, string last)
    {
        var stem = $"{first}.{last}".ToLowerInvariant().Replace(" ", "", StringComparison.Ordinal);
        var email = $"{stem}@hilom.test";
        for (var n = 2; !_emails.Add(email); n++)
        {
            email = string.Create(CultureInfo.InvariantCulture, $"{stem}{n}@hilom.test");
        }

        return email;
    }

    // Fictional: the 0900-000 block isn't assigned to anyone.
    private string Phone() => string.Create(CultureInfo.InvariantCulture, $"0900-000-{++_phoneCounter % 10000:D4}");

    private static DateOnly StartOfWeek(DateOnly day) => day.AddDays(-(((int)day.DayOfWeek + 6) % 7));

    private sealed record Hire(
        string First,
        string Last,
        Sex Sex,
        int AgeFrom,
        int AgeTo,
        string Department,
        string Position,
        ProfessionalType Type,
        string Role,
        string? DemoEmail = null)
    {
        public PhysicianProfile? Physician { get; init; }

        public string DisplayPrefix { get; init; } = "";
    }
}
