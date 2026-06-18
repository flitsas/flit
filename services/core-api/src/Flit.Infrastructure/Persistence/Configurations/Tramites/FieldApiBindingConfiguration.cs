using Flit.Tramites.Domain.Entities;
using Flit.Infrastructure.Persistence.Schemas;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flit.Infrastructure.Persistence.Configurations.Tramites;

internal sealed class FieldApiBindingConfiguration : IEntityTypeConfiguration<FieldApiBinding>
{
    public void Configure(EntityTypeBuilder<FieldApiBinding> builder)
    {
        builder.ToTable("field_api_bindings", SchemaNames.Tramites);

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasDefaultValueSql("uuidv7()");

        builder.Property(x => x.TriggerEvent).HasMaxLength(20).IsRequired();
        builder.Property(x => x.RequestMapping).HasColumnType("jsonb").IsRequired().HasDefaultValueSql("'{}'");
        builder.Property(x => x.ResponseMapping).HasColumnType("jsonb").IsRequired().HasDefaultValueSql("'{}'");
        builder.Property(x => x.CreatedAt).IsRequired();

        builder.HasIndex(x => x.FormFieldId).HasDatabaseName("ix_field_api_bindings_form_field_id");

        builder.HasOne(x => x.FormField)
            .WithMany()
            .HasForeignKey(x => x.FormFieldId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.ExternalDataSource)
            .WithMany()
            .HasForeignKey(x => x.ExternalDataSourceId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
