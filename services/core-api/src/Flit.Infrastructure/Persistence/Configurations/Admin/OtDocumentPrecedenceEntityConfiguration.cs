using Flit.Infrastructure.Persistence.Entities.Admin;
using Flit.Infrastructure.Persistence.Schemas;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flit.Infrastructure.Persistence.Configurations.Admin;

internal sealed class OtDocumentPrecedenceEntityConfiguration : IEntityTypeConfiguration<OtDocumentPrecedenceEntity>
{
    public void Configure(EntityTypeBuilder<OtDocumentPrecedenceEntity> builder)
    {
        builder.ToTable("ot_document_precedence", SchemaNames.Admin);

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasDefaultValueSql("uuidv7()");

        builder.Property(x => x.TenantId).IsRequired();
        builder.Property(x => x.ProcedureTypeId).IsRequired();
        builder.Property(x => x.DocumentTypeId).IsRequired();
        builder.Property(x => x.SortOrder).IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();

        builder.HasIndex(x => new { x.TenantId, x.ProcedureTypeId, x.DocumentTypeId })
            .IsUnique()
            .HasDatabaseName("uq_ot_document_precedence");
        builder.HasIndex(x => x.TenantId).HasDatabaseName("ix_ot_document_precedence_tenant_id");
    }
}
