using Hilom.Clinical.Domain;
using Hilom.Infrastructure.Persistence;
using Hilom.Patients.Domain;
using Hilom.Workforce.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hilom.Clinical.Persistence;

internal sealed class MedicationAdministrationConfiguration : IEntityTypeConfiguration<MedicationAdministration>
{
    /// <summary>A dose given says how much; one not given has no dose.</summary>
    public const string DoseCheck = "ck_medication_administrations_dose_given";

    /// <summary>A dose not given (held, refused, not available) says why.</summary>
    public const string ReasonCheck = "ck_medication_administrations_reason";

    public void Configure(EntityTypeBuilder<MedicationAdministration> builder)
    {
        builder.ToTable("medication_administrations", table =>
        {
            table.HasEnumCheck<MedicationAdministration, MedicationOutcome>("outcome");
            table.HasCheckConstraint(
                DoseCheck,
                "(outcome = 'given' AND length(btrim(coalesce(dose_given, ''))) > 0) OR (outcome <> 'given' AND dose_given IS NULL)");
            table.HasCheckConstraint(ReasonCheck, "outcome = 'given' OR length(btrim(coalesce(notes, ''))) > 0");
            table.HasVoidCheck("medication_administrations");
        });

        builder.Property(a => a.Outcome).HasEnumText();
        builder.Property(a => a.DoseGiven).HasMaxLength(MedicationAdministration.DoseMaxLength);
        builder.Property(a => a.Notes).HasMaxLength(MedicationAdministration.NotesMaxLength);
        builder.ConfigureVoid();

        builder.HasOne<PhysicianOrder>().WithMany().HasForeignKey(a => a.OrderId).OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_medication_administrations_physician_orders_order_id");
        builder.HasOne<Encounter>().WithMany().HasForeignKey(a => a.EncounterId).OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_medication_administrations_encounters_encounter_id");
        builder.HasOne<Patient>().WithMany().HasForeignKey(a => a.PatientId).OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_medication_administrations_patients_patient_id");
        builder.HasOne<Employee>().WithMany().HasForeignKey(a => a.AdministeredBy).OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_medication_administrations_employees_administered_by");

        builder.HasIndex(a => new { a.OrderId, a.AdministeredAt });
        builder.HasIndex(a => new { a.EncounterId, a.AdministeredAt });
        builder.HasIndex(a => new { a.PatientId, a.AdministeredAt });
    }
}
