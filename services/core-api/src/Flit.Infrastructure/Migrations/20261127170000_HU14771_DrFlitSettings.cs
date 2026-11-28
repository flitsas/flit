using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations;

/// <summary>
/// HU #14771 (Feature #14769) — <c>admin.dr_flit_settings</c>: fila única de visibilidad UI de DR. FLIT.
/// DDL: <c>207-HU14771-dr-flit-settings.sql</c>. Sin Designer (atributos inline).
/// </summary>
[DbContext(typeof(FlitDbContext))]
[Migration("20261127170000_HU14771_DrFlitSettings")]
public partial class HU14771_DrFlitSettings : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(EmbeddedDdl.LoadUp("207-HU14771-dr-flit-settings.sql"));

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(
            """
            DROP TRIGGER IF EXISTS tr_dr_flit_settings_audit ON admin.dr_flit_settings;
            DROP TRIGGER IF EXISTS tr_dr_flit_settings_row_version ON admin.dr_flit_settings;
            DROP TABLE IF EXISTS admin.dr_flit_settings;
            """);
}
