using Hilom.Access.Domain;
using Hilom.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hilom.Access.Persistence;

internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("users", table =>
        {
            table.HasEnumCheck<User, UserStatus>("status");
            table.HasEnumCheck<User, AccountType>("account_type");
        });

        builder.Property(u => u.Email).HasColumnType("citext").HasMaxLength(320);
        builder.HasIndex(u => u.Email).IsUnique();

        builder.Property(u => u.DisplayName).HasMaxLength(200);
        builder.Property(u => u.PasswordHash).HasMaxLength(500);
        builder.Property(u => u.Status).HasEnumText();
        builder.Property(u => u.AccountType).HasEnumText();
        builder.Property(u => u.EmailCodeHash).HasMaxLength(64);
        builder.Ignore(u => u.MustVerifyEmail);
    }
}
