using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations;

/// <summary>
/// HU #14389 (Feature #14387, Épica #14297) — un precio nuevo con el mismo inicio reemplaza al de ese día.
/// DDL: <c>181-HU14389-tarifas-reemplazo-mismo-dia.sql</c> (columna <c>superseded_at</c> no mapeada en EF, índice único
/// parcial, trigger y función de versionado). Lleva los atributos inline (sin Designer): sin ellos no se descubre.
/// </summary>
[DbContext(typeof(FlitDbContext))]
[Migration("20261105130000_HU14389_TarifasReemplazoMismoDia")]
public partial class HU14389_TarifasReemplazoMismoDia : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(EmbeddedDdl.LoadUp("181-HU14389-tarifas-reemplazo-mismo-dia.sql"));

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder) =>
        // Las versiones reemplazadas son historial que el trigger no deja borrar: si existen, no se puede volver al índice
        // único total sin perderlas, así que se detiene con un mensaje claro en vez de dejar la base a medias.
        migrationBuilder.Sql(
            """
            DO $$
            BEGIN
                IF EXISTS (SELECT 1 FROM admin.persona_natural_tariffs WHERE superseded_at IS NOT NULL) THEN
                    RAISE EXCEPTION 'Hay versiones de tarifa reemplazadas (HU 14389): no se puede revertir sin perder historial.';
                END IF;
            END $$;

            DROP INDEX IF EXISTS admin.uq_persona_natural_tariffs_scope_start;
            CREATE UNIQUE INDEX uq_persona_natural_tariffs_scope_start ON admin.persona_natural_tariffs (
                concept,
                COALESCE(procedure_type_id, '00000000-0000-0000-0000-000000000000'::uuid),
                COALESCE(transit_office_id, '00000000-0000-0000-0000-000000000000'::uuid),
                valid_from);

            CREATE OR REPLACE FUNCTION admin.trg_persona_natural_tariffs_guard() RETURNS trigger AS $$
            BEGIN
                IF TG_OP = 'DELETE' THEN
                    RAISE EXCEPTION 'Las versiones de tarifa no se eliminan: se cierran con una versión nueva.' USING ERRCODE = 'restrict_violation';
                END IF;
                IF TG_OP = 'UPDATE' THEN
                    IF OLD.valid_to IS NOT NULL THEN
                        RAISE EXCEPTION 'Una versión de tarifa cerrada es historial y no se edita.' USING ERRCODE = 'restrict_violation';
                    END IF;
                    IF NEW.concept <> OLD.concept
                       OR NEW.procedure_type_id IS DISTINCT FROM OLD.procedure_type_id
                       OR NEW.transit_office_id IS DISTINCT FROM OLD.transit_office_id
                       OR NEW.amount_cop <> OLD.amount_cop
                       OR NEW.valid_from <> OLD.valid_from THEN
                        RAISE EXCEPTION 'De una versión de tarifa abierta solo se puede cerrar la vigencia; el valor cambia con una versión nueva.' USING ERRCODE = 'restrict_violation';
                    END IF;
                    RETURN NEW;
                END IF;
                PERFORM pg_advisory_xact_lock(hashtextextended(
                    NEW.concept || '|' || COALESCE(NEW.procedure_type_id::text, '-') || '|' || COALESCE(NEW.transit_office_id::text, '-'), 0));
                IF EXISTS (
                    SELECT 1 FROM admin.persona_natural_tariffs t
                     WHERE t.concept = NEW.concept
                       AND t.procedure_type_id IS NOT DISTINCT FROM NEW.procedure_type_id
                       AND t.transit_office_id IS NOT DISTINCT FROM NEW.transit_office_id
                       AND t.id <> NEW.id
                       AND t.valid_from <= COALESCE(NEW.valid_to, 'infinity'::date)
                       AND COALESCE(t.valid_to, 'infinity'::date) >= NEW.valid_from) THEN
                    RAISE EXCEPTION 'Ya existe una tarifa vigente para ese concepto, tipo y organismo en esas fechas.' USING ERRCODE = 'unique_violation';
                END IF;
                RETURN NEW;
            END;
            $$ LANGUAGE plpgsql;

            CREATE OR REPLACE FUNCTION admin.persona_natural_tariff_new_version(
                p_concept varchar, p_procedure_type_id uuid, p_transit_office_id uuid, p_amount_cop bigint, p_valid_from date, p_user uuid)
            RETURNS uuid AS $$
            DECLARE
                v_id uuid;
            BEGIN
                PERFORM pg_advisory_xact_lock(hashtextextended(
                    p_concept || '|' || COALESCE(p_procedure_type_id::text, '-') || '|' || COALESCE(p_transit_office_id::text, '-'), 0));
                UPDATE admin.persona_natural_tariffs t
                   SET valid_to = p_valid_from - 1, updated_by = p_user, updated_at = now()
                 WHERE t.concept = p_concept
                   AND t.procedure_type_id IS NOT DISTINCT FROM p_procedure_type_id
                   AND t.transit_office_id IS NOT DISTINCT FROM p_transit_office_id
                   AND t.valid_to IS NULL
                   AND t.valid_from < p_valid_from;
                INSERT INTO admin.persona_natural_tariffs (concept, procedure_type_id, transit_office_id, amount_cop, valid_from, created_by)
                VALUES (p_concept, p_procedure_type_id, p_transit_office_id, p_amount_cop, p_valid_from, p_user)
                RETURNING id INTO v_id;
                RETURN v_id;
            END;
            $$ LANGUAGE plpgsql;

            ALTER TABLE admin.persona_natural_tariffs DROP COLUMN IF EXISTS superseded_at;
            """);
}
