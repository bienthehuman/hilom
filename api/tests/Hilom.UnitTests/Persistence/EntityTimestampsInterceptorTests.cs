using Hilom.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Hilom.UnitTests.Persistence;

public sealed class EntityTimestampsInterceptorTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 9, 6, 30, 0, TimeSpan.Zero);
    private static readonly Guid UserId = Guid.CreateVersion7();

    [Fact]
    public void New_records_get_created_and_updated_stamps()
    {
        using var db = SampleDb.Create();
        var record = new SampleRecord();
        db.Add(record);

        EntityTimestampsInterceptor.Stamp(db.ChangeTracker, Now, UserId);

        record.CreatedAt.ShouldBe(Now);
        record.CreatedBy.ShouldBe(UserId);
        record.UpdatedAt.ShouldBe(Now);
        record.UpdatedBy.ShouldBe(UserId);
    }

    [Fact]
    public void Modified_records_only_change_updated_stamps()
    {
        using var db = SampleDb.Create();
        var record = new SampleRecord();
        record.MarkCreated(Now.AddDays(-1), null);
        db.Attach(record);
        db.Entry(record).State = EntityState.Modified;

        EntityTimestampsInterceptor.Stamp(db.ChangeTracker, Now, UserId);

        record.CreatedAt.ShouldBe(Now.AddDays(-1));
        record.CreatedBy.ShouldBeNull();
        record.UpdatedAt.ShouldBe(Now);
        record.UpdatedBy.ShouldBe(UserId);
    }

    [Fact]
    public void Unchanged_records_are_left_alone()
    {
        using var db = SampleDb.Create();
        var record = new SampleRecord();
        db.Attach(record);

        EntityTimestampsInterceptor.Stamp(db.ChangeTracker, Now, UserId);

        record.UpdatedAt.ShouldBe(default);
    }
}
