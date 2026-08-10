using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations;

/// <summary>Seed plantilla sistema municipio (Envigado / Funza / Medellín).</summary>
[DbContext(typeof(FlitDbContext))]
[Migration("20260810120000_MandateMunicipioSystemTemplates")]
public partial class MandateMunicipioSystemTemplates : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(EmbeddedDdl.LoadUp("63-mandate-municipio-system-templates.sql"));
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DELETE FROM admin.transit_office_mandate_config cfg
            USING catalogs.transit_offices ot
            WHERE cfg.transit_office_id = ot.id
              AND ot.code IN ('5266000', '25286000', '5001000')
              AND cfg.template_code = 'municipio';
            """);
    }
}
