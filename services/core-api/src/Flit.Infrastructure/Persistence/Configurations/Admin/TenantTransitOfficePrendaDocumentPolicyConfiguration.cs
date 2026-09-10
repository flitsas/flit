using Flit.Infrastructure.Persistence.Entities.Admin;
using Flit.Infrastructure.Persistence.Schemas;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flit.Infrastructure.Persistence.Configurations.Admin;

internal sealed class TenantTransitOfficePrendaDocumentPolicyConfiguration
    : IEntityTypeConfiguration<TenantTransitOfficePrendaDocumentPolicy>
{
    public void Configure(EntityTypeBuilder<TenantTransitOfficePrendaDocumentPolicy> builder)
    {
        // DDL vía migración SQL embebida; no generar CreateTable en el modelo EF.
        builder.ToTable(
            "tenant_transit_office_prenda_document_policies",
            SchemaNames.Admin,
            t => t.ExcludeFromMigrations());

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasDefaultValueSql("uuidv7()");

        builder.Property(x => x.TenantId).IsRequired();
        builder.Property(x => x.TransitOfficeId).IsRequired();
        builder.Property(x => x.DocumentOptional).IsRequired();
        builder.Property(x => x.RowVersion).HasDefaultValue(0L).IsConcurrencyToken();
        builder.Property(x => x.CreatedAt).IsRequired();

        builder.HasIndex(x => new { x.TenantId, x.TransitOfficeId })
            .IsUnique()
            .HasDatabaseName("uq_tenant_transit_office_prenda_document_policies");
    }
}
