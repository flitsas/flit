using Flit.Infrastructure.Persistence.Schemas;
using Flit.Tramites.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flit.Infrastructure.Persistence.Configurations.Tramites;

/// <summary>
/// Mapeo EF Core del payload crudo sanitizado (HU #11302, ADR-0041). El DDL lo gestiona la migración
/// SQL cruda (<c>59-certificaciones-externas.sql</c>): CHECKs de vocabulario, RLS y triggers de
/// <c>row_version</c> + bitácora que el <c>MigrationBuilder</c> no modela. Mismo patrón que
/// <see cref="ExternalQueryCacheEntryConfiguration"/>: la entidad se excluye de migraciones y se
/// declaran los triggers para que EF emita UPDATE compatible con <c>row_version</c> como
/// concurrency token.
/// </summary>
internal sealed class ExternalQueryPayloadConfiguration : IEntityTypeConfiguration<ExternalQueryPayload>
{
    public void Configure(EntityTypeBuilder<ExternalQueryPayload> builder)
    {
        builder.ToTable("external_query_payloads", SchemaNames.Tramites, t =>
        {
            t.ExcludeFromMigrations();
            t.HasTrigger("tr_external_query_payloads_row_version");
            t.HasTrigger("tr_external_query_payloads_audit");
        });

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("uuidv7()");

        builder.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(x => x.ProcedureInstanceId).HasColumnName("procedure_instance_id").IsRequired();

        builder.Property(x => x.ProviderKey).HasColumnName("provider_key").HasMaxLength(40).IsRequired();
        builder.Property(x => x.SubjectKind).HasColumnName("subject_kind").HasMaxLength(10).IsRequired();
        builder.Property(x => x.SubjectKey).HasColumnName("subject_key").HasMaxLength(40);

        builder.Property(x => x.Payload).HasColumnName("payload").HasColumnType("jsonb").IsRequired();

        builder.Property(x => x.QueriedAt).HasColumnName("queried_at").IsRequired();

        // D6 — retención indefinida: hoy nadie escribe esta columna.
        builder.Property(x => x.ExpiresAt).HasColumnName("expires_at");

        builder.Property(x => x.RowVersion).HasColumnName("row_version").HasDefaultValue(0L).IsConcurrencyToken();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.CreatedBy).HasColumnName("created_by");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        builder.Property(x => x.UpdatedBy).HasColumnName("updated_by");

        builder.HasIndex(x => new { x.TenantId, x.ProcedureInstanceId, x.QueriedAt })
            .HasDatabaseName("ix_external_query_payloads_tenant_instance");

        builder.HasIndex(x => x.ProcedureInstanceId)
            .HasDatabaseName("ix_external_query_payloads_instance");

        builder.HasIndex(x => new { x.ProviderKey, x.SubjectKind, x.QueriedAt })
            .HasDatabaseName("ix_external_query_payloads_provider_subject");
    }
}
