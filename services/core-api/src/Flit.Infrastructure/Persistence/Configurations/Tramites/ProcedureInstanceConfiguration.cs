using Flit.Tramites.Domain.Entities;
using Flit.Infrastructure.Persistence.Schemas;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flit.Infrastructure.Persistence.Configurations.Tramites;

internal sealed class ProcedureInstanceConfiguration : IEntityTypeConfiguration<ProcedureInstance>
{
    public void Configure(EntityTypeBuilder<ProcedureInstance> builder)
    {
        // DDL gestionado por migración SQL cruda (HU #10150); la entidad se excluye del
        // snapshot/migraciones EF (evita drift y el 502 de arranque). Declaramos además los
        // triggers porque el rework escribe la instancia con row_version como concurrency
        // token: EF necesita conocerlos para emitir UPDATE compatible con triggers.
        builder.ToTable("procedure_instances", SchemaNames.Tramites, t =>
        {
            t.ExcludeFromMigrations();
            t.HasTrigger("tr_procedure_instances_audit");
            t.HasTrigger("tr_procedure_instances_row_version");
        });

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasDefaultValueSql("uuidv7()");

        builder.Property(x => x.ReferenceNumber).HasMaxLength(30).IsRequired();
        // N 03 (ADR-0022): estados de negocio en español (TramiteEstado); default = borrador.
        builder.Property(x => x.Status).HasMaxLength(20).IsRequired().HasDefaultValue("borrador");

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

        // HU #10349 — borrador finalizado (fase 2). Columna agregada por migración SQL cruda
        // (la tabla está ExcludeFromMigrations); aquí solo se mapea para el modelo EF.
        builder.Property(x => x.DraftFinalizedAt)
            .HasColumnName("draft_finalized_at");

        builder.HasIndex(x => new { x.TenantId, x.DraftFinalizedAt })
            .HasDatabaseName("ix_procedure_instances_draft_finalized")
            .HasFilter("status = 'borrador' AND draft_finalized_at IS NOT NULL");

        // HU #10536 — prioritario. Columna agregada por migración SQL cruda (la tabla está
        // ExcludeFromMigrations); aquí solo se mapea para el modelo EF. Índice que sostiene el
        // ordenamiento con primacía de los listados (prioritarios primero, luego por fecha).
        builder.Property(x => x.Prioritario)
            .HasColumnName("prioritario")
            .IsRequired()
            .HasDefaultValue(false);

        builder.HasIndex(x => new { x.TenantId, x.Prioritario, x.CreatedAt })
            .HasDatabaseName("ix_procedure_instances_prioritario");

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
