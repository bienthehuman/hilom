using Hilom.Infrastructure.Persistence;
using Hilom.Patients.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hilom.Patients.Persistence;

internal sealed class PatientAllergyConfiguration : IEntityTypeConfiguration<PatientAllergy>
{
    public const string OneActivePerAllergen = "ux_patient_allergies_active_allergen";

    public void Configure(EntityTypeBuilder<PatientAllergy> builder)
    {
        builder.ToTable("patient_allergies", table =>
        {
            table.HasEnumCheck<PatientAllergy, AllergyCategory>("category");
            table.HasEnumCheck<PatientAllergy, AllergySeverity>("severity");
            table.HasEnumCheck<PatientAllergy, AllergyStatus>("status");

            // A mistake needs a reason, so the struck-through entry still explains itself.
            table.HasCheckConstraint(
                "ck_patient_allergies_entered_in_error_note",
                "status <> 'entered_in_error' OR (status_note IS NOT NULL AND btrim(status_note) <> '')");
        });

        builder.HasOne<Patient>().WithMany().HasForeignKey(a => a.PatientId).OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_patient_allergies_patients_patient_id");
        builder.HasIndex(a => a.PatientId);
        builder.Property(a => a.Allergen).HasColumnType("citext").HasMaxLength(PatientAllergy.AllergenMaxLength);
        builder.Property(a => a.Category).HasEnumText();
        builder.Property(a => a.Severity).HasEnumText();
        builder.Property(a => a.Reaction).HasMaxLength(PatientAllergy.ReactionMaxLength);
        builder.Property(a => a.Status).HasEnumText();
        builder.Property(a => a.StatusNote).HasMaxLength(PatientAllergy.NoteMaxLength);

        // At most one active entry per allergen (ignoring case); resolved and mistaken entries stay as history.
        builder.HasIndex(a => new { a.PatientId, a.Allergen })
            .IsUnique()
            .HasFilter("status = 'active'")
            .HasDatabaseName(OneActivePerAllergen);
    }
}
