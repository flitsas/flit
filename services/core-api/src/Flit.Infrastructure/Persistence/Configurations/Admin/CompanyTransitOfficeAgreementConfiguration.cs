using Flit.Infrastructure.Persistence.Entities.Admin;
using Flit.Infrastructure.Persistence.Schemas;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flit.Infrastructure.Persistence.Configurations.Admin;

/// <summary>
/// Mapeo EF Core del convenio compañía ↔ organismo. El esquema lo lleva el DDL crudo
/// (<c>52-convenio-compania-organismo.sql</c>), así que la entidad va <c>ExcludeFromMigrations</c>
/// (mismo patrón que el baúl y los puentes del mandatario).
/// </summary>
internal sealed class CompanyTransitOfficeAgreementConfiguration
    : IEntityTypeConfiguration<CompanyTransitOfficeAgreement>
{
    public void Configure(EntityTypeBuilder<CompanyTransitOfficeAgreement> builder)
    {
        builder.ToTable(
            "company_transit_office_agreements", SchemaNames.Admin, t => t.ExcludeFromMigrations());

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasDefaultValueSql("uuidv7()");

        builder.Property(x => x.CompanyTenantId).HasColumnName("company_tenant_id").IsRequired();
        builder.Property(x => x.TransitOfficeId).HasColumnName("transit_office_id").IsRequired();
        builder.Property(x => x.IsActive).HasColumnName("is_active").IsRequired().HasDefaultValue(true);
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.CreatedBy).HasColumnName("created_by");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        builder.Property(x => x.UpdatedBy).HasColumnName("updated_by");

        builder.HasIndex(x => new { x.CompanyTenantId, x.TransitOfficeId })
            .IsUnique()
            .HasDatabaseName("uq_company_transit_office_agreements");
    }
}
