using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations
{
    /// <summary>
    /// Convenio comercial compañía ↔ organismo de tránsito y firma física del mandatario por organismo.
    /// El DDL embebido (<c>52-convenio-compania-organismo.sql</c>) crea
    /// <c>admin.company_transit_office_agreements</c> y agrega
    /// <c>admin.mandate_signer_transit_offices.signs_physically</c>.
    ///
    /// <para>Ambas entidades están <c>ExcludeFromMigrations</c> (el esquema lo lleva el DDL crudo, patrón
    /// del baúl y de los puentes del mandatario), así que el scaffolding no emite nada y el cuerpo se
    /// escribe a mano.</para>
    /// </summary>
    public partial class ConvenioCompaniaOrganismo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(EmbeddedDdl.LoadUp("52-convenio-compania-organismo.sql"));
        }

        /// <inheritdoc />
        /// <remarks>
        /// Reversible: sin convenios el mandato vuelve a llevar SIEMPRE bloque de firma del mandatario,
        /// que es el comportamiento anterior. La columna <c>signs_physically</c> se conserva a propósito:
        /// quitarla perdería la marca de quién firma a mano, y su default <c>false</c> ya reproduce la
        /// conducta previa.
        /// </remarks>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TABLE IF EXISTS admin.company_transit_office_agreements;");
        }
    }
}
