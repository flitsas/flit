using Flit.Ict.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flit.Ict.Infrastructure.Persistence.Configurations;

internal sealed class AllowedDocumentConfiguration : IEntityTypeConfiguration<AllowedDocument>
{
    public void Configure(EntityTypeBuilder<AllowedDocument> builder)
    {
        builder.ToTable("external_integration_allowed_documents", SchemaNames.Ict, t => t.ExcludeFromMigrations());
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
    }
}

internal sealed class ConfigurationDocumentConfiguration : IEntityTypeConfiguration<ConfigurationDocument>
{
    public void Configure(EntityTypeBuilder<ConfigurationDocument> builder)
    {
        builder.ToTable("external_integration_configuration_documents", SchemaNames.Ict, t => t.ExcludeFromMigrations());
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => x.ExternalTransactionType);
    }
}

internal sealed class TransactionAttachmentConfiguration : IEntityTypeConfiguration<TransactionAttachment>
{
    public void Configure(EntityTypeBuilder<TransactionAttachment> builder)
    {
        builder.ToTable("external_integration_transaction_attachments", SchemaNames.Ict, t => t.ExcludeFromMigrations());
        builder.HasKey(x => x.Id);
        builder.Property(x => x.RowVersion).IsConcurrencyToken().HasDefaultValue(0L);
        builder.HasIndex(x => x.MasterId);
    }
}
