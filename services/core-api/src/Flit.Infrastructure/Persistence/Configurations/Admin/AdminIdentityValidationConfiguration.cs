using Flit.Infrastructure.Persistence.Entities.Admin;
using Flit.Infrastructure.Persistence.Schemas;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flit.Infrastructure.Persistence.Configurations.Admin;

/// <summary>
/// Mapeo EF Core de la validación de identidad administrativa desacoplada (HU #10907, ADR-0034). El DDL
/// lo gestiona la migración SQL cruda (40-HU10907-admin-identity-validations.sql): índices, CHECK de
/// estado, RLS y triggers (row_version + audit). Entidad ExcludeFromMigrations (patrón del baúl /
/// representantes): el DDL crudo lleva el esquema y el snapshot solo refleja el modelo EF.
/// </summary>
internal sealed class AdminIdentityValidationConfiguration
    : IEntityTypeConfiguration<AdminIdentityValidationEntity>
{
    public void Configure(EntityTypeBuilder<AdminIdentityValidationEntity> builder)
    {
        builder.ToTable("admin_identity_validations", SchemaNames.Admin, t =>
        {
            t.ExcludeFromMigrations();
            t.HasTrigger("tr_admin_identity_validations_row_version");
            t.HasTrigger("tr_admin_identity_validations_audit");
        });

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasDefaultValueSql("uuidv7()");

        builder.Property(x => x.TenantId).IsRequired();
        builder.Property(x => x.SubjectType).HasMaxLength(40).IsRequired();
        builder.Property(x => x.SubjectRef).IsRequired();
        builder.Property(x => x.DocumentType).HasMaxLength(10).IsRequired();
        builder.Property(x => x.DocumentNumber).HasMaxLength(20).IsRequired();
        builder.Property(x => x.Name).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Email).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Status).HasMaxLength(20).IsRequired().HasDefaultValue("enviado");
        builder.Property(x => x.Provider).HasMaxLength(20).IsRequired().HasDefaultValue("kyverum");
        builder.Property(x => x.CaptureUrl).HasMaxLength(1000);
        builder.Property(x => x.KyverumVerificationId).HasMaxLength(100);
        builder.Property(x => x.WebhookSecretEncrypted);
        builder.Property(x => x.ProviderStatus).HasMaxLength(40);
        builder.Property(x => x.ProviderPayload).HasColumnType("jsonb");
        builder.Property(x => x.CertificateHash).HasMaxLength(200);
        // HU #11504 — conteo de intentos antes de terminalizar un rechazo (ver 74-HU11504-...sql).
        builder.Property(x => x.Attempts).HasDefaultValue(0).IsRequired();
        builder.Property(x => x.MaxAttempts).HasDefaultValue(3).IsRequired();
        builder.Property(x => x.LastAttemptAt).HasMaxLength(100);
        builder.Property(x => x.RowVersion).HasDefaultValue(0L).IsConcurrencyToken();
        builder.Property(x => x.CreatedAt).IsRequired();

        // Validación más reciente por sujeto (reenvío + anclaje).
        builder.HasIndex(x => new { x.TenantId, x.SubjectType, x.SubjectRef })
            .HasDatabaseName("ix_admin_identity_validations_tenant_subject");
        // Reconciliación por id de verificación del proveedor.
        builder.HasIndex(x => x.KyverumVerificationId)
            .HasDatabaseName("ix_admin_identity_validations_verification_id");
        // Barrido de vigencia/estado.
        builder.HasIndex(x => new { x.TenantId, x.Status, x.ValidUntil })
            .HasDatabaseName("ix_admin_identity_validations_tenant_status_valid_until");
    }
}
