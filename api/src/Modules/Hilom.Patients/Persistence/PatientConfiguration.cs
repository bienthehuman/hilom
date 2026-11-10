using Hilom.Infrastructure.Persistence;
using Hilom.Patients.Domain;
using Hilom.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hilom.Patients.Persistence;

internal sealed class PatientConfiguration : IEntityTypeConfiguration<Patient>
{
    /// <summary>Feeds the number in <c>MRN-000123</c>. Never reset.</summary>
    public const string MrnSequence = "patient_mrn_seq";

    public void Configure(EntityTypeBuilder<Patient> builder)
    {
        builder.Metadata.Model.AddSequence(MrnSequence, PatientsModule.SchemaName);

        builder.ToTable("patients", table =>
        {
            table.HasEnumCheck<Patient, Sex>("sex");
            table.HasEnumCheck<Patient, CivilStatus>("civil_status");
        });

        builder.Property(p => p.Mrn).HasMaxLength(20);
        builder.HasIndex(p => p.Mrn).IsUnique();

        builder.Property(p => p.FirstName).HasMaxLength(Patient.NameMaxLength);
        builder.Property(p => p.MiddleName).HasMaxLength(Patient.NameMaxLength);
        builder.Property(p => p.LastName).HasMaxLength(Patient.NameMaxLength);
        builder.HasIndex(p => new { p.LastName, p.FirstName });
        builder.HasIndex(p => p.BirthDate);

        builder.Property(p => p.Sex).HasEnumText();
        builder.Property(p => p.CivilStatus).HasEnumText();
        builder.Property(p => p.Religion).HasMaxLength(Patient.ReligionMaxLength);
        builder.Property(p => p.Address).HasMaxLength(Patient.AddressMaxLength);
        builder.Property(p => p.ContactNumber).HasMaxLength(Patient.ContactMaxLength);
        builder.Property(p => p.EmergencyContactName).HasMaxLength(Patient.EmergencyNameMaxLength);
        builder.Property(p => p.EmergencyContactRelation).HasMaxLength(Patient.RelationMaxLength);
        builder.Property(p => p.EmergencyContactNumber).HasMaxLength(Patient.ContactMaxLength);
    }
}
