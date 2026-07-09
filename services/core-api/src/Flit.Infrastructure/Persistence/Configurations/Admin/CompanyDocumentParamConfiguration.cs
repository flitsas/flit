using Flit.Infrastructure.Persistence.Entities.Admin;
using Flit.Infrastructure.Persistence.Schemas;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flit.Infrastructure.Persistence.Configurations.Admin;

/// <summary>Mapeo de <see cref="CompanyDocumentParamEntity"/> a <c>admin.company_document_params</c> (HU #10521).</summary>
internal sealed class CompanyDocumentParamConfiguration : IEntityTypeConfiguration<CompanyDocumentParamEntity>
{
    public void Configure(EntityTypeBuilder<CompanyDocumentParamEntity> builder)
    {
        // DDL gestionado por migración SQL cruda (24-HU10521); la entidad se excluye del snapshot EF.
        builder.ToTable("company_document_params", SchemaNames.Admin, t => t.ExcludeFromMigrations());

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasDefaultValueSql("uuidv7()");
        builder.Property(x => x.TenantId).IsRequired();
        builder.Property(x => x.DocumentTypeCode).HasMaxLength(50).IsRequired();
        builder.Property(x => x.State).HasMaxLength(20).IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();

        builder.HasIndex(x => new { x.TenantId, x.DocumentTypeCode })
            .IsUnique()
            .HasDatabaseName("uq_company_document_params");
    }
}
