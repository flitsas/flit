using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations;

/// <summary>
/// HU #12599 (Feature #12595, ADR-0059) — migración de datos del sub-estado <c>plate_flow_status</c> a
/// los estados de negocio <c>preasignacion</c> / <c>asignado</c> (<c>terminado</c> → <c>entregado</c>),
/// con fila sintética de historial (<c>metadata.motivo = migracion_plate_flow_a_status</c>); retira el
/// trigger de autoset del sub-estado y reescribe el trigger de inmutabilidad de <c>field_values</c> para
/// que decida por <c>status</c>. Idempotente. La columna NO se borra aquí (HU #12603).
/// DDL: <c>116-HU12599-plate-flow-a-status.sql</c>.
/// </summary>
[DbContext(typeof(FlitDbContext))]
[Migration("20260916130000_HU12599_PlateFlowToStatus")]
public partial class HU12599_PlateFlowToStatus : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(EmbeddedDdl.LoadUp("116-HU12599-plate-flow-a-status.sql"));

    /// <inheritdoc />
    /// <remarks>
    /// Best-effort documentado: devuelve <c>preasignacion</c>/<c>asignado</c> a <c>entregado</c> + sub-estado,
    /// borra las filas sintéticas de historial y restaura las versiones previas de los dos triggers
    /// (106-HU12167 y 79-tipo-tramite-barrera-y-familia). Lo que no se recupera: un trámite que ya pasó
    /// por «Enviar al OT» (<c>asignado → entregado</c>) queda como <c>entregado + NULL</c>, no
    /// <c>terminado</c>.
    /// </remarks>
    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(
            """
            SET LOCAL row_security = off;

            -- Reverse map (best-effort): los estados nuevos vuelven a 'entregado' + sub-estado. Un trámite que
            -- pasó por 'asignado → entregado' («Enviar al OT») queda como entregado + NULL: el modelo viejo
            -- lo habría tenido en 'terminado', pero ese dato no es recuperable sin adivinar.
            UPDATE tramites.procedure_instances
               SET plate_flow_status = CASE status WHEN 'preasignacion' THEN 'preasignado' ELSE 'asignado' END,
                   status = 'entregado'
             WHERE status IN ('preasignacion', 'asignado');

            -- Las filas sintéticas de historial se retiran: describen un movimiento que ya no existe.
            DELETE FROM tramites.procedure_instance_status_history
             WHERE metadata ->> 'motivo' = 'migracion_plate_flow_a_status';

            -- Inmutabilidad de field_values: versión previa (106-HU12167), gobernada por plate_flow_status.
            CREATE OR REPLACE FUNCTION tramites.trg_field_value_immutable() RETURNS trigger AS $$
            DECLARE v_status varchar(20);
            DECLARE v_plate varchar(20);
            DECLARE v_key varchar(80);
            BEGIN
              SELECT status, plate_flow_status INTO v_status, v_plate FROM tramites.procedure_instances
                WHERE id = COALESCE(NEW.procedure_instance_id, OLD.procedure_instance_id);
              IF v_status IS NULL THEN
                RETURN OLD;
              END IF;
              IF v_status = 'borrador' THEN
                RETURN COALESCE(NEW, OLD);
              END IF;
              -- Subsanación activa sobre rechazado: edición de datos permitida.
              IF v_status = 'rechazado' THEN
                IF EXISTS (
                  SELECT 1 FROM tramites.procedure_instances
                   WHERE id = COALESCE(NEW.procedure_instance_id, OLD.procedure_instance_id)
                     AND subsanacion_activa IS TRUE
                ) THEN
                  RETURN COALESCE(NEW, OLD);
                END IF;
              END IF;
              v_key := COALESCE(NEW.field_key, OLD.field_key);
              IF v_plate = 'preasignado' AND v_key = 'plate' THEN
                RETURN COALESCE(NEW, OLD);
              END IF;
              -- HU #12167 — 'plate' se agrega aquí: la corrección dentro de la ventana de 1 hora escribe el
              -- field_value con el sub-estado ya en 'asignado' (a diferencia de la asignación inicial, que
              -- ocurre en 'preasignado' y ya estaba cubierta arriba).
              IF v_plate = 'asignado' AND v_key IN ('plate', 'soat_estado', 'soat_pagado', 'impuesto_departamental_pagado') THEN
                RETURN COALESCE(NEW, OLD);
              END IF;
              RAISE EXCEPTION 'procedure_instance_field_values son inmutables cuando la instancia está en estado % (solo borrador permite cambios)', v_status
                USING ERRCODE = 'check_violation';
            END; $$ LANGUAGE plpgsql;

            -- Autoset del sub-estado (versión previa: 79-tipo-tramite-barrera-y-familia).
            CREATE OR REPLACE FUNCTION tramites.trg_autoset_plate_flow_status() RETURNS trigger AS $$
            DECLARE
              has_plate boolean;
              skip_gestor boolean;
              requires_plate boolean;
            BEGIN
              IF NEW.status = 'entregado'
                 AND OLD.status IS DISTINCT FROM 'entregado'
                 AND NEW.plate_flow_status IS NULL
              THEN
                SELECT COALESCE(
                         (SELECT (s.snapshot -> 'gateProfile' ->> 'requiresPlateRequest')::boolean
                            FROM tramites.procedure_type_snapshots s
                           WHERE s.procedure_instance_id = NEW.id),
                         (SELECT (pt.gate_profile ->> 'requiresPlateRequest')::boolean
                            FROM tramites.procedure_types pt
                           WHERE pt.id = NEW.procedure_type_id),
                         false)
                  INTO requires_plate;

                IF requires_plate THEN
                  has_plate := EXISTS (
                    SELECT 1 FROM tramites.procedure_instance_field_values f
                     WHERE f.procedure_instance_id = NEW.id
                       AND f.field_key = 'plate'
                       AND COALESCE(btrim(f.value_text), '') <> '');

                  IF has_plate THEN
                    SELECT COALESCE(p.plate_flow_skip_to_terminado, false)
                      INTO skip_gestor
                      FROM admin.tenant_operational_policies p
                     WHERE p.tenant_id = NEW.tenant_id;

                    NEW.plate_flow_status := CASE WHEN skip_gestor THEN 'terminado' ELSE 'asignado' END;
                  ELSIF EXISTS (
                        SELECT 1 FROM tramites.procedure_instance_field_values f
                         WHERE f.procedure_instance_id = NEW.id
                           AND f.field_key = 'plate_route_active'
                           AND lower(btrim(f.value_text)) = 'true')
                  THEN
                    NEW.plate_flow_status := 'preasignado';
                  END IF;
                END IF;
              END IF;
              RETURN NEW;
            END; $$ LANGUAGE plpgsql;

            CREATE TRIGGER tr_procedure_instances_autoset_plate_flow
              BEFORE UPDATE ON tramites.procedure_instances
              FOR EACH ROW EXECUTE FUNCTION tramites.trg_autoset_plate_flow_status();
            """);
}
