using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations
{
    /// <summary>
    /// HU #11201 (Feature #11190) — un mandatario para varios organismos de tránsito. El DDL embebido
    /// (48-HU11201-mandate-signer-transit-offices.sql) crea el puente
    /// <c>admin.mandate_signer_transit_offices</c> y hace backfill desde el organismo que hoy vive en
    /// cada mandatario, de modo que los existentes conservan el suyo (AC4).
    ///
    /// <para>La entidad del puente está <c>ExcludeFromMigrations</c> (el esquema lo lleva el DDL crudo,
    /// patrón del baúl), así que el scaffolding no emite <c>CreateTable</c>.</para>
    ///
    /// <para><b>Nota sobre el snapshot.</b> Al regenerarlo apareció pendiente un
    /// <c>AddColumn plate_flow_skip_to_terminado</c> ajeno a esta HU: esa columna ya la crea por SQL la
    /// migración <c>20260729140000_PlateFlowTerminado</c>, pero su snapshot no se actualizó y EF la
    /// seguía viendo como pendiente. Aquí se pone al día el snapshot SIN emitir el <c>AddColumn</c>
    /// —volver a crearla fallaría—; el esquema real no cambia por eso.</para>
    /// </summary>
    public partial class HU11201_MandateSignerTransitOffices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(EmbeddedDdl.LoadUp("48-HU11201-mandate-signer-transit-offices.sql"));
        }

        /// <inheritdoc />
        /// <remarks>
        /// Reversible sin pérdida de lo anterior: el organismo primario sigue viviendo en
        /// <c>mandate_signers</c>, así que basta con retirar el puente. Lo único que se pierde son los
        /// organismos ADICIONALES asignados después del cambio — inevitable, porque el modelo anterior
        /// no sabe representarlos.
        /// </remarks>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TABLE IF EXISTS admin.mandate_signer_transit_offices;");
        }
    }
}
