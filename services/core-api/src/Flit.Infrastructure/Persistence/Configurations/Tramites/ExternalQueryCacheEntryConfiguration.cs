using Flit.Tramites.Domain.Entities;
using Flit.Infrastructure.Persistence.Schemas;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flit.Infrastructure.Persistence.Configurations.Tramites;

/// <summary>
/// Mapeo EF Core de la caché cross-trámite de consultas externas (HU #10878, ADR-0030). El DDL lo
/// gestiona la migración SQL cruda (40-HU10878-external-query-cache.sql): CHECKs de forma por
/// <c>subject_kind</c>, índices únicos parciales persona/vehículo, FKs, RLS y triggers (row_version +
/// audit) que el <c>MigrationBuilder</c> no modela. Mismo patrón que
/// <see cref="ProcedureInstancePrendaConfiguration"/>/<see cref="SignatureVaultEntityConfiguration"/>:
/// la entidad se excluye de migraciones; se declaran los triggers para que EF emita UPDATE compatible
/// con <c>row_version</c> como concurrency token.
/// </summary>
internal sealed class ExternalQueryCacheEntryConfiguration : IEntityTypeConfiguration<ExternalQueryCacheEntry>
{
    public void Configure(EntityTypeBuilder<ExternalQueryCacheEntry> builder)
    {
        builder.ToTable("external_query_cache", SchemaNames.Tramites, t =>
        {
            t.ExcludeFromMigrations();
            t.HasTrigger("tr_external_query_cache_row_version");
            t.HasTrigger("tr_external_query_cache_audit");
        });

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("uuidv7()");

        builder.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(x => x.ExternalDataSourceId).HasColumnName("external_data_source_id").IsRequired();
        builder.Property(x => x.SubjectKind).HasColumnName("subject_kind").HasMaxLength(10).IsRequired();

        builder.Property(x => x.DocumentType).HasColumnName("document_type").HasMaxLength(10);
        builder.Property(x => x.DocumentNumber).HasColumnName("document_number").HasMaxLength(30);
        builder.Property(x => x.VehicleIdentifier).HasColumnName("vehicle_identifier").HasMaxLength(20);

        builder.Property(x => x.Payload).HasColumnName("payload").HasColumnType("jsonb")
            .IsRequired().HasDefaultValueSql("'[]'");

        builder.Property(x => x.QueriedAt).HasColumnName("queried_at").IsRequired();
        builder.Property(x => x.ExpiresAt).HasColumnName("expires_at").IsRequired();

        builder.Property(x => x.SourceProcedureInstanceId).HasColumnName("source_procedure_instance_id");

        builder.Property(x => x.ReuseCount).HasColumnName("reuse_count").IsRequired().HasDefaultValue(0);
        builder.Property(x => x.LastReusedAt).HasColumnName("last_reused_at");

        builder.Property(x => x.RowVersion).HasColumnName("row_version").HasDefaultValue(0L).IsConcurrencyToken();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.CreatedBy).HasColumnName("created_by");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        builder.Property(x => x.UpdatedBy).HasColumnName("updated_by");

        // Índices únicos parciales por sujeto (una fila reutilizable por tenant+fuente+llave).
        builder.HasIndex(x => new { x.TenantId, x.ExternalDataSourceId, x.DocumentType, x.DocumentNumber })
            .IsUnique()
            .HasFilter("subject_kind = 'person'")
            .HasDatabaseName("uq_external_query_cache_person");

        builder.HasIndex(x => new { x.TenantId, x.ExternalDataSourceId, x.VehicleIdentifier })
            .IsUnique()
            .HasFilter("subject_kind = 'vehicle'")
            .HasDatabaseName("uq_external_query_cache_vehicle");

        // Checklist A11: tenant_id primero. Housekeeping opcional de vencidos usa expires_at.
        builder.HasIndex(x => new { x.TenantId, x.ExpiresAt })
            .HasDatabaseName("ix_external_query_cache_tenant_expires");

        // Checklist A9: índices cubriendo las FKs.
        builder.HasIndex(x => x.ExternalDataSourceId)
            .HasDatabaseName("ix_external_query_cache_data_source");

        builder.HasIndex(x => x.SourceProcedureInstanceId)
            .HasDatabaseName("ix_external_query_cache_source_instance");
    }
}
