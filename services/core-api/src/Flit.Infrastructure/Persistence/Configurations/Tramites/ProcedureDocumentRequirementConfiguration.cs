using Flit.Infrastructure.Persistence.Entities.Tramites;
using Flit.Infrastructure.Persistence.Schemas;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flit.Infrastructure.Persistence.Configurations.Tramites;

/// <summary>
/// Mapeo de <see cref="ProcedureDocumentRequirement"/> a
/// <c>tramites.procedure_document_requirements</c> (DDL HU #10155). Originalmente de
/// solo lectura para el guard del soft-delete (HU #10193, AC6); extendido en la
/// HU #10195 para el CRUD de asociaciones (unicidad del par trámite ↔ documento).
/// </summary>
internal sealed class ProcedureDocumentRequirementConfiguration
    : IEntityTypeConfiguration<ProcedureDocumentRequirement>
{
    public void Configure(EntityTypeBuilder<ProcedureDocumentRequirement> builder)
    {
        // DDL gestionado por migración SQL cruda (HU #10155); la entidad vive en el modelo
        // EF solo para consultas y se excluye del snapshot/migraciones EF.
        builder.ToTable("procedure_document_requirements", SchemaNames.Tramites, t => t.ExcludeFromMigrations());

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasDefaultValueSql("uuidv7()");

        builder.Property(x => x.ProcedureTypeId).IsRequired();
        builder.Property(x => x.DocumentTypeId).IsRequired();
        builder.Property(x => x.IsMandatory).IsRequired();
        builder.Property(x => x.DefaultSortOrder).IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();

        builder.HasIndex(x => x.DocumentTypeId)
            .HasDatabaseName("ix_procedure_document_requirements_document_type_id");

        // Unicidad del par (trámite, documento) — un documento no se asocia dos veces
        // al mismo tipo de trámite (HU #10195).
        builder.HasIndex(x => new { x.ProcedureTypeId, x.DocumentTypeId })
            .IsUnique()
            .HasDatabaseName("uq_procedure_document_requirements");
    }
}
