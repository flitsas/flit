using System.Text.Json;
using Flit.Infrastructure.Persistence.Entities.Tramites;
using Flit.Infrastructure.Persistence.Schemas;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flit.Infrastructure.Persistence.Configurations.Tramites;

/// <summary>
/// Mapeo de <see cref="DocumentType"/> a <c>tramites.document_types</c> (DDL HU #10155).
/// Los nombres de columna se derivan por la convención snake_case global.
/// </summary>
internal sealed class DocumentTypeConfiguration : IEntityTypeConfiguration<DocumentType>
{
    public void Configure(EntityTypeBuilder<DocumentType> builder)
    {
        // DDL gestionado por migración SQL cruda (HU #10155); la entidad vive en el modelo
        // EF solo para consultas y se excluye del snapshot/migraciones EF.
        builder.ToTable("document_types", SchemaNames.Tramites, t => t.ExcludeFromMigrations());

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasDefaultValueSql("uuidv7()");

        builder.Property(x => x.Code).HasMaxLength(50).IsRequired();
        builder.Property(x => x.Name).HasMaxLength(150).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(500);
        builder.Property(x => x.IsActive).IsRequired().HasDefaultValue(true);
        builder.Property(x => x.CreatedAt).IsRequired();

        // HU #11181 — documentos generados por el sistema (DDL 46-HU11181): la marca y su orden
        // por defecto en el expediente. La marca excluye el tipo del checklist de carga del gestor
        // (sigue asociado para el consolidado y la prelación del OT).
        builder.Property(x => x.IsSystemGenerated)
            .HasColumnName("is_system_generated")
            .IsRequired()
            .HasDefaultValue(false);
        builder.Property(x => x.GeneratedSortOrder).HasColumnName("generated_sort_order");

        // HU #10520 — validación de carga por tipo. La columna es jsonb (DDL HU #10155); se
        // (de)serializa la lista de MIME con un value converter + comparer (colección mutable).
        var mimeComparer = new ValueComparer<List<string>>(
            (a, b) => a!.SequenceEqual(b!),
            v => v.Aggregate(0, (acc, s) => HashCode.Combine(acc, s.GetHashCode(StringComparison.Ordinal))),
            v => v.ToList());
        builder.Property(x => x.MimeTypesAllowed)
            .HasColumnName("mime_types_allowed")
            .HasColumnType("jsonb")
            .HasConversion(
                v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                v => JsonSerializer.Deserialize<List<string>>(v, (JsonSerializerOptions?)null) ?? new List<string>())
            .Metadata.SetValueComparer(mimeComparer);
        builder.Property(x => x.MaxSizeBytes).HasColumnName("max_size_bytes");

        builder.HasIndex(x => x.Code)
            .IsUnique()
            .HasDatabaseName("uq_document_types_code");
    }
}
