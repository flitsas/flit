using Flit.Infrastructure.Persistence.Entities.Tramites;
using Flit.Infrastructure.Persistence.Schemas;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flit.Infrastructure.Persistence.Configurations.Tramites;

/// <summary>
/// Mapeo de <see cref="ProcedureDocumentRequirement"/> a
/// <c>tramites.procedure_document_requirements</c> (DDL HU #10155). Mapeo mínimo de
/// solo lectura para el guard de integridad del soft-delete (HU #10193, AC6).
/// </summary>
internal sealed class ProcedureDocumentRequirementConfiguration
    : IEntityTypeConfiguration<ProcedureDocumentRequirement>
{
    public void Configure(EntityTypeBuilder<ProcedureDocumentRequirement> builder)
    {
        builder.ToTable("procedure_document_requirements", SchemaNames.Tramites);

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasDefaultValueSql("uuidv7()");

        builder.Property(x => x.ProcedureTypeId).IsRequired();
        builder.Property(x => x.DocumentTypeId).IsRequired();
        builder.Property(x => x.IsMandatory).IsRequired();
        builder.Property(x => x.DefaultSortOrder).IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();

        builder.HasIndex(x => x.DocumentTypeId)
            .HasDatabaseName("ix_procedure_document_requirements_document_type_id");
    }
}
