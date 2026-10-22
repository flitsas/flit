using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations
{
    /// <summary>
    /// HU #13264 (Feature #13261, Épica #12741) — el trigger de inmutabilidad de <c>field_values</c> admite
    /// <c>impuesto_departamental_pagado</c> en <c>preasignacion</c> (la marca de FLITO al adjuntar su comprobante).
    /// Sin otras llaves ni estados; <c>entregado</c> sigue rechazando. DDL:
    /// <c>130-HU13264-impuesto-pagado-flito-preasignacion.sql</c>.
    /// </summary>
    public partial class HU13264_ImpuestoPagadoFlitoPreasignacion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder) =>
            migrationBuilder.Sql(EmbeddedDdl.LoadUp("130-HU13264-impuesto-pagado-flito-preasignacion.sql"));

        /// <inheritdoc />
        /// <remarks>Restaura la versión de 129-BUG13194 (preasignacion solo admite 'plate').</remarks>
        protected override void Down(MigrationBuilder migrationBuilder) =>
            migrationBuilder.Sql(
                """
                CREATE OR REPLACE FUNCTION tramites.trg_field_value_immutable() RETURNS trigger AS $$
                DECLARE v_status varchar(20);
                DECLARE v_key varchar(80);
                BEGIN
                  SELECT status INTO v_status FROM tramites.procedure_instances
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
                  -- ADR-0059 — la ruta de placa vive en status.
                  IF v_status = 'preasignacion' AND v_key = 'plate' THEN
                    RETURN COALESCE(NEW, OLD);
                  END IF;
                  -- Bug #13194 — 'soat_vencimiento': fecha del PDF del SOAT leído por OCR (soporte manual del gate).
                  IF v_status = 'asignado' AND v_key IN ('plate', 'soat_estado', 'soat_vencimiento', 'soat_pagado', 'impuesto_departamental_pagado') THEN
                    RETURN COALESCE(NEW, OLD);
                  END IF;
                  RAISE EXCEPTION 'procedure_instance_field_values son inmutables cuando la instancia está en estado % (solo borrador permite cambios)', v_status
                    USING ERRCODE = 'check_violation';
                END; $$ LANGUAGE plpgsql;
                """);
    }
}
