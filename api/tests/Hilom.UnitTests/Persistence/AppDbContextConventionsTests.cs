using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Hilom.UnitTests.Persistence;

public sealed class AppDbContextConventionsTests
{
    private static IEntityType SampleTable()
    {
        using var db = SampleDb.Create();
        return db.Model.FindEntityType(typeof(SampleRecord)).ShouldNotBeNull();
    }

    [Fact]
    public void Tables_go_into_their_module_schema()
    {
        SampleTable().GetSchema().ShouldBe("sample");
    }

    [Fact]
    public void Table_and_column_names_are_snake_case()
    {
        var table = SampleTable();
        var store = StoreObjectIdentifier.Table(table.GetTableName()!, table.GetSchema());

        table.GetTableName().ShouldBe("sample_record");
        table.FindProperty(nameof(SampleRecord.DisplayName))!.GetColumnName(store).ShouldBe("display_name");
        table.FindProperty(nameof(SampleRecord.CreatedAt))!.GetColumnName(store).ShouldBe("created_at");
    }

    [Fact]
    public void Ids_are_generated_by_the_app_not_the_database()
    {
        var id = SampleTable().FindProperty(nameof(SampleRecord.Id))!;

        id.IsPrimaryKey().ShouldBeTrue();
        id.ValueGenerated.ShouldBe(ValueGenerated.Never);
    }

    [Fact]
    public void Version_is_a_concurrency_token_backed_by_xmin()
    {
        var table = SampleTable();
        var store = StoreObjectIdentifier.Table(table.GetTableName()!, table.GetSchema());
        var version = table.FindProperty(nameof(SampleRecord.Version))!;

        version.IsConcurrencyToken.ShouldBeTrue();
        version.GetColumnName(store).ShouldBe("xmin");
        version.GetColumnType().ShouldBe("xid");
    }

    [Fact]
    public void Timestamps_are_stored_as_timestamptz()
    {
        SampleTable().FindProperty(nameof(SampleRecord.CreatedAt))!.GetColumnType().ShouldBe("timestamp with time zone");
    }

    [Fact]
    public void Required_postgres_extensions_are_declared()
    {
        using var db = SampleDb.Create();

        // Extension declarations live on the design-time model (used by migrations), not the runtime one.
        var extensions = db.GetService<IDesignTimeModel>().Model.GetAnnotations().Select(a => a.Name).ToList();

        extensions.ShouldContain(name => name.EndsWith(":citext", StringComparison.Ordinal));
        extensions.ShouldContain(name => name.EndsWith(":btree_gist", StringComparison.Ordinal));
    }
}
