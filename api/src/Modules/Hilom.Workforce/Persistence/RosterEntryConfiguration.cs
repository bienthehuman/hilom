using Hilom.Facility.Domain;
using Hilom.Infrastructure.Persistence;
using Hilom.Workforce.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hilom.Workforce.Persistence;

internal sealed class RosterEntryConfiguration : IEntityTypeConfiguration<RosterEntry>
{
    /// <summary>One scheduled shift per person per day. The API turns a violation into a friendly 409.</summary>
    public const string OneShiftPerDay = "ux_roster_entries_one_shift_per_day";

    public void Configure(EntityTypeBuilder<RosterEntry> builder)
    {
        builder.ToTable("roster_entries", table => table.HasEnumCheck<RosterEntry, RosterStatus>("status"));
        builder.Property(r => r.Status).HasEnumText();

        builder.HasIndex(r => new { r.EmployeeId, r.DutyDate }, OneShiftPerDay)
            .IsUnique()
            .HasFilter("status = 'scheduled'")
            .HasDatabaseName(OneShiftPerDay);
        builder.HasIndex(r => new { r.WardId, r.DutyDate }, "ix_roster_entries_ward_id_duty_date")
            .HasDatabaseName("ix_roster_entries_ward_id_duty_date");

        builder.HasOne<Employee>().WithMany().HasForeignKey(r => r.EmployeeId).OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_roster_entries_employees_employee_id");
        builder.HasOne<Ward>().WithMany().HasForeignKey(r => r.WardId).OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_roster_entries_wards_ward_id");
        builder.HasOne<Shift>().WithMany().HasForeignKey(r => r.ShiftId).OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_roster_entries_shifts_shift_id");
    }
}
