using Flit.Infrastructure.Persistence.Entities.Admin;
using Flit.Infrastructure.Persistence.Schemas;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flit.Infrastructure.Persistence.Configurations.Admin;

/// <summary>
/// Mapeo EF Core del puente mandatario ↔ empresa representada por organismo. El esquema lo lleva el DDL
/// crudo (<c>54-mandatario-empresas-representadas.sql</c>), así que va <c>ExcludeFromMigrations</c>,
/// mismo patrón que el resto de puentes del mandatario.
/// </summary>
internal sealed class MandateSignerRepresentedCompanyConfiguration
    : IEntityTypeConfiguration<MandateSignerRepresentedCompany>
{
    public void Configure(EntityTypeBuilder<MandateSignerRepresentedCompany> builder)
    {
        builder.ToTable(
            "mandate_signer_represented_companies", SchemaNames.Admin, t => t.ExcludeFromMigrations());

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasDefaultValueSql("uuidv7()");

        builder.Property(x => x.MandateSignerId).HasColumnName("mandate_signer_id").IsRequired();
        builder.Property(x => x.TransitOfficeId).HasColumnName("transit_office_id").IsRequired();
        builder.Property(x => x.RepresentedCompanyId).HasColumnName("represented_company_id").IsRequired();
        builder.Property(x => x.IsActive).HasColumnName("is_active").IsRequired().HasDefaultValue(true);
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();

        builder.HasIndex(x => new { x.MandateSignerId, x.TransitOfficeId, x.RepresentedCompanyId })
            .IsUnique()
            .HasDatabaseName("uq_msrc_activa")
            .HasFilter("is_active");

        builder.HasIndex(x => new { x.TransitOfficeId, x.RepresentedCompanyId, x.IsActive })
            .HasDatabaseName("ix_msrc_office_company");
    }
}
