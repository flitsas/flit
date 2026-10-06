using Flit.Infrastructure.Persistence.Entities.Admin;
using Flit.Infrastructure.Persistence.Schemas;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flit.Infrastructure.Persistence.Configurations.Admin;

/// <summary>
/// Mapeo EF Core de <c>admin.mandate_signer_associated_companies</c> (HU #13177). El esquema lo lleva el DDL
/// 126 (migración con DDL embebido), así que va <c>ExcludeFromMigrations</c>, igual que el resto de puentes.
/// </summary>
internal sealed class MandateSignerAssociatedCompanyConfiguration
    : IEntityTypeConfiguration<MandateSignerAssociatedCompany>
{
    public void Configure(EntityTypeBuilder<MandateSignerAssociatedCompany> builder)
    {
        builder.ToTable(
            "mandate_signer_associated_companies", SchemaNames.Admin, t => t.ExcludeFromMigrations());

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasDefaultValueSql("uuidv7()");

        builder.Property(x => x.MandateSignerId).HasColumnName("mandate_signer_id").IsRequired();
        builder.Property(x => x.TransitOfficeId).HasColumnName("transit_office_id").IsRequired();
        builder.Property(x => x.AssociatedCompanyTenantId).HasColumnName("associated_company_tenant_id").IsRequired();
        builder.Property(x => x.IsActive).HasColumnName("is_active").IsRequired().HasDefaultValue(true);
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();

        builder.HasIndex(x => new { x.MandateSignerId, x.TransitOfficeId, x.AssociatedCompanyTenantId })
            .IsUnique()
            .HasDatabaseName("uq_msac_activa")
            .HasFilter("is_active");

        builder.HasIndex(x => new { x.TransitOfficeId, x.AssociatedCompanyTenantId, x.IsActive })
            .HasDatabaseName("ix_msac_office_company");

        // Sin HasOne, EF puede INSERT'ar el puente antes que mandate_signers → 23503 (mismo patrón que los otros puentes).
        builder.HasOne<MandateSigner>()
            .WithMany()
            .HasForeignKey(x => x.MandateSignerId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
