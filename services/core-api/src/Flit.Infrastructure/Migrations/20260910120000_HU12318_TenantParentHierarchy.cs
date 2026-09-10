using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations;

/// <summary>
/// HU #12318 (Feature #12254, Épica #12235) — jerarquía de clientes padre-hija con profundidad 2
/// forzada por la base de datos. Agrega a <c>identity.tenants</c> las columnas
/// <c>parent_tenant_id uuid NULL</c> (FK a la misma tabla, <c>ON DELETE RESTRICT</c>) e
/// <c>is_group_parent boolean NOT NULL DEFAULT false</c>, el CHECK anti-autorreferencia
/// <c>ck_tenants_parent_not_self</c>, el índice parcial <c>ix_tenants_parent_tenant_id</c> y el
/// trigger bidireccional <c>tr_tenants_hierarchy</c> (un hijo cuelga solo de una cabeza de grupo sin
/// padre; una cabeza de grupo no cuelga de nadie; quien tiene hijos no puede dejar de ser cabeza ni
/// recibir padre). <c>tenant_type</c> y <c>ck_tenants_tenant_type</c> no se tocan. Sin backfill.
/// DDL: <c>107-HU12318-tenant-parent-hierarchy.sql</c>.
/// </summary>
[DbContext(typeof(FlitDbContext))]
[Migration("20260910120000_HU12318_TenantParentHierarchy")]
public partial class HU12318_TenantParentHierarchy : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(EmbeddedDdl.LoadUp("107-HU12318-tenant-parent-hierarchy.sql"));

    /// <inheritdoc />
    /// <remarks>
    /// Aditiva y reversible sin condiciones: las columnas son nuevas y nunca tuvieron backfill, así
    /// que eliminarlas no pierde ningún dato que existiera antes de esta migración. Se retira primero
    /// el trigger y su función, luego índice y constraints, y al final las columnas.
    /// </remarks>
    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("""
            DROP TRIGGER IF EXISTS tr_tenants_hierarchy ON identity.tenants;

            DROP FUNCTION IF EXISTS identity.trg_tenant_hierarchy_depth();

            DROP INDEX IF EXISTS identity.ix_tenants_parent_tenant_id;

            ALTER TABLE identity.tenants
                DROP CONSTRAINT IF EXISTS ck_tenants_parent_not_self;

            ALTER TABLE identity.tenants
                DROP CONSTRAINT IF EXISTS fk_tenants_parent_tenant;

            ALTER TABLE identity.tenants
                DROP COLUMN IF EXISTS is_group_parent;

            ALTER TABLE identity.tenants
                DROP COLUMN IF EXISTS parent_tenant_id;
            """);
}
