using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations;

/// <summary>
/// HU #12416 (Feature #12368, Épica #12237 Marca Blanca, ADR-0060 D1/D2) — dominio dedicado de la red
/// como dato de la plataforma: <c>admin.tenant_domains</c> (uno vigente por cabeza MARCA_BLANCA, host
/// único global normalizado, estados pending|verified|active|failed, token de verificación, soft
/// delete) y la vista <c>admin.v_active_network_domains</c> (solo lo que resuelve red). Reutiliza la
/// función compartida <c>identity.trg_require_marca_blanca_head()</c> del DDL 115. DDL en
/// <c>116-HU12416-tenant-domains.sql</c>, que es la fuente de verdad; esta migración solo lo ejecuta.
/// No puebla datos (AC6). La entidad EF está <c>ExcludeFromMigrations</c> y la vista es keyless.
/// </summary>
public partial class E12416_TenantDomains : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(EmbeddedDdl.LoadUp("116-HU12416-tenant-domains.sql"));

    /// <inheritdoc />
    /// <remarks>
    /// Reversible y sin pérdida para quien no es Marca Blanca (la tabla nace vacía). Orden inverso al
    /// Up: primero la vista (depende de la tabla), luego triggers, política, índices y tabla. La función
    /// compartida <c>identity.trg_require_marca_blanca_head()</c> NO se toca aquí: la creó el DDL 115 y
    /// su Down (E12412) la retira cuando ya nadie la usa.
    /// </remarks>
    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("""
            DROP VIEW IF EXISTS admin.v_active_network_domains;

            DROP TRIGGER IF EXISTS tr_tenant_domains_audit ON admin.tenant_domains;
            DROP TRIGGER IF EXISTS tr_tenant_domains_row_version ON admin.tenant_domains;
            DROP TRIGGER IF EXISTS tr_tenant_domains_marca_blanca ON admin.tenant_domains;
            DROP POLICY IF EXISTS tenant_isolation ON admin.tenant_domains;
            DROP INDEX IF EXISTS admin.ix_tenant_domains_next_check;
            DROP INDEX IF EXISTS admin.ix_tenant_domains_host_active;
            DROP INDEX IF EXISTS admin.ix_tenant_domains_tenant_id;
            DROP INDEX IF EXISTS admin.uq_tenant_domains_verification_token;
            DROP INDEX IF EXISTS admin.uq_tenant_domains_host;
            DROP INDEX IF EXISTS admin.uq_tenant_domains_tenant_id;
            DROP TABLE IF EXISTS admin.tenant_domains;
            """);
}
