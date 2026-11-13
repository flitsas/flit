using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations;

/// <summary>
/// HU #14563 (decisión D24 · L1) — retira el rol global de producto <c>backoffice_diagnostico</c>, que el manifiesto
/// de Diagnóstico dejó de declarar (D23) y que <c>ProductManifestStore</c> no retira: cierra sus asignaciones, borra
/// sus permisos y lo deja inactivo y en soft delete. Sin cambio de esquema. DDL:
/// <c>196-HU14563-retiro-rol-backoffice-diagnostico.sql</c>. Sin Designer: sin los atributos la migración no se descubre.
/// </summary>
[DbContext(typeof(FlitDbContext))]
[Migration("20261113120000_HU14563_RetiroRolBackofficeDiagnostico")]
public partial class HU14563_RetiroRolBackofficeDiagnostico : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(EmbeddedDdl.LoadUp("196-HU14563-retiro-rol-backoffice-diagnostico.sql"));

    /// <inheritdoc />
    /// <remarks>
    /// Restituye el rol y lo que la migración cerró en la misma transacción (marca de tiempo = <c>deleted_at</c> del rol):
    /// las asignaciones con ese <c>deleted_at</c> y los permisos cuyo borrado quedó en <c>audit.audit_logs</c> con ese
    /// <c>changed_at</c>. No hace nada si el rol no existe o si ya hay otro vigente con el mismo código.
    /// </remarks>
    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("""
            DO $$
            DECLARE
                v_role uuid;
                v_at   timestamptz;
            BEGIN
                SELECT id, deleted_at INTO v_role, v_at
                  FROM security.roles
                 WHERE code = 'backoffice_diagnostico' AND product_code = 'diagnostico' AND tenant_id IS NULL
                   AND deleted_at IS NOT NULL
                 ORDER BY deleted_at DESC
                 LIMIT 1;

                IF v_role IS NULL OR EXISTS (
                       SELECT 1 FROM security.roles x
                        WHERE lower(x.code) = 'backoffice_diagnostico' AND x.deleted_at IS NULL) THEN
                    RETURN;
                END IF;

                UPDATE security.roles
                   SET is_active = true, deleted_at = NULL, deleted_by = NULL, updated_at = now()
                 WHERE id = v_role;

                INSERT INTO security.role_permissions (role_id, permission_id)
                SELECT DISTINCT v_role, p.id
                  FROM audit.audit_logs l
                  JOIN security.permissions p ON p.id = (l.old_data ->> 'permission_id')::uuid
                 WHERE l.schema_name = 'security' AND l.table_name = 'role_permissions' AND l.action = 'D'
                   AND (l.old_data ->> 'role_id')::uuid = v_role AND l.changed_at = v_at
                ON CONFLICT (role_id, permission_id) DO NOTHING;

                UPDATE security.user_role_assignments a
                   SET deleted_at = NULL, deleted_by = NULL, updated_at = now()
                 WHERE a.role_id = v_role AND a.deleted_at = v_at
                   AND NOT EXISTS (
                       SELECT 1 FROM security.user_role_assignments x
                        WHERE x.user_id = a.user_id AND x.tenant_id = a.tenant_id
                          AND x.product_code = a.product_code AND x.deleted_at IS NULL);
            END
            $$;
            """);
}
