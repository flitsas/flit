using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// Quipux (HU #10774) — amplía el CHECK de <c>tramites.quipux_submission_events.stage</c> con
    /// <c>reintento_manual</c> y <c>cancelado_manual</c>, las dos acciones de operación de la consola
    /// de cola QX (re-encolar un <c>fallido</c>, cancelar un <c>pendiente</c>). Sin ellas el INSERT de
    /// la bitácora de esas acciones fallaría el CHECK y el evento se perdería en silencio (la
    /// bitácora traga excepciones), dejando un hueco en el timeline del trámite.
    ///
    /// <para>Migración aparte y no edición de <c>32-HU10710</c>: ese DDL ya está aplicado y editarlo
    /// no lo re-ejecuta (EF solo mira <c>__EFMigrationsHistory</c>). En una base nueva 32 ya trae el
    /// CHECK ampliado y este ALTER es idempotente (<c>DROP CONSTRAINT IF EXISTS</c>).</para>
    /// </remarks>
    // Atributos inline: como el resto de migraciones Quipux, no hay .Designer.cs (el diff EF sale
    // vacío — las tablas están ExcludeFromMigrations y el DDL lo lleva el .sql embebido). Sin ellos
    // EF no descubre la migración y el ALTER nunca corre.
    [DbContext(typeof(FlitDbContext))]
    [Migration("20260716120000_HU10774_QuipuxManualStages")]
    public partial class HU10774_QuipuxManualStages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(EmbeddedDdl.LoadUp("34-HU10774-quipux-manual-stages.sql"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Restaura el CHECK original (sin los dos stages manuales). Down solo es seguro si no
            // quedan filas con esos stages; en un rollback real habría que purgarlas antes.
            migrationBuilder.Sql(
                """
                ALTER TABLE tramites.quipux_submission_events
                  DROP CONSTRAINT IF EXISTS quipux_submission_events_stage_check;

                ALTER TABLE tramites.quipux_submission_events
                  ADD CONSTRAINT quipux_submission_events_stage_check
                  CHECK (stage IN ('claimed','consolidado_generado','s3_subido',
                                   'registro_enviado','registro_respuesta','registro_error',
                                   'consulta_enviada','consulta_respuesta','consulta_error',
                                   'aprobado','rechazado','dead_letter'));
                """);
        }
    }
}
