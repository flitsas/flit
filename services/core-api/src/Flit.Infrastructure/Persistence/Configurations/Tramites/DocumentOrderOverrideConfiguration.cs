using Flit.Infrastructure.Persistence.Entities.Tramites;
using Flit.Infrastructure.Persistence.Schemas;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flit.Infrastructure.Persistence.Configurations.Tramites;

/// <summary>
/// Mapeo de <see cref="DocumentOrderOverride"/> a <c>tramites.document_order_overrides</c>
/// (DDL HU #10155). Las columnas snake_case (<c>scope_type</c>, <c>scope_ref_id</c>,
/// <c>sort_order</c>) las deriva la convención de nombres. Índice único de la tupla
/// (trámite, documento, scope, referencia) — un documento no se personaliza dos veces para
/// el mismo ámbito (HU #10196).
/// </summary>
internal sealed class DocumentOrderOverrideConfiguration
    : IEntityTypeConfiguration<DocumentOrderOverride>
{
    public void Configure(EntityTypeBuilder<DocumentOrderOverride> builder)
    {
        builder.ToTable("document_order_overrides", SchemaNames.Tramites);

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasDefaultValueSql("uuidv7()");

        builder.Property(x => x.ProcedureTypeId).IsRequired();
        builder.Property(x => x.DocumentTypeId).IsRequired();
        builder.Property(x => x.ScopeType).HasMaxLength(20).IsRequired();
        builder.Property(x => x.SortOrder).IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();

        builder.HasIndex(x => new { x.ProcedureTypeId, x.DocumentTypeId, x.ScopeType, x.ScopeRefId })
            .IsUnique()
            .HasDatabaseName("uq_document_order_overrides");
    }
}
