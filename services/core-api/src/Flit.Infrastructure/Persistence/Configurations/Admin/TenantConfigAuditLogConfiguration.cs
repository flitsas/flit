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

        // HU #10678: tenant_id pasa a NULLABLE — el rastro unificado también audita eventos
        // de autenticación/seguridad sin tenant resoluble (p. ej. login fallido).
        builder.Property(x => x.TenantId);
        builder.Property(x => x.EntityName).HasMaxLength(80).IsRequired();
        builder.Property(x => x.FieldName).HasMaxLength(80).IsRequired();

        builder.Property(x => x.OldValue).HasColumnType("jsonb");
        builder.Property(x => x.NewValue).HasColumnType("jsonb");

        builder.Property(x => x.ChangedAt).IsRequired();

        // Auditoría mínima RNF01 (ADR-0024): IP, resultado, operación y código de error.
        // text sin longitud fija: client_ip admite listas X-Forwarded-For normalizadas.
        builder.Property(x => x.ClientIp).HasColumnType("text");
        builder.Property(x => x.Result).HasColumnType("text");
        builder.Property(x => x.Operation).HasColumnType("text");
        builder.Property(x => x.ErrorCode).HasColumnType("text");

        // HU #10678 — auditoría administrativa/seguridad transversal (rastro unificado).
        builder.Property(x => x.TenantType).HasMaxLength(20);
        builder.Property(x => x.Module).HasMaxLength(20);
        builder.Property(x => x.TargetEntityType).HasMaxLength(40);
        builder.Property(x => x.TargetEntityId);
        builder.Property(x => x.UserAgent).HasColumnType("text");

        builder.HasIndex(x => new { x.TenantId, x.ChangedAt })
            .HasDatabaseName("ix_tenant_config_audit_logs_tenant_id_changed_at")
            .IsDescending(false, true);

        // Consultas de auditoría filtradas por resultado (éxito/fallo) más recientes primero.
        builder.HasIndex(x => new { x.TenantId, x.Result, x.ChangedAt })
            .HasDatabaseName("ix_tenant_config_audit_logs_tenant_id_result_changed_at")
            .IsDescending(false, false, true);

        // HU #10678 — índices para las consultas globales del SuperAdmin (rastro unificado).
        builder.HasIndex(x => x.ChangedAt)
            .HasDatabaseName("ix_audit_logs_changed_at")
            .IsDescending(true);
        builder.HasIndex(x => new { x.Module, x.ChangedAt })
            .HasDatabaseName("ix_audit_logs_module_changed_at")
            .IsDescending(false, true);
        builder.HasIndex(x => new { x.TargetEntityId, x.ChangedAt })
            .HasDatabaseName("ix_audit_logs_target_changed_at")
            .IsDescending(false, true);
        builder.HasIndex(x => new { x.ChangedBy, x.ChangedAt })
            .HasDatabaseName("ix_audit_logs_changed_by_changed_at")
            .IsDescending(false, true);
    }
}
