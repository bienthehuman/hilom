using Hilom.Access.Domain;
using Hilom.Infrastructure.Persistence;
using Hilom.Patients.Domain;
using Hilom.Scheduling.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hilom.Scheduling.Persistence;

internal sealed class AppointmentConfiguration : IEntityTypeConfiguration<Appointment>
{
    public const string OneLivePerSlot = "ux_appointments_one_live_per_slot";

    public void Configure(EntityTypeBuilder<Appointment> builder)
    {
        builder.ToTable("appointments", table => table.HasEnumCheck<Appointment, AppointmentStatus>("status"));

        builder.HasOne<AvailabilitySlot>().WithMany().HasForeignKey(a => a.SlotId).OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_appointments_availability_slots_slot_id");
        builder.HasOne<Patient>().WithMany().HasForeignKey(a => a.PatientId).OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_appointments_patients_patient_id");
        builder.HasOne<User>().WithMany().HasForeignKey(a => a.BookedBy).OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_appointments_users_booked_by");

        builder.Property(a => a.Reason).HasMaxLength(Appointment.ReasonMaxLength);
        builder.Property(a => a.Status).HasEnumText();

        // One live appointment per slot: a request holds it until declined or cancelled.
        builder.HasIndex(a => a.SlotId, "OneLivePerSlot")
            .IsUnique()
            .HasFilter("status IN ('requested', 'confirmed', 'checked_in', 'completed')")
            .HasDatabaseName(OneLivePerSlot);
        builder.HasIndex(a => a.SlotId, "BySlot").HasDatabaseName("ix_appointments_slot_id");
        builder.HasIndex(a => a.PatientId);
    }
}
