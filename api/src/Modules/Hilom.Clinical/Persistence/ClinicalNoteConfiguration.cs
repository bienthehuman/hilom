using Hilom.Clinical.Domain;
using Hilom.Infrastructure.Persistence;
using Hilom.Patients.Domain;
using Hilom.Workforce.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hilom.Clinical.Persistence;

internal sealed class ClinicalNoteConfiguration : IEntityTypeConfiguration<ClinicalNote>
{
    /// <summary>A note is replaced at most once: the correction of a correction replaces the newer one.</summary>
    public const string OneReplacement = "ux_clinical_notes_replaces_note_id";

    public void Configure(EntityTypeBuilder<ClinicalNote> builder)
    {
        builder.ToTable("clinical_notes", table =>
        {
            table.HasEnumCheck<ClinicalNote, NoteType>("note_type");
            table.HasCheckConstraint("ck_clinical_notes_body", "length(btrim(body)) > 0");
            table.HasVoidCheck("clinical_notes");
        });

        builder.Property(n => n.NoteType).HasEnumText();
        builder.Property(n => n.Body).HasMaxLength(ClinicalNote.BodyMaxLength);
        builder.ConfigureVoid();

        builder.HasOne<Encounter>().WithMany().HasForeignKey(n => n.EncounterId).OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_clinical_notes_encounters_encounter_id");
        builder.HasOne<Patient>().WithMany().HasForeignKey(n => n.PatientId).OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_clinical_notes_patients_patient_id");
        builder.HasOne<Employee>().WithMany().HasForeignKey(n => n.AuthoredBy).OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_clinical_notes_employees_authored_by");
        builder.HasOne<ClinicalNote>().WithMany().HasForeignKey(n => n.ReplacesNoteId).OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_clinical_notes_clinical_notes_replaces_note_id");

        builder.HasIndex(n => new { n.EncounterId, n.AuthoredAt });
        builder.HasIndex(n => new { n.PatientId, n.AuthoredAt });
        builder.HasIndex(n => n.ReplacesNoteId).IsUnique().HasDatabaseName(OneReplacement);
    }
}
