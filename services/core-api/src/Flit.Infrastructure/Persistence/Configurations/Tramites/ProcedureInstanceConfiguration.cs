using Flit.Tramites.Domain.Entities;
using Flit.Infrastructure.Persistence.Schemas;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flit.Infrastructure.Persistence.Configurations.Tramites;

internal sealed class ProcedureInstanceConfiguration : IEntityTypeConfiguration<ProcedureInstance>
{
    public void Configure(EntityTypeBuilder<ProcedureInstance> builder)
    {
        builder.ToTable("procedure_instances", SchemaNames.Tramites, t =>
        {
            t.HasTrigger("tr_procedure_instances_audit");
            t.HasTrigger("tr_procedure_instances_row_version");
        });

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasDefaultValueSql("uuidv7()");

        builder.Property(x => x.ReferenceNumber).HasMaxLength(30).IsRequired();
        builder.Property(x => x.Status).HasMaxLength(20).IsRequired().HasDefaultValue("draft");

        // Rework trámites (Slice 1)
        builder.Property(x => x.ModalidadEntrada)
            .HasColumnName("modalidad_entrada")
            .HasMaxLength(20).IsRequired().HasDefaultValue("matricula_inicial");
        builder.Property(x => x.TipologiaCodigo)
            .HasColumnName("tipologia_codigo")
            .HasMaxLength(40);
        builder.Property(x => x.ChecklistEstado)
            .HasColumnName("checklist_estado")
            .HasColumnType("jsonb").IsRequired().HasDefaultValueSql("'{}'");

        builder.HasIndex(x => new { x.TenantId, x.ReferenceNumber })
            .IsUnique()
            .HasDatabaseName("uq_procedure_instances_tenant_reference");

        builder.HasIndex(x => new { x.TenantId, x.Status, x.CreatedAt })
            .HasDatabaseName("ix_procedure_instances_tenant_id_status_created_at");

        builder.Property(x => x.RowVersion).HasDefaultValue(0L).IsConcurrencyToken();
        builder.Property(x => x.CreatedAt).IsRequired();

        builder.HasOne(x => x.ProcedureType)
            .WithMany()
            .HasForeignKey(x => x.ProcedureTypeId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_procedure_instances_procedure_types");
    }
}
