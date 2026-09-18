using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations;

/// <summary>
/// HU #12412 (Feature #12366, Épica #12237 Marca Blanca, ADR-0060 D1) — identidad de marca de la
/// cabeza de red: <c>admin.tenant_brandings</c> (borrador + publicada), <c>admin.tenant_brand_logos</c>
/// (versiones del logotipo) y la función compartida <c>identity.trg_require_marca_blanca_head()</c>
/// (fail-closed: solo una cabeza de tipo MARCA_BLANCA). DDL en
/// <c>115-HU12412-tenant-brandings.sql</c>, que es la fuente de verdad; esta migración solo lo ejecuta.
/// No puebla datos (AC8). Las entidades EF están <c>ExcludeFromMigrations</c>.
/// </summary>
public partial class E12412_TenantBrandings : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(EmbeddedDdl.LoadUp("115-HU12412-tenant-brandings.sql"));

    /// <inheritdoc />
    /// <remarks>
    /// Reversible y sin pérdida para quien no es Marca Blanca (las tablas nacen vacías). Orden inverso al
    /// Up: triggers y políticas caen con la tabla; la función compartida se elimina al final y solo si
    /// ya nadie la usa (el DDL 116 de <c>admin.tenant_domains</c> la reutiliza y su Down corre antes).
    /// </remarks>
    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("""
            DROP TRIGGER IF EXISTS tr_tenant_brand_logos_audit ON admin.tenant_brand_logos;
            DROP TRIGGER IF EXISTS tr_tenant_brand_logos_row_version ON admin.tenant_brand_logos;
            DROP TRIGGER IF EXISTS tr_tenant_brand_logos_marca_blanca ON admin.tenant_brand_logos;
            DROP POLICY IF EXISTS tenant_isolation ON admin.tenant_brand_logos;
            DROP INDEX IF EXISTS admin.ix_tenant_brand_logos_tenant_id;
            DROP INDEX IF EXISTS admin.uq_tenant_brand_logos_one_active;
            DROP TABLE IF EXISTS admin.tenant_brand_logos;

            DROP TRIGGER IF EXISTS tr_tenant_brandings_audit ON admin.tenant_brandings;
            DROP TRIGGER IF EXISTS tr_tenant_brandings_row_version ON admin.tenant_brandings;
            DROP TRIGGER IF EXISTS tr_tenant_brandings_marca_blanca ON admin.tenant_brandings;
            DROP POLICY IF EXISTS tenant_isolation ON admin.tenant_brandings;
            DROP TABLE IF EXISTS admin.tenant_brandings;

            DROP FUNCTION IF EXISTS identity.trg_require_marca_blanca_head();
            """);
}
