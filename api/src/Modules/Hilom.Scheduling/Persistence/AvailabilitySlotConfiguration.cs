using Hilom.Infrastructure.Persistence;
using Hilom.Scheduling.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hilom.Scheduling.Persistence;

internal sealed class AvailabilitySlotConfiguration : IEntityTypeConfiguration<AvailabilitySlot>
{
    /// <summary>
    /// A physician's open slots never overlap. EF has no fluent API for exclusion constraints, so the migration
    /// creates it with this name. Closed slots are history and don't block new ones.
    /// </summary>
    public const string NoOverlap = "ex_availability_slots_no_overlap";

    public const string PhysicianProfileForeignKey = "fk_availability_slots_physician_profiles_physician_id";

    public void Configure(EntityTypeBuilder<AvailabilitySlot> builder)
    {
        builder.ToTable("availability_slots", table =>
        {
            table.HasEnumCheck<AvailabilitySlot, SlotStatus>("status");
            table.HasCheckConstraint(
                "ck_availability_slots_during",
                "NOT isempty(during) AND NOT lower_inf(during) AND NOT upper_inf(during) AND upper(during) > lower(during)");
        });

        builder.Property(s => s.During).HasColumnType("tstzrange");
        builder.Property(s => s.Status).HasEnumText();
        builder.Ignore(s => s.StartsAt);
        builder.Ignore(s => s.EndsAt);

        // physician_id → workforce.physician_profiles(employee_id): the profile is an owned type, so the migration
        // adds this foreign key in SQL (PhysicianProfileForeignKey).
        builder.HasIndex(s => new { s.PhysicianId, s.Status });

        // Day lists and booking search filter on lower(during); EF can't model an expression index, so the
        // AddQueryIndexes migration creates it in SQL.
    }

    /// <summary>Created by the AddQueryIndexes migration.</summary>
    public const string StartIndex = "ix_availability_slots_start";
}
