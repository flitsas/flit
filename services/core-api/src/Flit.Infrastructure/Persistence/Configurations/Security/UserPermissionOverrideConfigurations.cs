using Flit.Infrastructure.Persistence.Entities.Security;
using Flit.Infrastructure.Persistence.Schemas;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flit.Infrastructure.Persistence.Configurations.Security;

internal sealed class UserPermissionOverrideConfiguration : IEntityTypeConfiguration<UserPermissionOverrideEntity>
{
    public void Configure(EntityTypeBuilder<UserPermissionOverrideEntity> builder)
    {
        builder.ToTable("user_permission_overrides", SchemaNames.Security);
        builder.HasKey(x => x.Id).HasName("pk_user_permission_overrides");
        builder.Property(x => x.Id).HasDefaultValueSql("uuidv7()");
        builder.Property(x => x.ProductCode).HasMaxLength(40).IsRequired();
        builder.Property(x => x.Effect).HasMaxLength(5).IsRequired();
        builder.Property(x => x.Source).HasMaxLength(16).IsRequired().HasDefaultValue("INDIVIDUAL");
        builder.Property(x => x.RowVersion).IsConcurrencyToken();
        builder.HasIndex(x => new { x.TenantId, x.UserId, x.ProductCode, x.PermissionId })
            .IsUnique()
            .HasFilter("deleted_at IS NULL")
            .HasDatabaseName("uq_user_permission_overrides_active");
        builder.HasIndex(x => x.UserId).HasDatabaseName("ix_user_permission_overrides_user_id");
        builder.HasIndex(x => x.PermissionId).HasDatabaseName("ix_user_permission_overrides_permission_id");
        builder.HasIndex(x => x.ProductCode).HasDatabaseName("ix_user_permission_overrides_product_code");
    }
}

internal sealed class UserPermissionAuditConfiguration : IEntityTypeConfiguration<UserPermissionAuditEntity>
{
    public void Configure(EntityTypeBuilder<UserPermissionAuditEntity> builder)
    {
        builder.ToTable("user_permission_audit", SchemaNames.Security);
        builder.HasKey(x => x.Id).HasName("pk_user_permission_audit");
        builder.Property(x => x.Id).HasDefaultValueSql("uuidv7()");
        builder.Property(x => x.ProductCode).HasMaxLength(40);
        builder.Property(x => x.ModuleCode).HasMaxLength(50);
        builder.Property(x => x.PermissionSlug).HasMaxLength(120);
        builder.Property(x => x.Action).HasMaxLength(32).IsRequired();
        builder.Property(x => x.Outcome).HasMaxLength(10).IsRequired().HasDefaultValue("OK");
        builder.Property(x => x.ReasonCode).HasMaxLength(40);
        builder.Property(x => x.ValueBefore).HasMaxLength(5);
        builder.Property(x => x.ValueAfter).HasMaxLength(5);
        builder.Property(x => x.CreatedAt).HasDefaultValueSql("now()");
        builder.HasIndex(x => new { x.TenantId, x.TargetUserId, x.CreatedAt })
            .IsDescending(false, false, true)
            .HasDatabaseName("ix_user_permission_audit_tenant_target");
        builder.HasIndex(x => new { x.TenantId, x.ActorUserId, x.CreatedAt })
            .IsDescending(false, false, true)
            .HasDatabaseName("ix_user_permission_audit_tenant_actor");
    }
}
