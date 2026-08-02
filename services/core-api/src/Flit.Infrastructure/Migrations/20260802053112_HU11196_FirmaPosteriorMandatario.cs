using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations
{
    /// <summary>
    /// HU #11196 (ajuste tras validación manual) — la firma a posteriori también cubre al MANDATARIO. El
    /// DDL embebido (<c>51-HU11196-firma-posterior-mandatario.sql</c>) afloja
    /// <c>company_document_number</c> a NULL: el mandatario no representa a ninguna de las partes del
    /// trámite, así que no hay NIT representado que anotar y rellenarlo con un valor inventado haría que
    /// la traza afirmara un vínculo inexistente.
    ///
    /// <para>La entidad está <c>ExcludeFromMigrations</c> (el esquema lo lleva el DDL crudo, patrón del
    /// baúl), así que el scaffolding no emite nada para ella y el cuerpo se escribe a mano.</para>
    /// </summary>
    public partial class HU11196_FirmaPosteriorMandatario : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(EmbeddedDdl.LoadUp("51-HU11196-firma-posterior-mandatario.sql"));
        }

        /// <inheritdoc />
        /// <remarks>
        /// El <c>Down</c> NO restaura el <c>NOT NULL</c>: si ya existen marcas de mandatario, todas
        /// tienen esa columna en NULL y reimponer la restricción fallaría. Aflojarla es seguro de dejar.
        /// </remarks>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
