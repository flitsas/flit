using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations;

/// <summary>
/// Plataforma Mandatos — columna <c>assignment_mode</c> (signer | institutional | open)
/// para exponer los tres tipos de negocio sin alterar plantillas ni defaults implícitos.
/// </summary>
[DbContext(typeof(FlitDbContext))]
[Migration("20260806163000_MandateAssignmentMode")]
public partial class MandateAssignmentMode : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(EmbeddedDdl.LoadUp("59-mandate-assignment-mode.sql"));
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            ALTER TABLE admin.transit_office_mandate_config
              DROP CONSTRAINT IF EXISTS ck_transit_office_mandate_config_assignment_mode,
              DROP COLUMN IF EXISTS assignment_mode;
            """);
    }
}
