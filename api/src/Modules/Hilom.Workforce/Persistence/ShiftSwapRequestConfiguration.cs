using Hilom.Access.Domain;
using Hilom.Facility.Domain;
using Hilom.Infrastructure.Persistence;
using Hilom.Workforce.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hilom.Workforce.Persistence;

internal sealed class ShiftSwapRequestConfiguration : IEntityTypeConfiguration<ShiftSwapRequest>
{
    private const string Open = "status IN ('pending_target', 'pending_approval')";

    public void Configure(EntityTypeBuilder<ShiftSwapRequest> builder)
    {
        builder.ToTable("shift_swap_requests", table =>
        {
            table.HasEnumCheck<ShiftSwapRequest, SwapStatus>("status");
            table.HasCheckConstraint("ck_shift_swap_requests_two_people", "requester_employee_id <> target_employee_id");
        });
        builder.Property(s => s.Status).HasEnumText();
        builder.Property(s => s.Message).HasMaxLength(ShiftSwapRequest.NoteMaxLength);
        builder.Property(s => s.TargetResponse).HasMaxLength(ShiftSwapRequest.NoteMaxLength);
        builder.Property(s => s.ReviewNote).HasMaxLength(ShiftSwapRequest.NoteMaxLength);
        builder.Ignore(s => s.IsOpen);

        // A shift can be in at most one open swap on each side (the API also checks across sides).
        builder.HasIndex(s => s.RequesterEntryId, "ux_shift_swap_requests_open_requester_entry")
            .IsUnique().HasFilter(Open).HasDatabaseName("ux_shift_swap_requests_open_requester_entry");
        builder.HasIndex(s => s.TargetEntryId, "ux_shift_swap_requests_open_target_entry")
            .IsUnique().HasFilter(Open).HasDatabaseName("ux_shift_swap_requests_open_target_entry");
        builder.HasIndex(s => new { s.WardId, s.Status }, "ix_shift_swap_requests_ward_id_status")
            .HasDatabaseName("ix_shift_swap_requests_ward_id_status");
        builder.HasIndex(s => s.RequesterEmployeeId, "ix_shift_swap_requests_requester_employee_id")
            .HasDatabaseName("ix_shift_swap_requests_requester_employee_id");
        builder.HasIndex(s => s.TargetEmployeeId, "ix_shift_swap_requests_target_employee_id")
            .HasDatabaseName("ix_shift_swap_requests_target_employee_id");

        // History outlives roster entries: removing an entry clears the link; the snapshot columns remain.
        builder.HasOne<RosterEntry>().WithMany().HasForeignKey(s => s.RequesterEntryId).OnDelete(DeleteBehavior.SetNull)
            .HasConstraintName("fk_shift_swap_requests_roster_entries_requester_entry_id");
        builder.HasOne<RosterEntry>().WithMany().HasForeignKey(s => s.TargetEntryId).OnDelete(DeleteBehavior.SetNull)
            .HasConstraintName("fk_shift_swap_requests_roster_entries_target_entry_id");
        builder.HasOne<Employee>().WithMany().HasForeignKey(s => s.RequesterEmployeeId).OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_shift_swap_requests_employees_requester_employee_id");
        builder.HasOne<Employee>().WithMany().HasForeignKey(s => s.TargetEmployeeId).OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_shift_swap_requests_employees_target_employee_id");
        builder.HasOne<Shift>().WithMany().HasForeignKey(s => s.RequesterShiftId).OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_shift_swap_requests_shifts_requester_shift_id");
        builder.HasOne<Shift>().WithMany().HasForeignKey(s => s.TargetShiftId).OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_shift_swap_requests_shifts_target_shift_id");
        builder.HasOne<Ward>().WithMany().HasForeignKey(s => s.WardId).OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_shift_swap_requests_wards_ward_id");
        builder.HasOne<User>().WithMany().HasForeignKey(s => s.ReviewedBy).OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_shift_swap_requests_users_reviewed_by");
    }
}
