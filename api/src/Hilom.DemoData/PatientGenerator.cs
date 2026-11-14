using System.Globalization;
using Hilom.Access.Domain;
using Hilom.Infrastructure.Auditing;
using Hilom.Patients.Domain;
using Hilom.SharedKernel.Auditing;
using Hilom.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;

namespace Hilom.DemoData;

/// <summary>
/// Patients, registered by front desk over the history; their allergies; and family portal accounts linked to some of
/// them through the real request-and-verify flow.
/// </summary>
internal sealed class PatientGenerator(SeedSession s, DemoWorld w, DemoSize size)
{
    private readonly HashSet<string> _emails = [];
    private int _phoneCounter;

    /// <summary>How many patients walk in on an ordinary day, so the registry grows through the history.</summary>
    public double PerDay(int historyDays) => size.Patients * 0.55 / historyDays;

    public async Task RegisterInitialAsync(DateTimeOffset at, CancellationToken cancellationToken)
    {
        var count = (int)(size.Patients * 0.45);
        for (var i = 0; i < count; i++)
        {
            // Records already on file when the history begins: registered in the weeks after the system went in.
            var when = at.AddMinutes(s.Random.Next(0, 28 * 24 * 60));
            Register(when, w.FrontDesk[i % w.FrontDesk.Count].UserId);
            if ((i + 1) % 250 == 0 || i == count - 1)
            {
                await s.SaveAsync(cancellationToken);
            }
        }

        await s.SaveAsync(cancellationToken);
    }

    /// <summary>Registers one patient, and often an allergy and a family portal account, each saved as the person who'd do it.</summary>
    public async Task RegisterAsync(DateTimeOffset at, CancellationToken cancellationToken)
    {
        var frontDesk = s.Random.Pick(w.FrontDesk);
        s.Act(frontDesk.UserId, at);
        var patient = Register(at, frontDesk.UserId);
        await s.SaveAsync(cancellationToken);

        if (s.Random.Chance(0.22))
        {
            await AddAllergyAsync(patient, at.AddMinutes(25), cancellationToken);
        }

        if (s.Random.Chance(0.07))
        {
            await LinkFamilyAsync(patient, at.AddHours(s.Random.Next(1, 30)), cancellationToken);
        }
    }

    private PatientInfo Register(DateTimeOffset at, Guid by)
    {
        // The initial pool is saved in chunks, so each is stamped at its own time.
        s.Act(by, at);
        var sex = s.Random.Chance(0.52) ? Sex.Female : Sex.Male;
        var first = s.Random.Pick(sex == Sex.Female ? DemoCatalog.FemaleNames : DemoCatalog.MaleNames);
        var last = s.Random.Pick(DemoCatalog.Surnames);
        var middle = s.Random.Chance(0.6) ? s.Random.Pick(DemoCatalog.Surnames) : null;
        var age = AgeInYears();
        var birth = AddYears(s.LocalDay(at), -age).AddDays(-s.Random.Next(0, 364));
        var details = new PatientDetails(
            first, middle, last, birth, sex,
            age >= 18 ? s.Random.Pick<CivilStatus?>([CivilStatus.Single, CivilStatus.Married, CivilStatus.Married, CivilStatus.Widowed, CivilStatus.Separated]) : null,
            s.Random.Pick(DemoCatalog.Religions),
            Address(),
            Phone(),
            $"{s.Random.Pick(DemoCatalog.FemaleNames)} {last}",
            age < 18 ? "Mother" : s.Random.Pick(["Spouse", "Daughter", "Son", "Sister", "Brother"]),
            Phone());

        var mrn = NextMrn();
        var patient = Patient.Register(mrn, details);
        s.Db.Add(patient);
        s.Audit.Event(AuditEvents.Patients.PatientRegistered, new AuditOptions
        {
            Target = new AuditTarget(nameof(Patient), patient.Id),
            PatientId = patient.Id,
            Details = new { mrn },
        });

        var info = new PatientInfo(patient.Id, mrn, $"{first} {last}", sex, birth, at)
        {
            HeightCm = age < 14 ? 80 + age * 5 + s.Random.Next(0, 10) : (sex == Sex.Male ? 163 : 152) + s.Random.Next(0, 18),
            WeightKg = age < 14 ? 10 + age * 3 + s.Random.Next(0, 6) : (sex == Sex.Male ? 62 : 52) + s.Random.Next(0, 30),
            Systolic = (short)(105 + Math.Min(age, 70) / 3 + s.Random.Next(0, 20)),
        };
        w.Patients.Add(info);
        return info;
    }

    private async Task AddAllergyAsync(PatientInfo patient, DateTimeOffset at, CancellationToken cancellationToken)
    {
        var nurse = s.Random.Pick(w.Wards).HeadNurse;
        s.Act(nurse.UserId, at);
        var count = s.Random.Chance(0.25) ? 2 : 1;
        for (var i = 0; i < count; i++)
        {
            var (allergen, category, reaction) = s.Random.Pick(DemoCatalog.Allergies);
            if (patient.Allergens.Contains(allergen))
            {
                continue;
            }

            var severity = s.Random.Pick((AllergySeverity.Mild, 0.4), (AllergySeverity.Moderate, 0.4), (AllergySeverity.Severe, 0.2));
            var allergy = new PatientAllergy(patient.Id, new AllergyDetails(allergen, category, severity, reaction));
            s.Db.Add(allergy);
            patient.Allergens.Add(allergen);
            s.Audit.Event(AuditEvents.Patients.AllergyRecorded, new AuditOptions
            {
                Target = new AuditTarget(nameof(PatientAllergy), allergy.Id),
                PatientId = patient.Id,
                Details = new { category, severity },
            });
        }

        await s.SaveAsync(cancellationToken);
    }

    /// <summary>A relative signs up, asks to be linked, and front desk verifies it with the permissions they judge right.</summary>
    private async Task<(User User, PatientAccountLink Link)> LinkFamilyAsync(PatientInfo patient, DateTimeOffset at, CancellationToken cancellationToken)
    {
        var (user, link) = await RequestLinkAsync(patient, at, "portal", null, w.UnusablePasswordHash, cancellationToken);
        var frontDesk = s.Random.Pick(w.FrontDesk);
        var reviewedAt = Min(link.RequestedAt.AddHours(s.Random.Next(2, 30)), s.Now);
        s.Act(frontDesk.UserId, reviewedAt);
        var canBook = s.Random.Chance(0.8);
        var canView = s.Random.Chance(0.7);
        Ensure.Ok(link.Verify(patient.Id, link.Relationship, canBook, canView, "Verified against the patient's ID.", frontDesk.UserId, reviewedAt));
        s.Audit.Event(AuditEvents.Patients.LinkVerified, new AuditOptions
        {
            Target = new AuditTarget(nameof(PatientAccountLink), link.Id),
            PatientId = patient.Id,
            Details = new { canBook, canViewRecords = canView },
        });
        await s.SaveAsync(cancellationToken);
        patient.PortalUserId = user.Id;
        patient.PortalCanBook = canBook;
        return (user, link);
    }

    private async Task<(User User, PatientAccountLink Link)> RequestLinkAsync(
        PatientInfo patient,
        DateTimeOffset at,
        string emailPrefix,
        string? fixedEmail,
        string passwordHash,
        CancellationToken cancellationToken,
        string? displayName = null,
        LinkRelationship? relationship = null)
    {
        var surname = patient.Name.Split(' ')[^1];
        var first = s.Random.Pick(DemoCatalog.FemaleNames.Concat(DemoCatalog.MaleNames).ToArray());
        var name = displayName ?? $"{first} {surname}";
        var email = fixedEmail ?? UniqueEmail($"{first}.{surname}", emailPrefix);
        at = Min(at, s.Now.AddMinutes(-5));

        s.Act(null, at);
        var user = new User(email, name, AccountType.Portal);
        user.ChangePassword(passwordHash);
        // Demo families confirmed their email when they signed up.
        user.MarkEmailVerified(at);
        s.Db.Add(user);
        s.Db.Add(new UserRole(user.Id, w.RoleIds["Patient Portal User"], null, at));
        s.Audit.Event(AuditEvents.Auth.AccountRegistered, new AuditOptions { Target = new AuditTarget(nameof(User), user.Id), ActorId = user.Id });
        await s.SaveAsync(cancellationToken);

        var requestedAt = Min(at.AddMinutes(10), s.Now);
        s.Act(user.Id, requestedAt);
        var kind = relationship ?? (AgeOf(patient) < 18 ? LinkRelationship.Parent : s.Random.Pick<LinkRelationship>([LinkRelationship.Child, LinkRelationship.Spouse, LinkRelationship.Guardian]));
        var link = new PatientAccountLink(
            user.Id,
            new LinkRequestDetails(patient.Name, patient.BirthDate, patient.Mrn, kind, "I help look after them and would like to book visits."),
            requestedAt);
        s.Db.Add(link);
        s.Keep(link);
        s.Audit.Event(AuditEvents.Patients.LinkRequested, new AuditOptions { Target = new AuditTarget(nameof(PatientAccountLink), link.Id), ActorId = user.Id });
        await s.SaveAsync(cancellationToken);
        return (user, link);
    }

    /// <summary>
    /// The family the demo's portal login follows: an older patient admitted to Ward 3 and his daughter, linked with
    /// permission to book and to read his records. Returns the patient.
    /// </summary>
    public async Task<PatientInfo> CreateFeaturedFamilyAsync(DateTimeOffset at, CancellationToken cancellationToken)
    {
        var frontDesk = w.FrontDesk[0];
        s.Act(frontDesk.UserId, at);
        var birth = s.LocalDay(at).AddYears(-71).AddDays(-40);
        var details = new PatientDetails(
            "Antonio", "Reyes", "Mercado", birth, Sex.Male, CivilStatus.Married, "Roman Catholic",
            "14 Narra St., Brgy. Maligaya, Hilom City", "0900-000-0001", "Liza Mercado", "Daughter", "0900-000-0002");
        var mrn = NextMrn();
        var patient = Patient.Register(mrn, details);
        s.Db.Add(patient);
        s.Audit.Event(AuditEvents.Patients.PatientRegistered, new AuditOptions
        {
            Target = new AuditTarget(nameof(Patient), patient.Id),
            PatientId = patient.Id,
            Details = new { mrn },
        });
        var info = new PatientInfo(patient.Id, mrn, "Antonio Mercado", Sex.Male, birth, at) { HeightCm = 168, WeightKg = 71, Systolic = 142 };
        w.Patients.Add(info);
        await s.SaveAsync(cancellationToken);
        await AddAllergyAsync(info, at.AddMinutes(30), cancellationToken);

        var (user, link) = await RequestLinkAsync(
            info, at.AddDays(1), "portal", "portal@demo.hilom.test", w.DemoPasswordHash, cancellationToken, "Liza Mercado", LinkRelationship.Child);
        var reviewedAt = at.AddDays(1).AddHours(3);
        s.Act(frontDesk.UserId, reviewedAt);
        Ensure.Ok(link.Verify(info.Id, LinkRelationship.Child, true, true, "Verified against the patient's ID.", frontDesk.UserId, reviewedAt));
        s.Audit.Event(AuditEvents.Patients.LinkVerified, new AuditOptions
        {
            Target = new AuditTarget(nameof(PatientAccountLink), link.Id),
            PatientId = info.Id,
            Details = new { canBook = true, canViewRecords = true },
        });
        await s.SaveAsync(cancellationToken);
        info.PortalUserId = user.Id;
        info.PortalCanBook = true;
        w.FeaturedPatient = info;
        return info;
    }

    /// <summary>Requests waiting for front desk, one that was turned down and one that was later revoked.</summary>
    public async Task CreateLinkQueueAsync(CancellationToken cancellationToken)
    {
        var candidates = w.Patients.Where(p => p.PortalUserId is null && p.RegisteredAt < s.Now.AddDays(-5) && p != w.FeaturedPatient).Take(40).ToList();
        if (candidates.Count < 5)
        {
            return;
        }

        for (var i = 0; i < 3; i++)
        {
            var patient = candidates[i * 7 % candidates.Count];
            await RequestLinkAsync(patient, s.Now.AddHours(-s.Random.Next(3, 60)), "portal", null, w.UnusablePasswordHash, cancellationToken);
        }

        var rejected = candidates[^1];
        var (_, rejectedLink) = await RequestLinkAsync(rejected, s.Now.AddDays(-6), "portal", null, w.UnusablePasswordHash, cancellationToken);
        var frontDesk = w.FrontDesk[0];
        s.Act(frontDesk.UserId, s.Now.AddDays(-5));
        Ensure.Ok(rejectedLink.Reject("Name and date of birth don't match the record.", frontDesk.UserId, s.Clock));
        s.Audit.Event(AuditEvents.Patients.LinkRejected, new AuditOptions { Target = new AuditTarget(nameof(PatientAccountLink), rejectedLink.Id), PatientId = rejected.Id });
        await s.SaveAsync(cancellationToken);

        var revoked = candidates[^2];
        var (_, revokedLink) = await RequestLinkAsync(revoked, s.Now.AddDays(-40), "portal", null, w.UnusablePasswordHash, cancellationToken);
        s.Act(frontDesk.UserId, s.Now.AddDays(-39));
        Ensure.Ok(revokedLink.Verify(revoked.Id, revokedLink.Relationship, true, false, null, frontDesk.UserId, s.Clock));
        await s.SaveAsync(cancellationToken);
        s.Act(frontDesk.UserId, s.Now.AddDays(-12));
        Ensure.Ok(revokedLink.Revoke("Relative asked for the link to be removed.", frontDesk.UserId, s.Clock));
        s.Audit.Event(AuditEvents.Patients.LinkRevoked, new AuditOptions { Target = new AuditTarget(nameof(PatientAccountLink), revokedLink.Id), PatientId = revoked.Id });
        await s.SaveAsync(cancellationToken);
    }

    private string NextMrn()
    {
        // The registry's own sequence, so patients registered later through the app continue the numbering.
        var next = s.Db.Database.SqlQueryRaw<long>("SELECT nextval('patients.patient_mrn_seq') AS \"Value\"").AsEnumerable().Single();
        return string.Create(CultureInfo.InvariantCulture, $"MRN-{next:D6}");
    }

    private int AgeInYears() => s.Random.Pick(
        (s.Random.Next(0, 13), 0.15),
        (s.Random.Next(13, 40), 0.25),
        (s.Random.Next(40, 65), 0.35),
        (s.Random.Next(65, 94), 0.25));

    private int AgeOf(PatientInfo patient) => Math.Max(0, s.Today.Year - patient.BirthDate.Year);

    private string Address() =>
        string.Create(CultureInfo.InvariantCulture, $"{s.Random.Next(1, 200)} {s.Random.Pick(DemoCatalog.Streets)} St., Brgy. {s.Random.Pick(DemoCatalog.Barangays)}, Hilom City");

    // Fictional: the 0900-000 block isn't assigned to anyone.
    private string Phone() => string.Create(CultureInfo.InvariantCulture, $"0900-000-{(++_phoneCounter + 100) % 10000:D4}");

    private string UniqueEmail(string stem, string prefix)
    {
        var clean = $"{prefix}.{stem}".ToLowerInvariant().Replace(" ", "", StringComparison.Ordinal);
        var email = $"{clean}@family.hilom.test";
        for (var n = 2; !_emails.Add(email); n++)
        {
            email = string.Create(CultureInfo.InvariantCulture, $"{clean}{n}@family.hilom.test");
        }

        return email;
    }

    private static DateOnly AddYears(DateOnly day, int years) => day.AddYears(years);

    private static DateTimeOffset Min(DateTimeOffset a, DateTimeOffset b) => a < b ? a : b;
}
