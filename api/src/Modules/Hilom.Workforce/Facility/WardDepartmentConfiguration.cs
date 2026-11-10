using Hilom.Facility.Domain;
using Hilom.Workforce.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hilom.Workforce.Facility;

/// <summary>
/// The database foreign key from facility.wards to workforce.departments. It's configured here because
/// Workforce may reference Facility, not the other way round (docs/ARCHITECTURE.md §3).
/// </summary>
internal sealed class WardDepartmentConfiguration : IEntityTypeConfiguration<Ward>
{
    public void Configure(EntityTypeBuilder<Ward> builder) =>
        builder.HasOne<Department>().WithMany().HasForeignKey(w => w.DepartmentId).OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_wards_departments_department_id");
}
