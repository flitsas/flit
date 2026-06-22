using Flit.Tramites.Domain.Entities;
using Flit.Infrastructure.Persistence.Schemas;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flit.Infrastructure.Persistence.Configurations.Tramites;

internal sealed class ConsultationTemplateConfiguration : IEntityTypeConfiguration<ConsultationTemplate>
{
    public void Configure(EntityTypeBuilder<ConsultationTemplate> builder)
    {
        builder.ToTable("consultation_templates", SchemaNames.Tramites);

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasDefaultValueSql("uuidv7()");

        builder.Property(x => x.Code).HasMaxLength(50).IsRequired();
        builder.HasIndex(x => x.Code).IsUnique().HasDatabaseName("uq_consultation_templates_code");

        builder.Property(x => x.Name).HasMaxLength(150).IsRequired();
        builder.Property(x => x.EntityScope).HasMaxLength(30).IsRequired();
        builder.Property(x => x.PersonType).HasMaxLength(20);
        builder.Property(x => x.RequiredFieldKeys).HasColumnType("jsonb").IsRequired().HasDefaultValueSql("'[]'");
        builder.Property(x => x.RequestSchema).HasColumnType("jsonb").IsRequired().HasDefaultValueSql("'{}'");
        builder.Property(x => x.ExternalRefs).HasColumnType("jsonb").IsRequired().HasDefaultValueSql("'{}'");
        builder.Property(x => x.RowVersion).HasDefaultValue(0L).IsConcurrencyToken();
        builder.Property(x => x.CreatedAt).IsRequired();

        builder.HasIndex(x => x.ExternalDataSourceId)
            .HasDatabaseName("ix_consultation_templates_external_data_source_id");

        builder.HasOne(x => x.ExternalDataSource)
            .WithMany()
            .HasForeignKey(x => x.ExternalDataSourceId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
