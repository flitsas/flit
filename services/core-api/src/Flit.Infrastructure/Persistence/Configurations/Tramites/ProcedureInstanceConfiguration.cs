using Flit.Infrastructure.Persistence.Entities.Tramites;
using Flit.Infrastructure.Persistence.Schemas;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flit.Infrastructure.Persistence.Configurations.Tramites;

/// <summary>
/// Mapeo de <see cref="ProcedureInstance"/> a <c>tramites.procedure_instances</c>
/// (DDL HU #10150). Columnas snake_case derivadas por convención. Número de referencia
/// único por tenant (<c>uq_procedure_instances_reference</c>).
/// </summary>
internal sealed class ProcedureInstanceConfiguration
    : IEntityTypeConfiguration<ProcedureInstance>
{
    public void Configure(EntityTypeBuilder<ProcedureInstance> builder)
    {
        // DDL gestionado por migración SQL cruda (HU #10150); la entidad vive en el modelo
        // EF solo para consultas y se excluye del snapshot/migraciones EF.
        builder.ToTable("procedure_instances", SchemaNames.Tramites, t => t.ExcludeFromMigrations());

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasDefaultValueSql("uuidv7()");

        builder.Property(x => x.TenantId).IsRequired();
        builder.Property(x => x.ProcedureTypeId).IsRequired();
        builder.Property(x => x.ReferenceNumber).HasMaxLength(30).IsRequired();
        builder.Property(x => x.Status).HasMaxLength(20).HasDefaultValue("draft").IsRequired();
        builder.Property(x => x.CreatedByUserId).IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();

        builder.HasIndex(x => new { x.TenantId, x.ReferenceNumber })
            .IsUnique()
            .HasDatabaseName("uq_procedure_instances_reference");
    }
}
