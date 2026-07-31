using Flit.Infrastructure.Persistence.Entities.Admin;
using Flit.Infrastructure.Persistence.Schemas;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flit.Infrastructure.Persistence.Configurations.Admin;

/// <summary>
/// Mapeo EF Core de la escritura (HU #10900, ADR-0033). El DDL lo gestiona la migración SQL cruda:
/// CHECK de vigencia (hasta ≥ desde), índice de consumo (tenant, activa, vigencia), RLS y triggers
/// (row_version + audit). Entidad ExcludeFromMigrations (patrón del baúl).
/// </summary>
internal sealed class CompanyDeedConfiguration : IEntityTypeConfiguration<CompanyDeedEntity>
{
    public void Configure(EntityTypeBuilder<CompanyDeedEntity> builder)
    {
        builder.ToTable("company_deeds", SchemaNames.Admin, t =>
        {
            t.ExcludeFromMigrations();
            t.HasTrigger("tr_company_deeds_row_version");
            t.HasTrigger("tr_company_deeds_audit");
        });

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasDefaultValueSql("uuidv7()");

        builder.Property(x => x.TenantId).IsRequired();
        // Representante que asoció la escritura (Feature #10929). Nullable (compat con escrituras
        // legadas). La FK y el índice los lleva el DDL crudo (44-HU-representante-escritura.sql).
        builder.Property(x => x.RepresentativeId).HasColumnName("representative_id");
        builder.Property(x => x.Description).HasMaxLength(500).IsRequired();
        builder.Property(x => x.StoragePath).HasMaxLength(1000).IsRequired();
        builder.Property(x => x.StorageSha256).HasMaxLength(64).IsRequired();
        builder.Property(x => x.VigenciaDesde).IsRequired();
        builder.Property(x => x.VigenciaHasta).IsRequired();
        builder.Property(x => x.IsActive).HasDefaultValue(true).IsRequired();
        builder.Property(x => x.RowVersion).HasDefaultValue(0L).IsConcurrencyToken();
        builder.Property(x => x.CreatedAt).IsRequired();

        builder.HasIndex(x => new { x.TenantId, x.IsActive, x.VigenciaHasta })
            .HasDatabaseName("ix_company_deeds_tenant_active_vigencia");

        builder.HasIndex(x => new { x.TenantId, x.RepresentativeId })
            .HasDatabaseName("ix_company_deeds_tenant_representative");
    }
}
