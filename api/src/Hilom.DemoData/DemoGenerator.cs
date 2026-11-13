using System.Security.Cryptography;
using Hilom.Access.Domain;
using Hilom.Clinical.Domain;
using Hilom.Infrastructure.Auditing;
using Hilom.Infrastructure.Configuration;
using Hilom.Infrastructure.Modules;
using Hilom.Infrastructure.Persistence;
using Hilom.Patients.Domain;
using Hilom.Scheduling.Domain;
using Hilom.Workforce.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Hilom.DemoData;

/// <summary>
/// Generates the fictional hospital: staff, patients and months of clinical history, all through the domain types.
/// The result depends only on the seed, the scale and the moment the history ends, so the same three give the same
/// ids, names, numbers and times.
/// </summary>
internal sealed partial class DemoGenerator(
    IServiceProvider services,
    IOptions<DemoOptions> options,
    HospitalClock hospital,
    ILogger<DemoGenerator> logger)
{
    public async Task<DemoSummary> RunAsync(CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var size = DemoSizes.For(settings.Scale);
        var now = settings.Now ?? TruncateToHour(DateTimeOffset.UtcNow);

        string connectionString;
        await using (var scope = services.CreateAsyncScope())
        {
            connectionString = scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.GetConnectionString()
                ?? throw new InvalidOperationException("The database connection string isn't available.");
        }

        var random = new DemoRandom(settings.Seed);
        using var session = new SeedSession(connectionString, services.GetRequiredService<ModuleRegistry>(), random, hospital.TimeZone, now);
        await session.OpenAsync(cancellationToken);
        var world = new DemoWorld();
        var hasher = services.GetRequiredService<IPasswordHasher<User>>();
        var hashing = new User("hash@demo.hilom.test", "Hash");
        world.DemoPasswordHash = hasher.HashPassword(hashing, settings.Password);
        world.UnusablePasswordHash = hasher.HashPassword(hashing, Convert.ToBase64String(RandomNumberGenerator.GetBytes(24)));

        var records = new ClinicalRecords(session);
        var patients = new PatientGenerator(session, world, size);
        var wards = new WardGenerator(session, world, size, records);
        var clinic = new ClinicGenerator(session, world, size, records, wards);
        var access = new AccessHistory(session, world);

        var today = session.Today;
        var historyStart = today.AddDays(-size.HistoryDays);
        LogStarting(size.Patients, size.HistoryDays, settings.Seed);

        await new StaffGenerator(session, world, size).RunAsync(historyStart, cancellationToken);
        var setupAt = session.Local(historyStart.AddDays(-30), new TimeOnly(8, 0)).AddDays(2);
        await patients.RegisterInitialAsync(setupAt, cancellationToken);
        await patients.CreateFeaturedFamilyAsync(setupAt.AddHours(3), cancellationToken);
        await clinic.PublishSlotsAsync(historyStart, cancellationToken);
        clinic.PlanScripts(size.HistoryDays);

        var perDay = patients.PerDay(size.HistoryDays);
        var total = today.AddDays(DemoSize.FutureDays).DayNumber - historyStart.DayNumber;
        for (var day = historyStart; day <= today.AddDays(DemoSize.FutureDays); day = day.AddDays(1))
        {
            if (day <= today)
            {
                var registrations = (int)perDay + (random.Chance(perDay - Math.Floor(perDay)) ? 1 : 0);
                for (var i = 0; i < registrations; i++)
                {
                    var at = session.Local(day, new TimeOnly(random.Next(8, 17), random.Next(0, 60)));
                    if (at <= now)
                    {
                        await patients.RegisterAsync(at, cancellationToken);
                    }
                }
            }

            await clinic.DayAsync(day, cancellationToken);
            if (day <= today)
            {
                await wards.DayAsync(day, cancellationToken);
                await access.DayAsync(day, cancellationToken);
            }

            if ((day.DayNumber - historyStart.DayNumber) % 10 == 0)
            {
                LogProgress(day.DayNumber - historyStart.DayNumber, total);
            }
        }

        wards.ReleaseReservedBed();
        await patients.CreateLinkQueueAsync(cancellationToken);
        await access.RecentSecurityEventsAsync(cancellationToken);

        var summary = await DemoSummary.ReadAsync(session.Db, cancellationToken);
        LogDone(summary.Patients, summary.Encounters, summary.AuditEntries, session.Saves, (int)session.SaveTime.TotalSeconds);
        return summary;
    }

    private static DateTimeOffset TruncateToHour(DateTimeOffset value) =>
        new(value.Year, value.Month, value.Day, value.Hour, 0, 0, TimeSpan.Zero);

    [LoggerMessage(Level = LogLevel.Information, Message = "Generating demo data: about {Patients} patients over {Days} days (seed {Seed})")]
    private partial void LogStarting(int patients, int days, int seed);

    [LoggerMessage(Level = LogLevel.Information, Message = "Demo data: day {Day} of {Total}")]
    private partial void LogProgress(int day, int total);

    [LoggerMessage(Level = LogLevel.Information, Message = "Demo data ready: {Patients} patients, {Encounters} encounters, {Audit} audit entries ({Saves} saves, {Seconds}s of them in the database)")]
    private partial void LogDone(long patients, long encounters, long audit, int saves, int seconds);
}

/// <summary>How much was generated.</summary>
public sealed record DemoSummary(
    long Patients,
    long Employees,
    long Appointments,
    long Encounters,
    long Admissions,
    long VitalSigns,
    long Notes,
    long Orders,
    long MedicationAdministrations,
    long AuditEntries)
{
    internal static async Task<DemoSummary> ReadAsync(AppDbContext db, CancellationToken cancellationToken) => new(
        await db.Set<Patient>().LongCountAsync(cancellationToken),
        await db.Set<Employee>().LongCountAsync(cancellationToken),
        await db.Set<Appointment>().LongCountAsync(cancellationToken),
        await db.Set<Encounter>().LongCountAsync(cancellationToken),
        await db.Set<Admission>().LongCountAsync(cancellationToken),
        await db.Set<VitalSigns>().LongCountAsync(cancellationToken),
        await db.Set<ClinicalNote>().LongCountAsync(cancellationToken),
        await db.Set<PhysicianOrder>().LongCountAsync(cancellationToken),
        await db.Set<MedicationAdministration>().LongCountAsync(cancellationToken),
        await db.Set<AuditEntry>().LongCountAsync(cancellationToken));
}
