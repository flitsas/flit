using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations;

/// <summary>
/// HU #14412 (Feature #14408) — elimina <c>admin.company_document_params</c> con sus filas, sin exportarlas.
///
/// <para><b>Despliegue:</b> sale en la misma imagen que retira el código de preferencias (#14409, #14410 y #14411) y
/// corre al arrancar con <c>Database:AutoMigrate</c>. Antes de desplegar en producción se confirma que
/// <c>SELECT count(*) FROM admin.company_document_params</c> da 0. Si da otro valor, no se despliega y se escala al PO.</para>
///
/// <para><b>Volver a una imagen anterior:</b> esa imagen lee la tabla, así que antes hay que correr el Down con destino
/// <c>20261105140000_HU14319_CanalEnReporteDetallado</c>. Las filas no se recuperan.</para>
///
/// <para>A mano y sin Designer: la entidad estaba <c>ExcludeFromMigrations</c>, así que EF no genera el DROP.</para>
/// <para>Sin <c>CASCADE</c>: una dependencia inesperada hace fallar el Up en vez de borrarse en silencio.</para>
/// <para>El Down recrea tabla, restricciones e índices desde el DDL 24, sin datos.</para>
/// </summary>
[DbContext(typeof(FlitDbContext))]
[Migration("20261106120000_HU14412_EliminarTablaPreferenciasDocumentos")]
public partial class HU14412_EliminarTablaPreferenciasDocumentos : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("DROP TABLE IF EXISTS admin.company_document_params;");

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(EmbeddedDdl.LoadUp("24-HU10521-company-document-params.sql"));
}
