using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations;

/// <summary>
/// HU #13604 (Feature #13602, Épica #13517) - el código de tenant pasa a ser el consecutivo <c>COMP-XXXX</c>:
/// secuencia <c>identity.tenant_code_seq</c>, DEFAULT <c>identity.fn_next_tenant_code()</c> sobre
/// <c>identity.tenants.code</c>, columna <c>legacy_code</c> y recodificación única de los tenants existentes por
/// antigüedad. DDL: <c>136-HU13604-codigo-tenant-secuencial.sql</c>. Idempotente.
/// </summary>
[DbContext(typeof(FlitDbContext))]
[Migration("20261015180000_HU13604_CodigoTenantSecuencial")]
public partial class HU13604_CodigoTenantSecuencial : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(EmbeddedDdl.LoadUp("136-HU13604-codigo-tenant-secuencial.sql"));

    /// <inheritdoc />
    /// <remarks>
    /// Devuelve a cada tenant recodificado su código anterior (<c>legacy_code</c>), en dos pasadas por la misma razón
    /// que el Up (<c>uq_tenants_code</c> no es diferible). Los tenants creados después de la recodificación no tienen
    /// código anterior y conservan su <c>COMP-XXXX</c>. Quitar la columna antes que la secuencia: el DEFAULT depende
    /// de la función y la secuencia pertenece a la columna <c>code</c>.
    /// </remarks>
    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("""
            ALTER TABLE identity.tenants ALTER COLUMN code DROP DEFAULT;
            UPDATE identity.tenants SET code = '#' || legacy_code WHERE legacy_code IS NOT NULL;
            UPDATE identity.tenants SET code = substr(code, 2) WHERE code LIKE '#%';
            ALTER TABLE identity.tenants DROP COLUMN IF EXISTS legacy_code;
            DROP FUNCTION IF EXISTS identity.fn_next_tenant_code();
            DROP FUNCTION IF EXISTS identity.fn_format_tenant_code(bigint);
            DROP SEQUENCE IF EXISTS identity.tenant_code_seq;
            """);
}
