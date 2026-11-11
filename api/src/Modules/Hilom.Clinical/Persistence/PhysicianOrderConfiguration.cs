using Hilom.Access.Domain;
using Hilom.Clinical.Domain;
using Hilom.Infrastructure.Persistence;
using Hilom.Patients.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hilom.Clinical.Persistence;

internal sealed class PhysicianOrderConfiguration : IEntityTypeConfiguration<PhysicianOrder>
{
    /// <summary>ordered_by → workforce.physician_profiles(employee_id), added in SQL (the profile is an owned type).</summary>
    public const string PhysicianProfileForeignKey = "fk_physician_orders_physician_profiles_ordered_by";

    /// <summary>A medication order has its name, dose, route and frequency, none of them blank.</summary>
    public const string MedicationFieldsCheck = "ck_physician_orders_medication_fields";

    /// <summary>A discontinued order says when, by whom and why; one that isn't has none of them.</summary>
    public const string DiscontinueCheck = "ck_physician_orders_discontinue";

    public void Configure(EntityTypeBuilder<PhysicianOrder> builder)
    {
        builder.ToTable("physician_orders", table =>
        {
            table.HasEnumCheck<PhysicianOrder, OrderType>("order_type");
            table.HasEnumCheck<PhysicianOrder, OrderStatus>("status");
            table.HasCheckConstraint("ck_physician_orders_details", "length(btrim(details)) > 0");
            table.HasCheckConstraint(
                MedicationFieldsCheck,
                "order_type <> 'medication' OR ("
                + "length(btrim(coalesce(medication_name, ''))) > 0 AND length(btrim(coalesce(dose, ''))) > 0 AND "
                + "length(btrim(coalesce(route, ''))) > 0 AND length(btrim(coalesce(frequency, ''))) > 0)");
            table.HasCheckConstraint(
                DiscontinueCheck,
                "(status <> 'discontinued' AND discontinued_at IS NULL AND discontinued_by IS NULL AND discontinue_reason IS NULL) OR "
                + "(status = 'discontinued' AND discontinued_at IS NOT NULL AND discontinued_by IS NOT NULL AND length(btrim(discontinue_reason)) > 0)");
        });

        builder.Property(o => o.OrderType).HasEnumText();
        builder.Property(o => o.Status).HasEnumText();
        builder.Property(o => o.Details).HasMaxLength(PhysicianOrder.DetailsMaxLength);
        builder.Property(o => o.MedicationName).HasMaxLength(MedicationDetails.FieldMaxLength);
        builder.Property(o => o.Dose).HasMaxLength(MedicationDetails.FieldMaxLength);
        builder.Property(o => o.Route).HasMaxLength(MedicationDetails.FieldMaxLength);
        builder.Property(o => o.Frequency).HasMaxLength(MedicationDetails.FieldMaxLength);
        builder.Property(o => o.DiscontinueReason).HasMaxLength(PhysicianOrder.ReasonMaxLength);
        builder.Ignore(o => o.IsActive);

        builder.HasOne<Encounter>().WithMany().HasForeignKey(o => o.EncounterId).OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_physician_orders_encounters_encounter_id");
        builder.HasOne<Patient>().WithMany().HasForeignKey(o => o.PatientId).OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_physician_orders_patients_patient_id");
        builder.HasOne<User>().WithMany().HasForeignKey(o => o.DiscontinuedBy).OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_physician_orders_users_discontinued_by");

        builder.HasIndex(o => new { o.EncounterId, o.OrderedAt });
        builder.HasIndex(o => new { o.PatientId, o.OrderedAt });
    }
}
