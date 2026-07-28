using Flit.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// HU #10870 (AC1) — habilita el estado <c>subsanacion</c> para reabrir la edición de un trámite
    /// entregado/rechazado SIN volver a <c>borrador</c>. Corrige un hallazgo de db-schema-validator
    /// (modo C, BLOCKED): la versión VIGENTE de <c>trg_field_value_immutable</c> NO es la de
    /// N03_EstadosNegocioTramite (que solo miraba <c>status = 'borrador'</c>); evolucionó en
    /// <c>20260716154530_HU10785_PlateFlowStatus.cs</c> (Feature #10587), que gobierna las
    /// excepciones de escritura de <c>plate</c> (cuando <c>plate_flow_status = 'preasignado'</c>) y
    /// <c>soat_estado</c> (cuando <c>plate_flow_status = 'asignado'</c>) por el SUB-ESTADO interno de
    /// placa, porque en esa ruta <c>status</c> queda fijo en <c>'entregado'</c>. Esta migración parte
    /// de ESE cuerpo TAL CUAL (preserva íntegras las ramas de <c>plate_flow_status</c> de HU #10785;
    /// HU #10606/#10611) y SOLO añade que <c>subsanacion</c> habilite la edición igual que
    /// <c>borrador</c> — sin eliminar ni alterar ninguna rama de placa/SOAT (evita la regresión
    /// silenciosa del Flujo B de preasignación de placa que rompía el reemplazo ingenuo por el
    /// cuerpo de N03). El segundo candado (bloqueo a nivel de aplicación) vive en
    /// <c>PatchFieldValuesHandler</c>. La tabla <c>tramites.procedure_instance_field_values</c> está
    /// <c>ExcludeFromMigrations</c> (DDL crudo, HU #10150): sin cambios de modelo EF, así que no
    /// requiere <c>*.Designer.cs</c> ni tocar el ModelSnapshot — se declaran
    /// <c>[DbContext]</c>/<c>[Migration]</c> inline (mismo patrón que N03_EstadosNegocioTramite /
    /// HU10879_CurrentStep) para que se descubra y corra al arrancar.
    /// </remarks>
    [DbContext(typeof(FlitDbContext))]
    [Migration("20260724120000_HU10870_SubsanacionEditableTrigger")]
    public partial class HU10870_SubsanacionEditableTrigger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                -- HU #10870 (AC1) — BASE: cuerpo VIGENTE de HU #10785 (Feature #10587), íntegro. ÚNICO
                -- cambio: la condición de "estado editable" ahora es 'borrador' O 'subsanacion' (antes
                -- solo 'borrador'); las ramas de plate_flow_status (preasignado→plate, asignado→soat_estado)
                -- se preservan sin modificar.
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
                  IF v_status = 'borrador' OR v_status = 'subsanacion' THEN
                    RETURN COALESCE(NEW, OLD);
                  END IF;
                  v_key := COALESCE(NEW.field_key, OLD.field_key);
                  -- Ruta de placa (Feature #10587 / HU #10785): la editabilidad la gobierna el SUB-ESTADO
                  -- interno plate_flow_status, no el status (que permanece en 'entregado'). Preservado
                  -- TAL CUAL de HU #10785 — no se toca.
                  IF v_plate = 'preasignado' AND v_key = 'plate' THEN
                    RETURN COALESCE(NEW, OLD);
                  END IF;
                  IF v_plate = 'asignado' AND v_key = 'soat_estado' THEN
                    RETURN COALESCE(NEW, OLD);
                  END IF;
                  RAISE EXCEPTION 'procedure_instance_field_values son inmutables cuando la instancia está en estado % (solo borrador/subsanacion permiten cambios)', v_status
                    USING ERRCODE = 'check_violation';
                END; $$ LANGUAGE plpgsql;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                -- Restaura EXACTAMENTE el cuerpo vigente ANTERIOR (HU #10785 Up), no el de N03: solo
                -- 'borrador' habilita edición general; plate/soat_estado siguen por plate_flow_status.
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
                  v_key := COALESCE(NEW.field_key, OLD.field_key);
                  IF v_plate = 'preasignado' AND v_key = 'plate' THEN
                    RETURN COALESCE(NEW, OLD);
                  END IF;
                  IF v_plate = 'asignado' AND v_key = 'soat_estado' THEN
                    RETURN COALESCE(NEW, OLD);
                  END IF;
                  RAISE EXCEPTION 'procedure_instance_field_values son inmutables cuando la instancia está en estado % (solo borrador permite cambios)', v_status
                    USING ERRCODE = 'check_violation';
                END; $$ LANGUAGE plpgsql;
                """);
        }
    }
}
