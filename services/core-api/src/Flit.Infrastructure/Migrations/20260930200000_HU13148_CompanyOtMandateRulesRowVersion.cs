using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations;

/// <summary>
/// HU #13148 (Feature #13117, Épica #13090) — agrega <c>row_version</c> y su trigger a
/// <c>admin.company_ot_mandate_rules</c> para detectar ediciones simultáneas del tipo de mandato por compañía
/// y organismo. Aditiva e idempotente; las filas previas quedan con <c>row_version = 0</c>.
/// DDL: <c>123-HU13148-company-ot-mandate-rules-row-version.sql</c>.
/// </summary>
[DbContext(typeof(FlitDbContext))]
[Migration("20260930200000_HU13148_CompanyOtMandateRulesRowVersion")]
public partial class HU13148_CompanyOtMandateRulesRowVersion : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(EmbeddedDdl.LoadUp("123-HU13148-company-ot-mandate-rules-row-version.sql"));

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(
            """
            DROP TRIGGER IF EXISTS tr_company_ot_mandate_rules_row_version ON admin.company_ot_mandate_rules;
            ALTER TABLE admin.company_ot_mandate_rules DROP COLUMN IF EXISTS row_version;
            """);
}
