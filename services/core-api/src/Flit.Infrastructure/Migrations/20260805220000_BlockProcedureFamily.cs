using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations;

/// <summary>
/// Bloqueo de creación por familia TRASPASO/OTROS (MATRICULAS sigue en allow_initial_registration invertido en UI).
/// </summary>
[DbContext(typeof(FlitDbContext))]
[Migration("20260805220000_BlockProcedureFamily")]
public partial class BlockProcedureFamily : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(EmbeddedDdl.LoadUp("58-block-procedure-family.sql"));
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            ALTER TABLE admin.tenant_operational_policies
              DROP COLUMN IF EXISTS block_procedure_family_traspaso,
              DROP COLUMN IF EXISTS block_procedure_family_otros;
            """);
    }
}
