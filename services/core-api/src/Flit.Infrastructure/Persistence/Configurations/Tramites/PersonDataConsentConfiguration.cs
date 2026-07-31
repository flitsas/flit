using Flit.Tramites.Domain.Entities;
using Flit.Infrastructure.Persistence.Schemas;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flit.Infrastructure.Persistence.Configurations.Tramites;

/// <summary>
/// Mapeo EF Core del gate de consentimiento Habeas Data para el reúso cross-trámite de datos de
/// persona (HU #10878, ADR-0031). El DDL lo gestiona la migración SQL cruda
/// (40-HU10878-external-query-cache.sql): CHECK de fechas por estado, índice único por
/// (tenant, documento), FK, RLS y triggers (row_version + audit) que el <c>MigrationBuilder</c> no
/// modela. Mismo patrón de exclusión que <see cref="ExternalQueryCacheEntryConfiguration"/>.
/// </summary>
internal sealed class PersonDataConsentConfiguration : IEntityTypeConfiguration<PersonDataConsent>
{
    public void Configure(EntityTypeBuilder<PersonDataConsent> builder)
    {
        builder.ToTable("person_data_consents", SchemaNames.Tramites, t =>
        {
            t.ExcludeFromMigrations();
            t.HasTrigger("tr_person_data_consents_row_version");
            t.HasTrigger("tr_person_data_consents_audit");
        });

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("uuidv7()");

        builder.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(x => x.DocumentType).HasColumnName("document_type").HasMaxLength(10).IsRequired();
        builder.Property(x => x.DocumentNumber).HasColumnName("document_number").HasMaxLength(30).IsRequired();

        builder.Property(x => x.Status).HasColumnName("status").HasMaxLength(10)
            .IsRequired().HasDefaultValue(PersonDataConsentStatus.Unknown);

        builder.Property(x => x.ConsentVersion).HasColumnName("consent_version").HasMaxLength(40);
        builder.Property(x => x.ConsentSource).HasColumnName("consent_source").HasMaxLength(40);

        builder.Property(x => x.GrantedAt).HasColumnName("granted_at");
        builder.Property(x => x.RevokedAt).HasColumnName("revoked_at");
        builder.Property(x => x.CapturedIp).HasColumnName("captured_ip").HasMaxLength(64);
        builder.Property(x => x.CapturedUserAgent).HasColumnName("captured_user_agent").HasMaxLength(120);

        builder.Property(x => x.SourceProcedureInstanceId).HasColumnName("source_procedure_instance_id");

        builder.Property(x => x.RowVersion).HasColumnName("row_version").HasDefaultValue(0L).IsConcurrencyToken();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.CreatedBy).HasColumnName("created_by");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        builder.Property(x => x.UpdatedBy).HasColumnName("updated_by");

        builder.HasIndex(x => new { x.TenantId, x.DocumentType, x.DocumentNumber })
            .IsUnique()
            .HasDatabaseName("uq_person_data_consents_person");

        // Checklist A9: índice cubriendo la FK.
        builder.HasIndex(x => x.SourceProcedureInstanceId)
            .HasDatabaseName("ix_person_data_consents_source_instance");
    }
}
