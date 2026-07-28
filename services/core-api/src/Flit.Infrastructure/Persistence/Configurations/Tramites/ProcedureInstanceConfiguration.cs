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

        // HU #10879 — paso actual persistido del wizard (autosave del avance). Columna agregada por
        // migración SQL cruda (la tabla está ExcludeFromMigrations); aquí solo se mapea al modelo EF.
        builder.Property(x => x.CurrentStep)
            .HasColumnName("current_step")
            .HasMaxLength(40);

        // HU #10536 — prioritario. Columna agregada por migración SQL cruda (la tabla está
        // ExcludeFromMigrations); aquí solo se mapea para el modelo EF. Índice que sostiene el
        // ordenamiento con primacía de los listados (prioritarios primero, luego por fecha).
        builder.Property(x => x.Prioritario)
            .HasColumnName("prioritario")
            .IsRequired()
            .HasDefaultValue(false);

        builder.HasIndex(x => new { x.TenantId, x.Prioritario, x.CreatedAt })
            .HasDatabaseName("ix_procedure_instances_prioritario");

        // Feature #10701 — vigencia del consolidado maestro. Columna agregada por migración SQL
        // cruda (la tabla está ExcludeFromMigrations); aquí solo se mapea para el modelo EF. La baja
        // a false cualquier transición de estado o el adjuntar la LT; la sube a true la generación.
        builder.Property(x => x.ConsolidadoMaestroVigente)
            .HasColumnName("consolidado_maestro_vigente")
            .IsRequired()
            .HasDefaultValue(false);

        // HU #10860 (Feature #10852, ADR-0032) — vigencia del expediente derivado del wizard, espejo
        // de consolidado_maestro_vigente. Columna agregada por migración SQL cruda (tabla
        // ExcludeFromMigrations); aquí solo se mapea. La baja a false cualquier transición de estado,
        // la decisión del OT o el adjuntar la LT; la sube a true la generación del consolidado.
        builder.Property(x => x.ConsolidadoWizardVigente)
            .HasColumnName("consolidado_wizard_vigente")
            .IsRequired()
            .HasDefaultValue(false);

        // Feature #10587 / HU #10785 — sub-estado interno de placa, ortogonal al status global
        // (que permanece en 'entregado'). Columna agregada por migración SQL cruda (la tabla está
        // ExcludeFromMigrations); aquí solo se mapea para el modelo EF. Nullable: null = sin ruta de placa.
        builder.Property(x => x.PlateFlowStatus)
            .HasColumnName("plate_flow_status")
            .HasMaxLength(20);

        // ADR-0036 §D9 (HU #10916) — mandatario que firma el mandato, resuelto al aprobar. Columna
        // agregada por migración SQL cruda (la tabla está ExcludeFromMigrations); aquí solo se mapea al
        // modelo EF. La FK a admin.mandate_signers (ON DELETE SET NULL) se declara en el DDL, no en EF
        // (evita que EF intente materializar una relación en una tabla excluida de migraciones).
        builder.Property(x => x.MandateSignerId)
            .HasColumnName("mandate_signer_id");

        builder.HasIndex(x => x.MandateSignerId)
            .HasDatabaseName("ix_procedure_instances_mandate_signer_id")
            .HasFilter("mandate_signer_id IS NOT NULL");

        builder.HasIndex(x => new { x.TenantId, x.ReferenceNumber })
            .IsUnique()
            .HasDatabaseName("uq_procedure_instances_tenant_reference");

        // ICT — origen/referencia externa para materialización idempotente. Columnas agregadas por
        // migración SQL cruda (la tabla está ExcludeFromMigrations); aquí solo se mapean para el modelo
        // EF. El índice único parcial (creado en 40-ICT-procedure-external-ref.sql) impide dos borradores
        // para el mismo pre-trámite; aquí se declara para que EF conozca el modelo (no emite DDL).
        builder.Property(x => x.Origin)
            .HasColumnName("origin")
            .HasMaxLength(20);
        builder.Property(x => x.ExternalRef)
            .HasColumnName("external_ref")
            .HasMaxLength(64);
        builder.HasIndex(x => new { x.TenantId, x.ExternalRef })
            .IsUnique()
            .HasDatabaseName("uq_procedure_instances_tenant_external_ref")
            .HasFilter("external_ref IS NOT NULL AND deleted_at IS NULL");

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
