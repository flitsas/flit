using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations;

/// <summary>
/// HU #12238 (Feature #12236) — tabla <c>admin.banners</c>: banners promocionales
/// globales (ADR-0058, sin <c>tenant_id</c> ni RLS — excepción documentada a A4/A10/A11)
/// cuya imagen se sirve por endpoint propio de streaming (ADR-0057). DDL:
/// <c>107-HU12238-admin-banners.sql</c>.
///
/// AC2: el DDL de referencia de ADR-0058 declara <c>valid_from</c>/<c>valid_until</c>
/// <c>NOT NULL</c> con <c>CHECK ck_banners_vigencia (valid_until > valid_from)</c>. Esta
/// migración ajusta ambas columnas a nullable y el CHECK admite
/// <c>(valid_from IS NULL AND valid_until IS NULL)</c> además del caso con ambas
/// presentes y <c>valid_until > valid_from</c> — un banner puede no tener vigencia
/// programada (activación/desactivación manual vía <c>is_active</c>), requisito
/// funcional aprobado que el DDL de referencia del ADR no contemplaba.
/// </summary>
[DbContext(typeof(FlitDbContext))]
[Migration("20260909180000_HU12238_AdminBanners")]
public partial class HU12238_AdminBanners : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(EmbeddedDdl.LoadUp("107-HU12238-admin-banners.sql"));

    /// <inheritdoc />
    /// <remarks>
    /// Revierte en orden inverso: triggers, luego la tabla (el índice, el CHECK y el
    /// COMMENT se eliminan implícitamente con el DROP TABLE). No hay dato productivo que
    /// perder — la tabla se crea vacía en esta HU; el CRUD llega en HU #12239.
    /// </remarks>
    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(
            """
            DROP TRIGGER IF EXISTS tr_banners_audit ON admin.banners;
            DROP TRIGGER IF EXISTS tr_banners_row_version ON admin.banners;
            DROP TABLE IF EXISTS admin.banners;
            """);
}
