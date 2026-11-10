using Hilom.Facility.Domain;
using Hilom.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hilom.Facility.Persistence;

internal sealed class WardConfiguration : IEntityTypeConfiguration<Ward>
{
    // The foreign key to workforce.departments is configured by Workforce (WardDepartmentConfiguration),
    // since Facility can't reference Workforce types.
    public void Configure(EntityTypeBuilder<Ward> builder)
    {
        builder.ToTable("wards", table => table.HasEnumCheck<Ward, WardType>("ward_type"));
        builder.Property(w => w.Code).HasColumnType("citext").HasMaxLength(Ward.CodeMaxLength);
        builder.HasIndex(w => w.Code).IsUnique();
        builder.Property(w => w.Name).HasMaxLength(Ward.NameMaxLength);
        builder.Property(w => w.WardType).HasEnumText();
    }
}

internal sealed class BedConfiguration : IEntityTypeConfiguration<Bed>
{
    public void Configure(EntityTypeBuilder<Bed> builder)
    {
        builder.ToTable("beds");
        builder.HasOne<Ward>().WithMany().HasForeignKey(b => b.WardId).OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_beds_wards_ward_id");
        builder.Property(b => b.Label).HasColumnType("citext").HasMaxLength(Bed.LabelMaxLength);
        builder.HasIndex(b => new { b.WardId, b.Label }).IsUnique();
    }
}
