using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations;

/// <summary>
/// HU #13074 (Feature #13062, Épica #12737) — los cambios en actores, campos, historial, adjuntos y
/// datos comerciales mueven la versión de sincronización del trámite (una vez por transacción), sin
/// subir <c>row_version</c> ni auditar el trámite por ello, y el histórico recibe versión en orden de
/// creación. DDL: <c>123-HU13074-sync-propagacion-hijas.sql</c>.
/// </summary>
[DbContext(typeof(FlitDbContext))]
[Migration("20260929130000_HU13074_SyncPropagacionHijas")]
public partial class HU13074_SyncPropagacionHijas : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(EmbeddedDdl.LoadUp("123-HU13074-sync-propagacion-hijas.sql"));

    /// <inheritdoc />
    /// <remarks>
    /// Devuelve los triggers del trámite y la función de sellado al estado del DDL 122. Las versiones
    /// asignadas al histórico se conservan: no hay un valor anterior al que volver que tenga sentido.
    /// </remarks>
    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(
            """
            DO $$
            DECLARE
                t text;
            BEGIN
                FOREACH t IN ARRAY ARRAY[
                    'procedure_instance_actors',
                    'procedure_instance_field_values',
                    'procedure_instance_status_history',
                    'procedure_instance_attachments',
                    'procedure_instance_commercial']
                LOOP
                    EXECUTE format('DROP TRIGGER IF EXISTS tr_%s_sync_touch_ins ON tramites.%I', t, t);
                    EXECUTE format('DROP TRIGGER IF EXISTS tr_%s_sync_touch_upd ON tramites.%I', t, t);
                    EXECUTE format('DROP TRIGGER IF EXISTS tr_%s_sync_touch_del ON tramites.%I', t, t);
                END LOOP;
            END $$;
            DROP FUNCTION IF EXISTS tramites.trg_procedure_child_sync_touch();

            DROP TRIGGER IF EXISTS tr_procedure_instances_row_version ON tramites.procedure_instances;
            CREATE TRIGGER tr_procedure_instances_row_version BEFORE UPDATE ON tramites.procedure_instances
                FOR EACH ROW EXECUTE FUNCTION public.trg_row_version();

            DROP TRIGGER IF EXISTS tr_procedure_instances_audit_update ON tramites.procedure_instances;
            DROP TRIGGER IF EXISTS tr_procedure_instances_audit ON tramites.procedure_instances;
            CREATE TRIGGER tr_procedure_instances_audit AFTER INSERT OR UPDATE OR DELETE ON tramites.procedure_instances
                FOR EACH ROW EXECUTE FUNCTION public.trg_audit_log();

            DROP FUNCTION IF EXISTS tramites.fn_procedure_instance_solo_sync(tramites.procedure_instances, tramites.procedure_instances);

            CREATE OR REPLACE FUNCTION tramites.trg_procedure_sync_stamp() RETURNS trigger AS $fn$
            BEGIN
                NEW.sync_version    := nextval('tramites.procedure_sync_seq');
                NEW.sync_changed_at := now();
                RETURN NEW;
            END;
            $fn$ LANGUAGE plpgsql;

            ALTER TABLE tramites.procedure_instances DROP COLUMN IF EXISTS sync_xact;
            """);
}
