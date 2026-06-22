using Flit.Tramites.Domain.Entities;
using Flit.Infrastructure.Persistence.Schemas;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flit.Infrastructure.Persistence.Configurations.Tramites;

internal sealed class FormFieldConfiguration : IEntityTypeConfiguration<FormField>
{
    public void Configure(EntityTypeBuilder<FormField> builder)
    {
        builder.ToTable("form_fields", SchemaNames.Tramites);

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasDefaultValueSql("uuidv7()");

        builder.HasIndex(x => new { x.ProcedureSectionId, x.FieldKey })
            .IsUnique()
            .HasDatabaseName("uq_form_fields_section_key");

        builder.Property(x => x.FieldKey).HasMaxLength(80).IsRequired();
        builder.Property(x => x.Label).HasMaxLength(150).IsRequired();
        builder.Property(x => x.FieldType).HasMaxLength(20).IsRequired();
        builder.Property(x => x.LockReason).HasMaxLength(200);
        builder.Property(x => x.Options).HasColumnType("jsonb");
        builder.Property(x => x.ValidationSchema).HasColumnType("jsonb").IsRequired().HasDefaultValueSql("'{}'");
        builder.Property(x => x.RowVersion).HasDefaultValue(0L).IsConcurrencyToken();
        builder.Property(x => x.CreatedAt).IsRequired();

        builder.HasOne(x => x.ConsultationTemplate)
            .WithMany()
            .HasForeignKey(x => x.ConsultationTemplateId)
            .OnDelete(DeleteBehavior.SetNull)
            .HasConstraintName("fk_form_fields_consultation_templates");
    }
}
