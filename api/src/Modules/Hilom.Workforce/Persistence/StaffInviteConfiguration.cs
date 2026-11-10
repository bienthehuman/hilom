using Hilom.Workforce.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hilom.Workforce.Persistence;

internal sealed class StaffInviteConfiguration : IEntityTypeConfiguration<StaffInvite>
{
    public void Configure(EntityTypeBuilder<StaffInvite> builder)
    {
        builder.ToTable("staff_invites", table => table.HasCheckConstraint(
            "ck_staff_invites_accepted_or_revoked",
            "accepted_at IS NULL OR revoked_at IS NULL"));

        builder.HasOne<Employee>().WithMany().HasForeignKey(i => i.EmployeeId).OnDelete(DeleteBehavior.Restrict);

        // One open invite per employee (docs/DATA_MODEL.md).
        builder.HasIndex(i => i.EmployeeId, "ux_staff_invites_one_open_per_employee")
            .IsUnique()
            .HasFilter("accepted_at IS NULL AND revoked_at IS NULL")
            .HasDatabaseName("ux_staff_invites_one_open_per_employee");
        builder.HasIndex(i => i.EmployeeId, "ix_staff_invites_employee_id").HasDatabaseName("ix_staff_invites_employee_id");

        builder.Property(i => i.Email).HasColumnType("citext").HasMaxLength(Employee.EmailMaxLength);
        builder.Property(i => i.TokenHash).HasMaxLength(32);
        builder.HasIndex(i => i.TokenHash).IsUnique();
        builder.Property(i => i.InitialRoleIds).HasColumnType("uuid[]");
    }
}
