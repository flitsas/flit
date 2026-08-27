using Flit.Infrastructure.Persistence.Entities.Admin;
using Flit.Infrastructure.Persistence.Schemas;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flit.Infrastructure.Persistence.Configurations.Admin;

/// <summary>
/// Mapeo EF Core de la compañía representada (HU #10900, ADR-0033). El DDL lo gestiona la migración
/// SQL cruda (39-HU10900-legal-representatives-deeds.sql): índice único (tenant, NIT), CHECK de
/// document_type, RLS y triggers (row_version + audit). Entidad ExcludeFromMigrations (patrón del
/// baúl); el snapshot la refleja para el modelo EF y se declaran los triggers para que EF emita
/// UPDATE compatible con row_version como concurrency token.
/// </summary>
internal sealed class RepresentedCompanyConfiguration : IEntityTypeConfiguration<RepresentedCompanyEntity>
{
    public void Configure(EntityTypeBuilder<RepresentedCompanyEntity> builder)
    {
        builder.ToTable("represented_companies", SchemaNames.Admin, t =>
        {
            t.ExcludeFromMigrations();
            t.HasTrigger("tr_represented_companies_row_version");
            t.HasTrigger("tr_represented_companies_audit");
        });

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasDefaultValueSql("uuidv7()");

        builder.Property(x => x.TenantId).IsRequired();
        builder.Property(x => x.DocumentType).HasMaxLength(10).IsRequired().HasDefaultValue("NIT");
        builder.Property(x => x.DocumentNumber).HasMaxLength(20).IsRequired();
        builder.Property(x => x.Name).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Email).HasMaxLength(200);
        builder.Property(x => x.Address).HasMaxLength(300);
        builder.Property(x => x.City).HasMaxLength(120);
        builder.Property(x => x.Phone).HasMaxLength(40);
        builder.Property(x => x.RepresentativeId).IsRequired(false);
        builder.Property(x => x.IsActive).HasDefaultValue(true).IsRequired();
        builder.Property(x => x.RowVersion).HasDefaultValue(0L).IsConcurrencyToken();
        builder.Property(x => x.CreatedAt).IsRequired();

        builder.HasIndex(x => new { x.TenantId, x.RepresentativeId, x.DocumentNumber })
            .IsUnique()
            .HasFilter("is_active AND representative_id IS NOT NULL")
            .HasDatabaseName("uq_represented_companies_owner_nit");
        builder.HasIndex(x => new { x.TenantId, x.DocumentNumber })
            .IsUnique()
            .HasFilter("is_active AND representative_id IS NULL")
            .HasDatabaseName("uq_represented_companies_orphan_nit");
        builder.HasIndex(x => x.RepresentativeId)
            .HasDatabaseName("ix_represented_companies_representative_id");
    }
}
