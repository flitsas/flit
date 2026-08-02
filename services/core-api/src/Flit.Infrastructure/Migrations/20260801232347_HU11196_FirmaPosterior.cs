using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations
{
    /// <summary>
    /// HU #11196 (Feature #11188) — firma a posteriori. El DDL embebido
    /// (<c>49-HU11196-firma-posterior.sql</c>) crea <c>tramites.deferred_signature_marks</c> con su RLS,
    /// su CHECK de estado y el índice parcial que garantiza UNA sola marca pendiente por (trámite, parte).
    ///
    /// <para>La entidad está <c>ExcludeFromMigrations</c> (el esquema lo lleva el DDL crudo, patrón del
    /// baúl), así que el scaffolding no emite <c>CreateTable</c> para ella.</para>
    ///
    /// <para><b>Nota sobre el snapshot.</b> Al regenerarlo aparecieron pendientes dos cambios AJENOS a
    /// esta HU y traídos de <c>develop</c>: la columna
    /// <c>admin.tenant_operational_policies.validate_soat_with_runt</c> y la tabla
    /// <c>admin.user_ui_preferences</c>. Ambas las crean ya por SQL las migraciones
    /// <c>20260801110000_SoatValidationModePorCompania</c> y <c>20260801120000_UserUiPreferences</c>,
    /// cuyos snapshots no se actualizaron entonces. Aquí se pone al día el snapshot SIN emitir el
    /// <c>AddColumn</c> ni el <c>CreateTable</c> —volver a crearlos fallaría—; el esquema real no cambia
    /// por eso. Es la misma deriva que ya se corrigió con <c>plate_flow_skip_to_terminado</c> en la
    /// HU #11201.</para>
    /// </summary>
    public partial class HU11196_FirmaPosterior : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(EmbeddedDdl.LoadUp("49-HU11196-firma-posterior.sql"));
        }

        /// <inheritdoc />
        /// <remarks>
        /// Reversible sin pérdida funcional: las marcas son un registro de intención, no un dato del
        /// trámite. Lo ya firmado por el lote sigue firmado; lo que se pierde es la traza de qué trámites
        /// esperaban a qué persona.
        /// </remarks>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TABLE IF EXISTS tramites.deferred_signature_marks;");
        }
    }
}
