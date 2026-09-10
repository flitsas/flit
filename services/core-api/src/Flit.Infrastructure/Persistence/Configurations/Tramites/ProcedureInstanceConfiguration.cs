using Flit.Tramites.Domain.Entities;
using Flit.Infrastructure.Persistence.Schemas;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
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
            t.HasTrigger("tr_procedure_instances_radicado");
            t.HasTrigger("tr_procedure_instances_radicado_inmutable");
        });

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasDefaultValueSql("uuidv7()");

        // HU #12151 + HU #12371 — el radicado lo asigna Postgres, no la aplicación:
        //   · consecutivo       → bigint, contador GLOBAL de tramites.procedure_instance_reference_seq.
        //   · reference_number  → 'FT1-0000012', compuesto por el trigger BEFORE INSERT
        //                          tr_procedure_instances_radicado a partir de la familia del tipo
        //                          (DDL 108). Ver Radicado en Flit.Tramites.Domain.
        //
        // Para cada una, tres piezas, y las tres hacen falta:
        //  · ValueGeneratedOnAdd        → EF las LEE de vuelta tras el INSERT (RETURNING).
        //  · BeforeSaveBehavior=Ignore  → EF NUNCA las manda en el INSERT. `CreateProcedureInstanceCommand`
        //                                 construye la entidad con ReferenceNumber = string.Empty, y una
        //                                 cadena vacía NO es ausencia de valor para Postgres: sin esto el
        //                                 trigger recibiría '' y el CHECK de formato reventaría.
        //  · AfterSaveBehavior=Ignore   → EF tampoco las manda en el UPDATE: el radicado es inmutable
        //                                 (AC4) y el trigger tr_procedure_instances_radicado_inmutable
        //                                 falla ruidoso ante cualquier intento. Un cambio en memoria se
        //                                 descarta en vez de tumbar el SaveChanges entero.
        //
        // HasDefaultValueSql se declara solo por documentación del modelo: la tabla está
        // ExcludeFromMigrations y el DEFAULT real ya no existe (lo asigna el trigger, que así puede
        // respetar un número explícito de los seeds). Verificado contra una copia de dev.
        builder.Property(x => x.ReferenceNumber)
            .HasMaxLength(30)
            .IsRequired()
            .ValueGeneratedOnAdd();
        builder.Property(x => x.ReferenceNumber).Metadata.SetBeforeSaveBehavior(PropertySaveBehavior.Ignore);
        builder.Property(x => x.ReferenceNumber).Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);

        builder.Property(x => x.Consecutivo)
            .HasColumnName("consecutivo")
            .IsRequired()
            .HasDefaultValueSql("nextval('tramites.procedure_instance_reference_seq')")
            .ValueGeneratedOnAdd();
        builder.Property(x => x.Consecutivo).Metadata.SetBeforeSaveBehavior(PropertySaveBehavior.Ignore);
        builder.Property(x => x.Consecutivo).Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);

        // HU #12371 — el contador es global: el número no se repite entre familias.
        builder.HasIndex(x => x.Consecutivo)
            .IsUnique()
            .HasDatabaseName("uq_procedure_instances_consecutivo");

        // N 03 (ADR-0022): estados de negocio en español (TramiteEstado); default = borrador.
        builder.Property(x => x.Status).HasMaxLength(20).IsRequired().HasDefaultValue("borrador");

        // Rework trámites (Slice 1)
        // ADR-0050 — clasificación derivada del tipo: se calcula desde la navegación
        // ProcedureType, no son columnas.
        builder.Ignore(x => x.Family);
        builder.Ignore(x => x.TypeCode);
        builder.Ignore(x => x.TypeName);
        builder.Ignore(x => x.FamilyCode);

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

        // Subsanación por flag (no estado): editable en rechazado mientras el flag esté activo.
        builder.Property(x => x.SubsanacionActiva)
            .HasColumnName("subsanacion_activa")
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(x => x.SubsanacionCount)
            .HasColumnName("subsanacion_count")
            .IsRequired()
            .HasDefaultValue(0);

        // Baseline del diff de re-radicación. Antes viajaba en el metadata de una fila
        // rechazado→rechazado del historial, que el timeline pintaba como un rechazo repetido.
        builder.Property(x => x.SubsanacionBaseline)
            .HasColumnName("subsanacion_baseline")
            .HasColumnType("jsonb");

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

        // HU #12165 (Feature #12156) — ventana de 1 hora de corrección de placa por el OT (HU
        // #12167). Columnas agregadas por migración SQL cruda (la tabla está ExcludeFromMigrations);
        // aquí solo se mapean para el modelo EF.
        builder.Property(x => x.PlateAssignedAt)
            .HasColumnName("plate_assigned_at");

        builder.Property(x => x.PlateUpdatedAt)
            .HasColumnName("plate_updated_at");

        // Feature #12276 — marca «Confirmado en RUNT» (HU #12312). Columnas agregadas por migración SQL
        // cruda (107-F12276-confirmacion-runt.sql; la tabla está ExcludeFromMigrations); aquí solo se
        // mapean. Es una marca ortogonal al status: la corrida de confirmación nunca lo toca.
        builder.Property(x => x.RuntConfirmedAt)
            .HasColumnName("runt_confirmed_at");

        builder.Property(x => x.RuntAttempts)
            .HasColumnName("runt_attempts")
            .IsRequired()
            .HasDefaultValue(0);

        builder.Property(x => x.RuntFlag)
            .HasColumnName("runt_flag")
            .HasMaxLength(20);

        // Migración V1→V2 — marca de trámite histórico importado (foto de solo lectura). Columna
        // agregada por migración SQL cruda (la tabla está ExcludeFromMigrations); aquí solo se mapea
        // para el modelo EF. Default false = trámite nativo de V2.
        builder.Property(x => x.IsMigrated)
            .HasColumnName("is_migrated")
            .IsRequired()
            .HasDefaultValue(false);

        // ADR-0036 §D9 (HU #10916) — mandatario que firma el mandato, resuelto al aprobar. Columna
        // agregada por migración SQL cruda (la tabla está ExcludeFromMigrations); aquí solo se mapea al
        // modelo EF. La FK a admin.mandate_signers (ON DELETE SET NULL) se declara en el DDL, no en EF
        // (evita que EF intente materializar una relación en una tabla excluida de migraciones).
        builder.Property(x => x.MandateSignerId)
            .HasColumnName("mandate_signer_id");

        builder.HasIndex(x => x.MandateSignerId)
            .HasDatabaseName("ix_procedure_instances_mandate_signer_id")
            .HasFilter("mandate_signer_id IS NOT NULL");

        // HU #12151 — sin tenant_id en la llave. Esa era justo la causa de que dos compañías
        // pudieran compartir radicado (137 filas de dev lo hacían).
        builder.HasIndex(x => x.ReferenceNumber)
            .IsUnique()
            .HasDatabaseName("uq_procedure_instances_reference");

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

        // ICT — pausa del trámite (servicio v1 pauseDraftProcess + bandera starts_procedure_in_paused).
        // Columnas agregadas por 39-ICT-procedure-pause.sql (tabla ExcludeFromMigrations); aquí solo se
        // mapean al modelo EF. is_paused NOT NULL default false; paused_observation nullable varchar(250).
        builder.Property(x => x.IsPaused)
            .HasColumnName("is_paused")
            .IsRequired()
            .HasDefaultValue(false);
        builder.Property(x => x.PausedObservation)
            .HasColumnName("paused_observation")
            .HasMaxLength(250);
        builder.HasIndex(x => new { x.TenantId, x.ExternalRef })
            .IsUnique()
            .HasDatabaseName("uq_procedure_instances_tenant_external_ref")
            .HasFilter("external_ref IS NOT NULL AND deleted_at IS NULL");

        builder.HasIndex(x => new { x.TenantId, x.Status, x.CreatedAt })
            .HasDatabaseName("ix_procedure_instances_tenant_id_status_created_at");

        // Migración TramitesCamposBusqueda — vin/plate/vendedor_nombre/comprador_nombre denormalizados
        // por trigger (ver Ddl/47-tramites-campos-busqueda.sql) para filtrar/ordenar el listado en SQL.
        // Columnas agregadas por migración SQL cruda (la tabla está ExcludeFromMigrations); aquí solo se
        // mapean para el modelo EF. Solo lectura de facto: nada en el aplicativo debería escribirlas
        // directamente (la fuente de verdad sigue siendo FieldValues/Actors).
        builder.Property(x => x.Vin).HasColumnName("vin").HasMaxLength(20);
        builder.Property(x => x.Plate).HasColumnName("plate").HasMaxLength(20);
        builder.Property(x => x.VendedorNombre).HasColumnName("vendedor_nombre").HasMaxLength(200);
        builder.Property(x => x.CompradorNombre).HasColumnName("comprador_nombre").HasMaxLength(200);

        builder.HasIndex(x => new { x.TenantId, x.Vin })
            .HasDatabaseName("ix_procedure_instances_tenant_id_vin");
        builder.HasIndex(x => new { x.TenantId, x.Plate })
            .HasDatabaseName("ix_procedure_instances_tenant_id_plate");
        builder.HasIndex(x => new { x.TenantId, x.CompradorNombre })
            .HasDatabaseName("ix_procedure_instances_tenant_id_comprador_nombre");
        builder.HasIndex(x => new { x.TenantId, x.VendedorNombre })
            .HasDatabaseName("ix_procedure_instances_tenant_id_vendedor_nombre");
        builder.HasIndex(x => new { x.TenantId, x.CreatedAt })
            .HasDatabaseName("ix_procedure_instances_tenant_id_created_at");
        builder.HasIndex(x => new { x.TenantId, x.UpdatedAt })
            .HasDatabaseName("ix_procedure_instances_tenant_id_updated_at");
        builder.HasIndex(x => new { x.TenantId, x.CreatedByUserId })
            .HasDatabaseName("ix_procedure_instances_tenant_id_created_by_user_id");

        // HU #12162 — gestor asignado (reasignable por admin), distinto de CreatedByUserId (quién
        // radicó). Columna agregada por migración SQL cruda (la tabla está ExcludeFromMigrations);
        // aquí solo se mapea para el modelo EF. La FK a identity.users (ON DELETE SET NULL) y el
        // índice parcial se declaran en el DDL, no en EF (tabla excluida de migraciones).
        builder.Property(x => x.AssignedToUserId)
            .HasColumnName("assigned_to_user_id");

        builder.HasIndex(x => new { x.TenantId, x.AssignedToUserId })
            .HasDatabaseName("ix_procedure_instances_tenant_id_assigned_to_user_id")
            .HasFilter("assigned_to_user_id IS NOT NULL");

        builder.Property(x => x.RowVersion).HasDefaultValue(0L).IsConcurrencyToken();
        builder.Property(x => x.CreatedAt).IsRequired();

        builder.HasOne(x => x.ProcedureType)
            .WithMany()
            .HasForeignKey(x => x.ProcedureTypeId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_procedure_instances_procedure_types");
    }
}
