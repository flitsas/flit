using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations;

/// <summary>
/// HU #12323 (Feature #12254, Épica #12235, ADR-0057) — desactivación sin despliegue y auditoría del
/// vínculo padre-hija. Crea <c>identity.hierarchy_switches</c> (interruptores globales
/// <c>group_read_scope</c> e <c>inherited_configuration</c>, seed idempotente encendido, en columnas
/// propias y nunca sobre <c>is_group_parent</c>), la bitácora append-only
/// <c>identity.tenant_hierarchy_audit</c> (LINK/UNLINK, sin FK a <c>identity.tenants</c> a propósito
/// para sobrevivir a desvínculos y borrados, UPDATE/DELETE rechazados por trigger) y el trigger
/// <c>tr_tenants_hierarchy_audit</c> (AFTER INSERT OR UPDATE OF <c>parent_tenant_id</c>) que la
/// alimenta. No toca ninguna columna existente de <c>identity.tenants</c>.
/// DDL: <c>108-HU12323-hierarchy-switches-and-link-audit.sql</c>.
/// </summary>
[DbContext(typeof(FlitDbContext))]
[Migration("20260910130000_HU12323_HierarchySwitchesAndLinkAudit")]
public partial class HU12323_HierarchySwitchesAndLinkAudit : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(EmbeddedDdl.LoadUp("108-HU12323-hierarchy-switches-and-link-audit.sql"));

    /// <inheritdoc />
    /// <remarks>
    /// Orden inverso al Up: primero el trigger sobre <c>identity.tenants</c> y su función (para que
    /// ningún UPDATE posterior intente escribir en una tabla que ya no existe), luego la bitácora con
    /// su trigger de inmutabilidad, y al final los interruptores. Las dos tablas nacen en esta
    /// migración, así que eliminarlas no pierde ningún dato anterior a ella; la bitácora acumulada
    /// desde el Up sí se pierde, que es lo esperado de un rollback de esta HU.
    /// </remarks>
    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("""
            DROP TRIGGER IF EXISTS tr_tenants_hierarchy_audit ON identity.tenants;

            DROP FUNCTION IF EXISTS identity.trg_tenant_hierarchy_audit();

            DROP TRIGGER IF EXISTS tr_tenant_hierarchy_audit_immutable ON identity.tenant_hierarchy_audit;

            DROP FUNCTION IF EXISTS identity.trg_tenant_hierarchy_audit_immutable();

            DROP INDEX IF EXISTS identity.ix_tenant_hierarchy_audit_child_occurred_at;

            DROP INDEX IF EXISTS identity.ix_tenant_hierarchy_audit_parent_occurred_at;

            DROP TABLE IF EXISTS identity.tenant_hierarchy_audit;

            DROP TRIGGER IF EXISTS tr_hierarchy_switches_audit ON identity.hierarchy_switches;

            DROP TRIGGER IF EXISTS tr_hierarchy_switches_row_version ON identity.hierarchy_switches;

            DROP TABLE IF EXISTS identity.hierarchy_switches;
            """);
}
