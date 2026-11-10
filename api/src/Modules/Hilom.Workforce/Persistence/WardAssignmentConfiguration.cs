using Hilom.Access.Domain;
using Hilom.Facility.Domain;
using Hilom.Workforce.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hilom.Workforce.Persistence;

internal sealed class WardAssignmentConfiguration : IEntityTypeConfiguration<WardAssignment>
{
    /// <summary>
    /// One ward at a time per employee. EF has no fluent API for exclusion constraints, so the migration
    /// creates it with this name; the API turns a violation into a friendly 409.
    /// </summary>
    public const string OneWardAtATime = "ex_ward_assignments_one_ward_at_a_time";

    public void Configure(EntityTypeBuilder<WardAssignment> builder)
    {
        builder.ToTable("ward_assignments", table =>
        {
            table.HasCheckConstraint("ck_ward_assignments_period_bounded_start", "NOT isempty(period) AND NOT lower_inf(period)");
        });

        builder.Property(a => a.Period).HasColumnType("daterange");
        builder.Property(a => a.Reason).HasMaxLength(WardAssignment.ReasonMaxLength);
        builder.Ignore(a => a.StartsOn);
        builder.Ignore(a => a.LastDay);

        builder.HasOne<Employee>().WithMany().HasForeignKey(a => a.EmployeeId).OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_ward_assignments_employees_employee_id");
        builder.HasOne<Ward>().WithMany().HasForeignKey(a => a.WardId).OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_ward_assignments_wards_ward_id");
        builder.HasOne<User>().WithMany().HasForeignKey(a => a.AssignedBy).OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_ward_assignments_users_assigned_by");
    }
}
