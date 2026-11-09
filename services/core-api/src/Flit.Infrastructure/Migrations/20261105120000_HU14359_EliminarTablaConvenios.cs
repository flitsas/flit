using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations;

/// <summary>
/// HU #14359 (Feature #14356) — elimina <c>admin.company_transit_office_agreements</c> con sus convenios.
///
/// <para><b>Despliegue:</b> sale en la misma imagen que retira el código de convenios (#14357 y #14358) y corre al
/// arrancar con <c>Database:AutoMigrate</c>. Antes de desplegar, comprobar en cada ambiente que la tabla no tiene filas
/// con <c>is_active = true</c>, porque el Up borra los convenios y el Down no los restaura.</para>
///
/// <para><b>Volver a una imagen anterior:</b> esa imagen lee la tabla, así que antes hay que correr el Down con destino
/// <c>20261105120000_HU14317_CanalTramite</c>. Ese destino también revierte HU14318 y HU14319.</para>
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
