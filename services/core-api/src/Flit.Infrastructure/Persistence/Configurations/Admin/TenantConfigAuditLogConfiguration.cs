using Flit.Infrastructure.Persistence.Entities.Admin;
using Flit.Infrastructure.Persistence.Schemas;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flit.Infrastructure.Persistence.Configurations.Admin;

internal sealed class TenantConfigAuditLogConfiguration
    : IEntityTypeConfiguration<TenantConfigAuditLog>
{
    public void Configure(EntityTypeBuilder<TenantConfigAuditLog> builder)
    {
        builder.ToTable("tenant_config_audit_logs", SchemaNames.Admin);

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasDefaultValueSql("uuidv7()");

        builder.Property(x => x.TenantId).IsRequired();
        builder.Property(x => x.EntityName).HasMaxLength(80).IsRequired();
        builder.Property(x => x.FieldName).HasMaxLength(80).IsRequired();

        builder.Property(x => x.OldValue).HasColumnType("jsonb");
        builder.Property(x => x.NewValue).HasColumnType("jsonb");

        builder.Property(x => x.ChangedAt).IsRequired();

        builder.HasIndex(x => new { x.TenantId, x.ChangedAt })
            .HasDatabaseName("ix_tenant_config_audit_logs_tenant_id_changed_at")
            .IsDescending(false, true);
    }
}
