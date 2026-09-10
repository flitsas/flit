using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations;

/// <summary>Tipo de mandato (3) por compañía gestora × OT.</summary>
[DbContext(typeof(FlitDbContext))]
[Migration("20260806230000_CompanyOtMandateRules")]
public partial class CompanyOtMandateRules : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(EmbeddedDdl.LoadUp("61-company-ot-mandate-rules.sql"));
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DROP TABLE IF EXISTS admin.company_ot_mandate_rules;
            """);
    }
}
