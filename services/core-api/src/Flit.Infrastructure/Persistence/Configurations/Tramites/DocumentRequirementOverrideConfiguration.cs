using Flit.Infrastructure.Persistence.Entities.Tramites;
using Flit.Infrastructure.Persistence.Schemas;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flit.Infrastructure.Persistence.Configurations.Tramites;

/// <summary>
/// Mapeo de <see cref="DocumentRequirementOverride"/> a
/// <c>tramites.document_requirement_overrides</c> (DDL HU #10198). Las columnas snake_case
/// (<c>transit_office_id</c>, <c>requirement_state</c>) las deriva la convención de nombres.
/// Índice único de la tupla (trámite, documento, OT): un documento no se personaliza dos
/// veces para el mismo OT.
/// </summary>
internal sealed class DocumentRequirementOverrideConfiguration
    : IEntityTypeConfiguration<DocumentRequirementOverride>
{
    public void Configure(EntityTypeBuilder<DocumentRequirementOverride> builder)
    {
        // DDL gestionado por migración SQL cruda (HU #10198); la entidad vive en el modelo
        // EF solo para consultas y se excluye del snapshot/migraciones EF.
        builder.ToTable("document_requirement_overrides", SchemaNames.Tramites, t => t.ExcludeFromMigrations());

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasDefaultValueSql("uuidv7()");

        builder.Property(x => x.ProcedureTypeId).IsRequired();
        builder.Property(x => x.DocumentTypeId).IsRequired();
        builder.Property(x => x.TransitOfficeId).IsRequired();
        builder.Property(x => x.RequirementState).HasMaxLength(20).IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();

        builder.HasIndex(x => new { x.ProcedureTypeId, x.DocumentTypeId, x.TransitOfficeId })
            .IsUnique()
            .HasDatabaseName("uq_document_requirement_overrides");
    }
}
