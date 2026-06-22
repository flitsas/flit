using Flit.Infrastructure.Persistence.Entities.Tramites;
using Flit.Infrastructure.Persistence.Schemas;
using Flit.Tramites.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flit.Infrastructure.Persistence.Configurations.Tramites;

/// <summary>
/// Mapeo de <see cref="ProcedureDocumentSnapshot"/> a
/// <c>tramites.procedure_document_snapshots</c> (DDL HU #10155, RF19). El snapshot se
/// almacena en una columna <c>jsonb</c>; la relación con el trámite es 1:1
/// (<c>procedure_instance_id</c> único). El registro es inmutable (HU #10197).
/// </summary>
internal sealed class ProcedureDocumentSnapshotConfiguration
    : IEntityTypeConfiguration<ProcedureDocumentSnapshot>
{
    public void Configure(EntityTypeBuilder<ProcedureDocumentSnapshot> builder)
    {
        // DDL gestionado por migración SQL cruda (HU #10155); la entidad vive en el modelo
        // EF solo para consultas y se excluye del snapshot/migraciones EF.
        builder.ToTable("procedure_document_snapshots", SchemaNames.Tramites, t => t.ExcludeFromMigrations());

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasDefaultValueSql("uuidv7()");

        builder.Property(x => x.TenantId).IsRequired();
        builder.Property(x => x.ProcedureInstanceId).IsRequired();
        builder.Property(x => x.Snapshot).HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.SnapshotAt).IsRequired();

        builder.HasIndex(x => x.ProcedureInstanceId)
            .IsUnique()
            .HasDatabaseName("uq_procedure_document_snapshots_instance");

        // Relación con el trámite (FK procedure_instance_id → procedure_instances.id,
        // ON DELETE CASCADE en BD). Sin mapearla, EF Core desconoce la dependencia
        // padre→hijo y no garantiza el orden de inserción: contra PostgreSQL real
        // intenta insertar el snapshot antes que la instancia y viola el FK (HU #10197).
        // El índice único de arriba mantiene la cardinalidad 1:1.
        builder.HasOne<ProcedureInstance>()
            .WithMany()
            .HasForeignKey(x => x.ProcedureInstanceId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
