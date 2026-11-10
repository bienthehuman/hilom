using Hilom.Workforce.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hilom.Workforce.Persistence;

/// <summary>Shared table shape for every lookup: a unique, case-insensitive name and an active flag.</summary>
internal abstract class LookupConfiguration<T>(string table) : IEntityTypeConfiguration<T>
    where T : Lookup
{
    public void Configure(EntityTypeBuilder<T> builder)
    {
        builder.ToTable(table);
        builder.Property(l => l.Name).HasColumnType("citext").HasMaxLength(Lookup.NameMaxLength);
        builder.HasIndex(l => l.Name).IsUnique();
    }
}

internal sealed class DepartmentConfiguration() : LookupConfiguration<Department>("departments");

internal sealed class PositionConfiguration() : LookupConfiguration<Position>("positions");

internal sealed class SpecializationConfiguration() : LookupConfiguration<Specialization>("specializations");
