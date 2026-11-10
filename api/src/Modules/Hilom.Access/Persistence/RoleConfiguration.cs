using Hilom.Access.Domain;
using Hilom.Infrastructure.Persistence;
using Hilom.SharedKernel.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hilom.Access.Persistence;

internal sealed class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    public void Configure(EntityTypeBuilder<Role> builder)
    {
        builder.ToTable("roles");
        builder.Property(r => r.Name).HasColumnType("citext").HasMaxLength(80);
        builder.HasIndex(r => r.Name).IsUnique();
        builder.Property(r => r.Description).HasMaxLength(300);

        // At most one system role.
        builder.HasIndex(r => r.IsSystem).IsUnique().HasFilter("is_system");
        builder.HasIndex(r => r.IsPortal).IsUnique().HasFilter("is_portal");

        builder.OwnsMany(r => r.Grants, grants =>
        {
            grants.ToTable("role_permissions", table => table.HasEnumCheck<Role, RoleGrant, PermissionScope>("scope"));
            grants.WithOwner().HasForeignKey("RoleId");
            grants.HasKey("RoleId", nameof(RoleGrant.Permission));
            grants.Property(g => g.Permission).HasMaxLength(100);
            grants.Property(g => g.Scope).HasEnumText();
        });
        builder.Navigation(r => r.Grants).HasField("_grants").UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class UserRoleConfiguration : IEntityTypeConfiguration<UserRole>
{
    public void Configure(EntityTypeBuilder<UserRole> builder)
    {
        builder.ToTable("user_roles");
        builder.HasKey(ur => new { ur.UserId, ur.RoleId });
        builder.HasOne<User>().WithMany().HasForeignKey(ur => ur.UserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Role>().WithMany().HasForeignKey(ur => ur.RoleId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(ur => ur.AssignedBy).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(ur => ur.RoleId);
    }
}
