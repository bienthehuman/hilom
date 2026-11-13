using System.Diagnostics;
using System.Net;
using System.Security.Claims;
using Hilom.Infrastructure;
using Hilom.Infrastructure.Auditing;
using Hilom.Infrastructure.Modules;
using Hilom.Infrastructure.Persistence;
using Hilom.SharedKernel.Auditing;
using Hilom.SharedKernel.Domain;
using Hilom.SharedKernel.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Hilom.DemoData;

/// <summary>
/// One generation run: its own database context, whose interceptors stamp rows and write audit entries with the
/// run's clock and the acting user, so the history reads as if each person did each thing at its time.
/// Everything is saved step by step as that person, the way the endpoints would.
/// </summary>
internal sealed class SeedSession : IDisposable
{
    private readonly SimClock _clock;
    private readonly SimUser _user = new();
    private readonly SimRequests _requests = new();
    private readonly IDisposable _ids;
    private readonly HashSet<object> _keep = new(ReferenceEqualityComparer.Instance);

    public SeedSession(string connectionString, ModuleRegistry modules, DemoRandom random, TimeZoneInfo zone, DateTimeOffset now)
    {
        Random = random;
        Zone = zone;
        Now = now;
        _clock = new SimClock(now);
        Activity.Current = null; // correlation ids come from the run, not from whatever trace started it
        _ids = EntityIds.Use(() => Random.NewId(_clock.GetUtcNow()));

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable(DependencyInjection.MigrationsHistoryTable))
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(new EntityTimestampsInterceptor(_clock, _user), new AuditChangesInterceptor(_requests, _clock))
            .Options;
        Db = new AppDbContext(options, modules);
        Audit = new HistoricalAuditWriter(Db, _requests, _clock);
    }

    public AppDbContext Db { get; }

    /// <summary>
    /// Holds one connection for the whole run and lets commits return before they reach disk. The run saves tens of thousands
    /// of times, and if the server dies mid-run the demo is simply generated again. Nothing else uses this connection.
    /// </summary>
    public async Task OpenAsync(CancellationToken cancellationToken)
    {
        await Db.Database.OpenConnectionAsync(cancellationToken);
        await Db.Database.ExecuteSqlAsync($"SET synchronous_commit = off", cancellationToken);
    }

    public DemoRandom Random { get; }

    public HistoricalAuditWriter Audit { get; }

    public TimeZoneInfo Zone { get; }

    /// <summary>How many times the run saved, and how long the database took, for the log.</summary>
    public int Saves { get; private set; }

    public TimeSpan SaveTime { get; private set; }

    /// <summary>The end of the generated history. Nothing is recorded as having happened after it.</summary>
    public DateTimeOffset Now { get; }

    public DateOnly Today => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(Now, Zone).DateTime);

    /// <summary>The run's clock: what "now" is for ids, timestamps and audit entries.</summary>
    public DateTimeOffset Clock => _clock.GetUtcNow();

    /// <summary>The next save happens at <paramref name="at"/>, done by <paramref name="userId"/>.</summary>
    public SeedSession Act(Guid? userId, DateTimeOffset at)
    {
        _clock.Set(at > Now ? Now : at);
        _user.UserId = userId;
        _requests.Set(userId);
        return this;
    }

    /// <summary>Hospital local date and time to an instant.</summary>
    public DateTimeOffset Local(DateOnly day, TimeOnly time) =>
        new(TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(day.ToDateTime(time), DateTimeKind.Unspecified), Zone), TimeSpan.Zero);

    public DateOnly LocalDay(DateTimeOffset instant) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, Zone).DateTime);

    /// <summary>Keeps tracking this entity through saves because a later step will change it.</summary>
    public void Keep(object entity) => _keep.Add(entity);

    public void Release(object entity) => _keep.Remove(entity);

    /// <summary>Saves, then stops tracking everything a later step won't change, so memory stays flat over months of history.</summary>
    public async Task SaveAsync(CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        await Db.SaveChangesAsync(cancellationToken);
        SaveTime += Stopwatch.GetElapsedTime(started);
        Saves++;
        foreach (var entry in Db.ChangeTracker.Entries().ToList())
        {
            if (!_keep.Contains(entry.Entity))
            {
                entry.State = EntityState.Detached;
            }
        }
    }

    public void Dispose()
    {
        _ids.Dispose();
        Db.Dispose();
    }

    private sealed class SimClock(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;

        public void Set(DateTimeOffset value) => _now = value;

        public override DateTimeOffset GetUtcNow() => _now;
    }

    private sealed class SimUser : ICurrentUser
    {
        public Guid? UserId { get; set; }

        public bool IsAuthenticated => UserId is not null;
    }

    /// <summary>Gives the audit interceptor a request to read the actor, address and correlation id from.</summary>
    private sealed class SimRequests : IHttpContextAccessor
    {
        private long _counter;

        public HttpContext? HttpContext { get; set; }

        public void Set(Guid? userId)
        {
            if (userId is null)
            {
                HttpContext = null;
                return;
            }

            var context = new DefaultHttpContext { TraceIdentifier = $"demo-{++_counter:D8}" };
            context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId.Value.ToString())], "demo"));
            var bytes = userId.Value.ToByteArray();
            context.Connection.RemoteIpAddress = new IPAddress([10, 20, bytes[0], (byte)Math.Max(1, (int)bytes[1])]);
            context.Request.Headers.UserAgent = "Mozilla/5.0 (X11; Linux x86_64) Hilom-Demo";
            HttpContext = context;
        }
    }
}
