using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations
{
    /// <summary>
    /// HU #13169 (Feature #13118, Épica #13090) — personalización y versiones inmutables de cada formato de contrato
    /// de mandato (<c>admin.mandate_format_settings</c> y <c>admin.mandate_format_versions</c>). Aditiva e idempotente;
    /// siembra una fila por formato del catálogo con los valores actuales. Las entidades están excluidas de
    /// migraciones: el esquema lo crea el DDL <c>124-HU13169-mandate-format-settings-versions.sql</c>. Es descubrible
    /// al arrancar por el Designer.cs.
    /// </summary>
    public partial class HU13169_MandateFormatSettingsVersions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder) =>
            migrationBuilder.Sql(EmbeddedDdl.LoadUp("124-HU13169-mandate-format-settings-versions.sql"));

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) =>
            migrationBuilder.Sql(
                """
                DROP TRIGGER IF EXISTS tr_mandate_format_versions_immutable ON admin.mandate_format_versions;
                DROP TABLE IF EXISTS admin.mandate_format_versions;
                DROP FUNCTION IF EXISTS admin.trg_mandate_format_versions_immutable();
                DROP TABLE IF EXISTS admin.mandate_format_settings;
                """);
    }
}
