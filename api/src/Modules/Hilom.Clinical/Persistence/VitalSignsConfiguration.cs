using System.Globalization;
using Hilom.Clinical.Domain;
using Hilom.Patients.Domain;
using Hilom.Workforce.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hilom.Clinical.Persistence;

internal sealed class VitalSignsConfiguration : IEntityTypeConfiguration<VitalSigns>
{
    public void Configure(EntityTypeBuilder<VitalSigns> builder)
    {
        builder.ToTable("vital_signs", table =>
        {
            // Plausible values only (VitalSigns' limits): outside them is a typo, not a patient.
            table.HasCheckConstraint("ck_vital_signs_temperature_c", Between("temperature_c", VitalSigns.TemperatureMin, VitalSigns.TemperatureMax));
            table.HasCheckConstraint("ck_vital_signs_systolic_bp", Between("systolic_bp", VitalSigns.SystolicMin, VitalSigns.SystolicMax));
            table.HasCheckConstraint("ck_vital_signs_diastolic_bp", Between("diastolic_bp", VitalSigns.DiastolicMin, VitalSigns.DiastolicMax));
            table.HasCheckConstraint("ck_vital_signs_heart_rate", Between("heart_rate", VitalSigns.HeartRateMin, VitalSigns.HeartRateMax));
            table.HasCheckConstraint("ck_vital_signs_respiratory_rate", Between("respiratory_rate", VitalSigns.RespiratoryRateMin, VitalSigns.RespiratoryRateMax));
            table.HasCheckConstraint("ck_vital_signs_oxygen_saturation", Between("oxygen_saturation", 0, 100));
            table.HasCheckConstraint("ck_vital_signs_pain_score", Between("pain_score", 0, VitalSigns.PainMax));
            table.HasCheckConstraint("ck_vital_signs_weight_kg", Between("weight_kg", VitalSigns.WeightMin, VitalSigns.WeightMax));
            table.HasCheckConstraint("ck_vital_signs_height_cm", Between("height_cm", VitalSigns.HeightMin, VitalSigns.HeightMax));

            // Blood pressure is both numbers or neither, and diastolic is below systolic.
            table.HasCheckConstraint(
                "ck_vital_signs_blood_pressure",
                "(systolic_bp IS NULL) = (diastolic_bp IS NULL) AND (systolic_bp IS NULL OR diastolic_bp < systolic_bp)");

            // At least one measurement.
            table.HasCheckConstraint(
                "ck_vital_signs_any_measurement",
                "num_nonnulls(temperature_c, systolic_bp, heart_rate, respiratory_rate, oxygen_saturation, pain_score, weight_kg, height_cm) > 0");

            table.HasVoidCheck("vital_signs");
        });

        builder.ConfigureVoid();

        builder.Property(v => v.TemperatureC).HasPrecision(4, 1);
        builder.Property(v => v.WeightKg).HasPrecision(5, 2);
        builder.Property(v => v.HeightCm).HasPrecision(5, 2);
        builder.Property(v => v.Notes).HasMaxLength(VitalSigns.NotesMaxLength);
        builder.Ignore(v => v.Bmi);

        builder.HasOne<Encounter>().WithMany().HasForeignKey(v => v.EncounterId).OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_vital_signs_encounters_encounter_id");
        builder.HasOne<Patient>().WithMany().HasForeignKey(v => v.PatientId).OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_vital_signs_patients_patient_id");
        builder.HasOne<Employee>().WithMany().HasForeignKey(v => v.RecordedBy).OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_vital_signs_employees_recorded_by");

        builder.HasIndex(v => new { v.EncounterId, v.RecordedAt });
        builder.HasIndex(v => new { v.PatientId, v.RecordedAt });
    }

    private static string Between(string column, decimal min, decimal max) =>
        string.Create(CultureInfo.InvariantCulture, $"{column} IS NULL OR {column} BETWEEN {min} AND {max}");
}
