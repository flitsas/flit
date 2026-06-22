using Flit.Tramites.Domain.Entities;
using Flit.Infrastructure.Persistence.Schemas;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flit.Infrastructure.Persistence.Configurations.Tramites;

internal sealed class ProcedureInstanceFieldValueConfiguration : IEntityTypeConfiguration<ProcedureInstanceFieldValue>
{
    public void Configure(EntityTypeBuilder<ProcedureInstanceFieldValue> builder)
    {
        builder.ToTable("procedure_instance_field_values", SchemaNames.Tramites, t =>
        {
            t.HasTrigger("tr_procedure_instance_field_values_audit");
            t.HasTrigger("tr_procedure_instance_field_values_immutable");
        });

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasDefaultValueSql("uuidv7()");

        builder.Property(x => x.FieldKey).HasMaxLength(80).IsRequired();
        builder.Property(x => x.ValueText).HasColumnType("text");
        builder.Property(x => x.ValueJson).HasColumnType("jsonb");
        builder.Property(x => x.Source).HasMaxLength(20).IsRequired().HasDefaultValue("user");
        builder.Property(x => x.CreatedAt).IsRequired();

        // Nullable: valores "loose" derivados de consulta/sistema no se atan a un form_field.
        builder.Property(x => x.FormFieldId).IsRequired(false);

        builder.HasIndex(x => new { x.ProcedureInstanceId, x.FieldKey })
            .IsUnique()
            .HasDatabaseName("uq_procedure_instance_field_values_instance_key");

        builder.HasIndex(x => x.TenantId)
            .HasDatabaseName("ix_procedure_instance_field_values_tenant_id");

        builder.HasOne(x => x.ProcedureInstance)
            .WithMany(x => x.FieldValues)
            .HasForeignKey(x => x.ProcedureInstanceId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("fk_procedure_instance_field_values_procedure_instances");

        builder.HasOne(x => x.FormField)
            .WithMany()
            .HasForeignKey(x => x.FormFieldId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_procedure_instance_field_values_form_fields");
    }
}
