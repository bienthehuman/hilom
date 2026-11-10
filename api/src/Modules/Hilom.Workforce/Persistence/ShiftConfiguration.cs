using Hilom.Infrastructure.Persistence;
using Hilom.Workforce.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hilom.Workforce.Persistence;

internal sealed class ShiftConfiguration : IEntityTypeConfiguration<Shift>
{
    public void Configure(EntityTypeBuilder<Shift> builder)
    {
        builder.ToTable("shifts", table =>
        {
            table.HasEnumCheck<Shift, ShiftColor>("color");
            table.HasCheckConstraint("ck_shifts_starts_and_ends_differ", "starts_at <> ends_at");
        });
        builder.Property(s => s.Code).HasColumnType("citext").HasMaxLength(Shift.CodeMaxLength);
        builder.HasIndex(s => s.Code).IsUnique();
        builder.Property(s => s.Name).HasMaxLength(Shift.NameMaxLength);
        builder.Property(s => s.StartsAt).HasColumnType("time");
        builder.Property(s => s.EndsAt).HasColumnType("time");
        builder.Property(s => s.Color).HasEnumText();
        builder.Ignore(s => s.CrossesMidnight);
        builder.Ignore(s => s.Duration);
    }
}
