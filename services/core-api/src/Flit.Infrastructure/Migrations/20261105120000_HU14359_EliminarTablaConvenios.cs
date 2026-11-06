using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations;

/// <summary>
/// HU #14359 (Feature #14356) — elimina <c>admin.company_transit_office_agreements</c> con sus convenios.
///
/// <para><b>Regla de despliegue:</b> aplicarla solo en un ambiente donde ya corre la imagen que no lee la tabla
/// (#14357 y #14358). Una imagen anterior fallaría al consultarla, y el arranque con
/// <c>Database:AutoMigrate=true</c> la aplica al subir esta imagen, así que las dos HUs previas deben haber llegado
/// antes a ese ambiente.</para>
///
/// <para>A mano y sin Designer: la entidad estaba <c>ExcludeFromMigrations</c>, así que EF no genera el DROP.</para>
/// <para>El Down recrea tabla e índices desde el DDL 52, sin datos. Ese DDL también crea
/// <c>signs_physically</c> con <c>IF NOT EXISTS</c>, por lo que sus valores no cambian.</para>
/// </summary>
[DbContext(typeof(FlitDbContext))]
[Migration("20261105120000_HU14359_EliminarTablaConvenios")]
public partial class HU14359_EliminarTablaConvenios : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("DROP TABLE IF EXISTS admin.company_transit_office_agreements CASCADE;");

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(EmbeddedDdl.LoadUp("52-convenio-compania-organismo.sql"));
}
