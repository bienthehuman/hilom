using Hilom.Access.Domain;
using Hilom.Clinical.Domain;
using Hilom.Facility.Domain;
using Hilom.Infrastructure.Persistence;
using Hilom.Patients.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hilom.Clinical.Persistence;

internal sealed class AdmissionConfiguration : IEntityTypeConfiguration<Admission>
{
    /// <summary>
    /// A patient can't be admitted twice at once: no two admissions of one patient overlap in time, and an open
    /// one runs forever. EF has no fluent API for exclusion constraints, so the migration creates it with this name.
    /// </summary>
    public const string OneAtATime = "ex_admissions_one_per_patient";

    public void Configure(EntityTypeBuilder<Admission> builder)
    {
        builder.ToTable("admissions", table =>
        {
            table.HasEnumCheck<Admission, DischargeDisposition>("discharge_disposition");

            // A discharge has when, by whom and how, all together, and never before the admission.
            table.HasCheckConstraint(
                "ck_admissions_discharge",
                "(discharged_at IS NULL AND discharge_disposition IS NULL AND discharged_by IS NULL) OR "
                + "(discharged_at IS NOT NULL AND discharge_disposition IS NOT NULL AND discharged_by IS NOT NULL AND discharged_at >= admitted_at)");
        });

        // One admission per inpatient encounter: it shares the encounter's id.
        builder.Property(a => a.Id).HasColumnName("encounter_id");
        builder.Ignore(a => a.EncounterId);
        builder.Property(a => a.AdmittingDiagnosis).HasMaxLength(Admission.AdmittingDiagnosisMaxLength);
        builder.Property(a => a.Reason).HasMaxLength(Admission.ReasonMaxLength);
        builder.Property(a => a.DischargeDisposition).HasEnumText();

        builder.HasOne<Encounter>().WithOne().HasForeignKey<Admission>(a => a.Id).OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_admissions_encounters_encounter_id");
        builder.HasOne<Patient>().WithMany().HasForeignKey(a => a.PatientId).OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_admissions_patients_patient_id");
        builder.HasOne<User>().WithMany().HasForeignKey(a => a.DischargedBy).OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_admissions_users_discharged_by");

        builder.HasIndex(a => new { a.PatientId, a.AdmittedAt });
    }
}

internal sealed class BedAssignmentConfiguration : IEntityTypeConfiguration<BedAssignment>
{
    /// <summary>No two patients in one bed at once. Created by the migration.</summary>
    public const string OnePatientPerBed = "ex_bed_assignments_one_patient_per_bed";

    /// <summary>A patient is in one bed at a time. Created by the migration.</summary>
    public const string OneBedPerAdmission = "ex_bed_assignments_one_bed_per_admission";

    /// <summary>(bed_id, ward_id) → facility.beds(id, ward_id): the copied ward is the bed's. Created by the migration.</summary>
    public const string WardMatchesBed = "fk_bed_assignments_beds_bed_id_ward_id";

    public void Configure(EntityTypeBuilder<BedAssignment> builder)
    {
        builder.ToTable("bed_assignments", table =>
            table.HasCheckConstraint(
                "ck_bed_assignments_during",
                "NOT isempty(during) AND NOT lower_inf(during) AND (upper_inf(during) OR upper(during) > lower(during))"));

        builder.Property(b => b.During).HasColumnType("tstzrange");
        builder.Property(b => b.Reason).HasMaxLength(Admission.ReasonMaxLength);

        builder.HasOne<Admission>().WithMany().HasForeignKey(b => b.AdmissionId).OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_bed_assignments_admissions_admission_id");
        builder.HasOne<Bed>().WithMany().HasForeignKey(b => b.BedId).OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_bed_assignments_beds_bed_id");
        builder.HasOne<Ward>().WithMany().HasForeignKey(b => b.WardId).OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_bed_assignments_wards_ward_id");
        builder.HasOne<User>().WithMany().HasForeignKey(b => b.MovedBy).OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_bed_assignments_users_moved_by");

        // The exclusion constraints index admission_id and bed_id with the range; this one serves ward scope.
        builder.HasIndex(b => b.WardId);
    }
}
