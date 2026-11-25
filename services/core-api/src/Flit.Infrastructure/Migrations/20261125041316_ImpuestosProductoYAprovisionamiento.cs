using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations;

/// <summary>
/// HU #14694 (Epic #14692, producto Impuestos, ADR-0093): producto <c>impuestos</c>, rol <c>admin_impuestos</c> sembrado a
/// los AdminCompany con el ascenso de Gestor a Admin solo en Impuestos, y la tabla
/// <c>platform.tenant_product_provisioning</c>. DDL: <c>205-impuestos-producto-y-aprovisionamiento.sql</c>.
/// <para>
/// Generada con <c>dotnet ef migrations add</c>. El cuerpo que generó la herramienta se reemplazó por el DDL porque la
/// tabla nueva es <c>ExcludeFromMigrations</c> y el diff del snapshot también traía cuatro tablas que ya existían por SQL
/// (<c>admin.ot_user_client_scopes</c>, <c>admin.ot_user_client_scope_clients</c>,
/// <c>admin.ot_invitation_client_scope_clients</c> y <c>admin.tenant_runt_person_overrides</c>, DDL 197 y 203): el
/// snapshot queda alineado con el modelo y esta migración no las vuelve a crear.
/// </para>
/// </summary>
public partial class ImpuestosProductoYAprovisionamiento : Migration
{
    /// <summary>
    /// Actor de sistema con que el paso 3a del DDL 205 cierra (<c>deleted_by</c>) los <c>gestor_impuestos</c> que asciende a
    /// <c>admin_impuestos</c>. No es un usuario (la columna no tiene FK): marca qué cierres son del DDL para que el
    /// <see cref="Down"/> reabra solo esos. El valor está repetido en el DDL; si cambia, cambia en los dos.
    /// </summary>
    public const string ActorAscensoDdl205 = "00000000-0000-4000-8000-000000000205";

    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(EmbeddedDdl.LoadUp("205-impuestos-producto-y-aprovisionamiento.sql"));

    /// <inheritdoc />
    /// <remarks>
    /// Vuelve al espejo del DDL 126, retira admin_impuestos con sus asignaciones y sus invitaciones
    /// (<c>security.user_invitations.role_id</c> es FK RESTRICT; las filas de <c>invitation_roles</c> caen en cascada), reabre
    /// solo los gestor_impuestos que cerró el paso 3a del DDL (<c>deleted_by</c> = <see cref="ActorAscensoDdl205"/>) si su
    /// usuario quedó sin rol de Impuestos, y borra el producto solo si nada lo referencia (el manifiesto pudo crear módulos,
    /// permisos y gestor_impuestos, que no son de esta migración). Los overrides de permisos de Impuestos
    /// (<c>security.user_permission_overrides.product_code</c>, FK RESTRICT, DDL 144) tampoco se borran: son concesiones
    /// sobre permisos del manifiesto, no sobre admin_impuestos; si hay alguno, el producto se queda.
    /// </remarks>
    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql($"""
            DROP TABLE IF EXISTS platform.tenant_product_provisioning;

            CREATE OR REPLACE FUNCTION security.trg_ura_mirror_product_admins()
            RETURNS trigger
            LANGUAGE plpgsql
            AS $$
            DECLARE
                v_admin_company uuid;
                v_was_active boolean;
                v_is_active boolean;
            BEGIN
                SELECT id INTO v_admin_company FROM security.roles
                 WHERE code = 'AdminCompany' AND target_entity_type = 'COMPANY' AND deleted_at IS NULL LIMIT 1;
                IF v_admin_company IS NULL THEN
                    RETURN NULL;
                END IF;

                v_is_active  := NEW.deleted_at IS NULL AND NEW.role_id = v_admin_company;
                v_was_active := TG_OP = 'UPDATE' AND OLD.deleted_at IS NULL AND OLD.role_id = v_admin_company;

                IF v_is_active AND NOT v_was_active THEN
                    INSERT INTO security.user_role_assignments (tenant_id, user_id, role_id, assigned_at, assigned_by, created_by)
                    SELECT NEW.tenant_id, NEW.user_id, pa.id, now(), NEW.assigned_by, NEW.created_by
                      FROM platform.products p
                      JOIN security.roles pa ON pa.code = 'admin_' || p.code AND pa.target_entity_type = 'COMPANY'
                                             AND pa.is_system AND pa.deleted_at IS NULL
                     WHERE p.code <> 'plataforma'
                       AND NOT EXISTS (
                           SELECT 1 FROM security.user_role_assignments x
                            WHERE x.user_id = NEW.user_id AND x.tenant_id = NEW.tenant_id
                              AND x.product_code = p.code AND x.deleted_at IS NULL);
                ELSIF v_was_active AND NOT v_is_active THEN
                    UPDATE security.user_role_assignments u
                       SET deleted_at = COALESCE(NEW.deleted_at, now()), deleted_by = NEW.deleted_by
                      FROM security.roles pa
                      JOIN platform.products p ON pa.code = 'admin_' || p.code AND p.code <> 'plataforma'
                     WHERE u.role_id = pa.id AND pa.is_system AND pa.target_entity_type = 'COMPANY'
                       AND u.user_id = NEW.user_id AND u.tenant_id = NEW.tenant_id AND u.deleted_at IS NULL;
                END IF;

                RETURN NULL;
            END;
            $$;

            COMMENT ON FUNCTION security.trg_ura_mirror_product_admins() IS
                'HU #12967 · El admin de cada producto (admin_<código>, de sistema) acompaña a AdminCompany: se crea al asignarlo y se cierra al quitarlo. Reemplaza al espejo solo de Trámites del DDL 120.';

            DELETE FROM security.user_role_assignments
             WHERE role_id IN (SELECT id FROM security.roles WHERE code = 'admin_impuestos' AND tenant_id IS NULL);
            DELETE FROM security.user_invitations
             WHERE role_id IN (SELECT id FROM security.roles WHERE code = 'admin_impuestos' AND tenant_id IS NULL);
            DELETE FROM security.roles WHERE code = 'admin_impuestos' AND tenant_id IS NULL;

            UPDATE security.user_role_assignments g
               SET deleted_at = NULL, deleted_by = NULL
             WHERE g.id IN (
                   SELECT DISTINCT ON (c.user_id, c.tenant_id) c.id
                     FROM security.user_role_assignments c
                     JOIN security.roles r ON r.id = c.role_id AND r.code = 'gestor_impuestos' AND r.tenant_id IS NULL
                    WHERE c.deleted_at IS NOT NULL
                      AND c.deleted_by = '{ActorAscensoDdl205}'::uuid
                      AND NOT EXISTS (
                          SELECT 1 FROM security.user_role_assignments x
                           WHERE x.user_id = c.user_id AND x.tenant_id = c.tenant_id
                             AND x.product_code = 'impuestos' AND x.deleted_at IS NULL)
                    ORDER BY c.user_id, c.tenant_id, c.deleted_at DESC);

            DELETE FROM platform.products p
             WHERE p.code = 'impuestos'
               AND NOT EXISTS (SELECT 1 FROM platform.tenant_products t WHERE t.product_code = p.code)
               AND NOT EXISTS (SELECT 1 FROM security.roles r WHERE r.product_code = p.code)
               AND NOT EXISTS (SELECT 1 FROM security.modules m WHERE m.product_code = p.code)
               AND NOT EXISTS (SELECT 1 FROM security.user_permission_overrides o WHERE o.product_code = p.code);
            """);
}
