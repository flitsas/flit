using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations;

/// <summary>
/// Mejora sin HU (Feature #14453, ADR-0082, revisión 2026-11-17) — ventana de anulación de la caja por organismo
/// (<c>admin.persona_natural_caja_settings</c>) y su historial (<c>admin.persona_natural_caja_window_history</c>).
/// DDL: <c>201-ventana-anulacion-caja-por-organismo.sql</c>. Sin Designer: sin los atributos no se descubre.
/// </summary>
[DbContext(typeof(FlitDbContext))]
[Migration("20261117160000_VentanaAnulacionCajaPorOrganismo")]
public partial class VentanaAnulacionCajaPorOrganismo : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(EmbeddedDdl.LoadUp("201-ventana-anulacion-caja-por-organismo.sql"));

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("""
            DROP TABLE IF EXISTS admin.persona_natural_caja_window_history;
            DROP TABLE IF EXISTS admin.persona_natural_caja_settings;
            """);
}
