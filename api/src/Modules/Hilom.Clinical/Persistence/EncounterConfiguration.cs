using Hilom.Clinical.Domain;
using Hilom.Infrastructure.Persistence;
using Hilom.Patients.Domain;
using Hilom.Scheduling.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hilom.Clinical.Persistence;

internal sealed class EncounterConfiguration : IEntityTypeConfiguration<Encounter>
{
    public const string OnePerAppointment = "ux_encounters_appointment_id";

    /// <summary>attending_physician_id → workforce.physician_profiles(employee_id), added in SQL (the profile is an owned type).</summary>
    public const string PhysicianProfileForeignKey = "fk_encounters_physician_profiles_attending_physician_id";

    public void Configure(EntityTypeBuilder<Encounter> builder)
    {
        builder.ToTable("encounters", table =>
        {
            table.HasEnumCheck<Encounter, EncounterType>("encounter_type");
            table.HasEnumCheck<Encounter, EncounterStatus>("status");

            // Open encounters have no end; closed and voided ones do, and it's never before the start.
            table.HasCheckConstraint("ck_encounters_ended_at", "(status = 'open') = (ended_at IS NULL) AND (ended_at IS NULL OR ended_at >= started_at)");

            // Only clinic visits come from an appointment; only ward stays come from another encounter.
            table.HasCheckConstraint("ck_encounters_appointment_outpatient", "appointment_id IS NULL OR encounter_type = 'outpatient'");
            table.HasCheckConstraint("ck_encounters_parent_inpatient", "parent_encounter_id IS NULL OR encounter_type = 'inpatient'");
        });

        builder.Property(e => e.Type).HasColumnName("encounter_type").HasEnumText();
        builder.Property(e => e.Status).HasEnumText();
        builder.Property(e => e.ChiefComplaint).HasMaxLength(Encounter.ChiefComplaintMaxLength);
        builder.Property(e => e.DiagnosisSummary).HasMaxLength(Encounter.DiagnosisSummaryMaxLength);

        builder.HasOne<Patient>().WithMany().HasForeignKey(e => e.PatientId).OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_encounters_patients_patient_id");
        builder.HasOne<Appointment>().WithMany().HasForeignKey(e => e.AppointmentId).OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_encounters_appointments_appointment_id");
        builder.HasOne<Encounter>().WithMany().HasForeignKey(e => e.ParentEncounterId).OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_encounters_encounters_parent_encounter_id");

        // One encounter per appointment: checking in twice can't open two visits.
        builder.HasIndex(e => e.AppointmentId).IsUnique().HasDatabaseName(OnePerAppointment);
        builder.HasIndex(e => new { e.PatientId, e.StartedAt });
        builder.HasIndex(e => new { e.AttendingPhysicianId, e.Status });
        builder.HasIndex(e => e.ParentEncounterId);

        // The encounters list, newest first.
        builder.HasIndex(e => new { e.StartedAt, e.Id });
    }
}
