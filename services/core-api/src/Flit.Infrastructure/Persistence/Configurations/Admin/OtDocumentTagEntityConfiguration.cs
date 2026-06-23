using Flit.Infrastructure.Persistence.Entities.Admin;
using Flit.Infrastructure.Persistence.Schemas;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flit.Infrastructure.Persistence.Configurations.Admin;

internal sealed class OtDocumentTagEntityConfiguration : IEntityTypeConfiguration<OtDocumentTagEntity>
{
    public void Configure(EntityTypeBuilder<OtDocumentTagEntity> builder)
    {
        builder.ToTable("ot_document_tags", SchemaNames.Admin);

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasDefaultValueSql("uuidv7()");

        builder.Property(x => x.TenantId).IsRequired();
        builder.Property(x => x.Code).HasMaxLength(50).IsRequired();
        builder.Property(x => x.Name).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Color).HasMaxLength(7).HasDefaultValue("#162744").IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();

        builder.HasIndex(x => new { x.TenantId, x.Code })
            .IsUnique()
            .HasDatabaseName("uq_ot_document_tags_tenant_code");
        builder.HasIndex(x => x.TenantId).HasDatabaseName("ix_ot_document_tags_tenant_id");
    }
}
