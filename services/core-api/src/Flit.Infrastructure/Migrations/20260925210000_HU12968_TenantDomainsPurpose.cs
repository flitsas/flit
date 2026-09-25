using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations;

/// <summary>
/// HU #12968 (Feature #12888, FLIT Suite, tarea B-08) — <c>admin.tenant_domains.purpose</c> (HUB o un producto),
/// unicidad por (red, propósito) y la vista de dominios activos con el propósito. DDL en
/// <c>122-HU12968-tenant-domains-purpose.sql</c>; la entidad EF está <c>ExcludeFromMigrations</c>.
/// </summary>
public partial class HU12968_TenantDomainsPurpose : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(EmbeddedDdl.LoadUp("122-HU12968-tenant-domains-purpose.sql"));

    /// <inheritdoc />
    /// <remarks>
    /// Los dominios de producto se retiran (soft delete) antes de volver al índice de un dominio por red, que no
    /// los admite. La vista se recrea sin la columna (quitar una columna exige DROP + CREATE).
    /// </remarks>
    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("""
            UPDATE admin.tenant_domains SET deleted_at = now() WHERE purpose <> 'HUB' AND deleted_at IS NULL;

            DROP VIEW IF EXISTS admin.v_active_network_domains;
            CREATE VIEW admin.v_active_network_domains AS
            SELECT d.host,
                   d.tenant_id AS head_tenant_id
              FROM admin.tenant_domains d
              JOIN identity.tenants t ON t.id = d.tenant_id
             WHERE d.status = 'active'
               AND d.deleted_at IS NULL
               AND t.tenant_type = 'MARCA_BLANCA'
               AND t.is_group_parent = true
               AND t.is_active = true;

            DROP INDEX IF EXISTS admin.uq_tenant_domains_tenant_purpose;
            CREATE UNIQUE INDEX IF NOT EXISTS uq_tenant_domains_tenant_id
                ON admin.tenant_domains (tenant_id) WHERE deleted_at IS NULL;
            DROP TRIGGER IF EXISTS tr_tenant_domains_purpose_product ON admin.tenant_domains;
            DROP FUNCTION IF EXISTS admin.trg_tenant_domains_purpose_product();
            ALTER TABLE admin.tenant_domains DROP CONSTRAINT IF EXISTS ck_tenant_domains_purpose;
            ALTER TABLE admin.tenant_domains DROP COLUMN IF EXISTS purpose;
            """);
}
