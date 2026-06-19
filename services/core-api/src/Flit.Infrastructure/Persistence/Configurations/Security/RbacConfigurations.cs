using Flit.Infrastructure.Persistence.Entities.Security;
using Flit.Infrastructure.Persistence.Schemas;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flit.Infrastructure.Persistence.Configurations.Security;

internal sealed class SecurityModuleConfiguration : IEntityTypeConfiguration<SecurityModule>
{
    public void Configure(EntityTypeBuilder<SecurityModule> builder)
    {
        builder.ToTable("modules", SchemaNames.Security);
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Code).HasMaxLength(50).IsRequired();
        builder.Property(x => x.Name).HasMaxLength(100).IsRequired();
    }
}

internal sealed class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    public void Configure(EntityTypeBuilder<Role> builder)
    {
        builder.ToTable("roles", SchemaNames.Security);
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Code).HasMaxLength(50).IsRequired();
        builder.Property(x => x.Name).HasMaxLength(100).IsRequired();
        builder.Property(x => x.RowVersion).IsConcurrencyToken();
    }
}

internal sealed class RbacActionConfiguration : IEntityTypeConfiguration<RbacAction>
{
    public void Configure(EntityTypeBuilder<RbacAction> builder)
    {
        builder.ToTable("permissions", SchemaNames.Security);
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Slug).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Name).HasMaxLength(150).IsRequired();
    }
}

internal sealed class RoleGrantConfiguration : IEntityTypeConfiguration<RoleGrant>
{
    public void Configure(EntityTypeBuilder<RoleGrant> builder)
    {
        builder.ToTable("role_permissions", SchemaNames.Security);
        builder.HasKey(x => x.Id);
        builder.HasOne(x => x.Role).WithMany().HasForeignKey(x => x.RoleId);
        builder.HasOne(x => x.Action).WithMany().HasForeignKey(x => x.PermissionId);
    }
}

internal sealed class UserRoleAssignmentConfiguration : IEntityTypeConfiguration<UserRoleAssignment>
{
    public void Configure(EntityTypeBuilder<UserRoleAssignment> builder)
    {
        builder.ToTable("user_role_assignments", SchemaNames.Security);
        builder.HasKey(x => x.Id);
        builder.Property(x => x.RowVersion).IsConcurrencyToken();
        builder.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId);
        builder.HasOne(x => x.Role).WithMany().HasForeignKey(x => x.RoleId);
    }
}
