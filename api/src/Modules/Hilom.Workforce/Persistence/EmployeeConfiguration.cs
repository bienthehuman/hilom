using Hilom.Access.Domain;
using Hilom.Infrastructure.Persistence;
using Hilom.Workforce.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hilom.Workforce.Persistence;

internal sealed class EmployeeConfiguration : IEntityTypeConfiguration<Employee>
{
    /// <summary>Feeds the number in <c>EMP-2026-0042</c>. Never reset, so numbers stay unique across years.</summary>
    public const string NumberSequence = "employee_number_seq";

    public void Configure(EntityTypeBuilder<Employee> builder)
    {
        builder.Metadata.Model.AddSequence(NumberSequence, WorkforceModule.SchemaName);

        builder.ToTable("employees", table =>
        {
            table.HasEnumCheck<Employee, Sex>("sex");
            table.HasEnumCheck<Employee, ProfessionalType>("professional_type");
            table.HasEnumCheck<Employee, EmploymentType>("employment_type");
            table.HasEnumCheck<Employee, EmployeeStatus>("status");

            // Physicians and nurses must have a license number (docs/DATA_MODEL.md).
            table.HasCheckConstraint(
                "ck_employees_license_number",
                "professional_type NOT IN ('physician', 'nurse') OR (license_number IS NOT NULL AND btrim(license_number) <> '')");

            // A separation date exactly when separated.
            table.HasCheckConstraint(
                "ck_employees_separated_on",
                "(status = 'separated') = (separated_on IS NOT NULL)");
        });

        builder.Property(e => e.EmployeeNo).HasMaxLength(20);
        builder.HasIndex(e => e.EmployeeNo).IsUnique();

        builder.HasIndex(e => e.UserId).IsUnique();
        builder.HasOne<User>().WithMany().HasForeignKey(e => e.UserId).OnDelete(DeleteBehavior.Restrict);

        builder.Property(e => e.FirstName).HasMaxLength(Employee.NameMaxLength);
        builder.Property(e => e.MiddleName).HasMaxLength(Employee.NameMaxLength);
        builder.Property(e => e.LastName).HasMaxLength(Employee.NameMaxLength);
        builder.HasIndex(e => new { e.LastName, e.FirstName });

        builder.Property(e => e.Sex).HasEnumText();
        builder.Property(e => e.ContactNumber).HasMaxLength(Employee.ContactMaxLength);
        builder.Property(e => e.Email).HasColumnType("citext").HasMaxLength(Employee.EmailMaxLength);
        builder.Property(e => e.ProfessionalType).HasEnumText();
        builder.Property(e => e.LicenseNumber).HasMaxLength(Employee.LicenseMaxLength);
        builder.Property(e => e.EmploymentType).HasEnumText();
        builder.Property(e => e.Status).HasEnumText();

        builder.HasOne<Department>().WithMany().HasForeignKey(e => e.DepartmentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Position>().WithMany().HasForeignKey(e => e.PositionId).OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_employees_positions_position_id");

        builder.OwnsOne(e => e.Physician, physician =>
        {
            physician.ToTable("physician_profiles", table => table.HasCheckConstraint(
                "ck_physician_profiles_consultation_minutes",
                $"consultation_minutes BETWEEN {PhysicianProfile.MinConsultationMinutes} AND {PhysicianProfile.MaxConsultationMinutes}"));
            physician.WithOwner().HasForeignKey("EmployeeId");
            physician.HasKey("EmployeeId");
            physician.Property<Guid>("EmployeeId").HasColumnName("employee_id");
            physician.HasOne<Specialization>().WithMany().HasForeignKey(p => p.SpecializationId).OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_physician_profiles_specializations_specialization_id");
            physician.HasIndex(p => p.SpecializationId);
        });
    }
}
