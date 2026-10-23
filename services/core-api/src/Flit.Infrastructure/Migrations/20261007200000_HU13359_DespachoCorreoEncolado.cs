using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations;

/// <summary>
/// HU #13359 — los cupos de aviso de correo (cambio de estado, asignación de placa, revocatoria) admiten el estado
/// <c>encolado</c>: core-api ya no entrega, encola para Notificaciones. DDL: <c>133-HU13359-despacho-correo-encolado.sql</c>.
/// Solo amplía el CHECK de <c>status</c>; el modelo de EF no cambia.
/// </summary>
[DbContext(typeof(FlitDbContext))]
[Migration("20261007200000_HU13359_DespachoCorreoEncolado")]
public partial class HU13359_DespachoCorreoEncolado : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(EmbeddedDdl.LoadUp("133-HU13359-despacho-correo-encolado.sql"));

    /// <inheritdoc />
    /// <remarks>
    /// Las filas <c>encolado</c> pasan a <c>enviado</c> (lo que el código viejo escribía en ese caso) antes de volver
    /// al CHECK anterior; si no, el CHECK no se podría crear.
    /// </remarks>
    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("""
            UPDATE tramites.procedure_state_change_email_dispatches SET status = 'enviado' WHERE status = 'encolado';
            UPDATE tramites.plate_assignment_email_dispatches SET status = 'enviado' WHERE status = 'encolado';
            UPDATE tramites.revocation_request_email_dispatches SET status = 'enviado' WHERE status = 'encolado';
            ALTER TABLE tramites.procedure_state_change_email_dispatches DROP CONSTRAINT IF EXISTS ck_psce_dispatches_status;
            ALTER TABLE tramites.procedure_state_change_email_dispatches ADD CONSTRAINT ck_psce_dispatches_status
                CHECK (status IN ('pendiente', 'enviado', 'fallido', 'omitido'));
            ALTER TABLE tramites.plate_assignment_email_dispatches DROP CONSTRAINT IF EXISTS ck_pae_dispatches_status;
            ALTER TABLE tramites.plate_assignment_email_dispatches ADD CONSTRAINT ck_pae_dispatches_status
                CHECK (status IN ('pendiente', 'enviado', 'fallido', 'omitido'));
            ALTER TABLE tramites.revocation_request_email_dispatches DROP CONSTRAINT IF EXISTS ck_rre_dispatches_status;
            ALTER TABLE tramites.revocation_request_email_dispatches ADD CONSTRAINT ck_rre_dispatches_status
                CHECK (status IN ('pendiente', 'enviado', 'fallido', 'omitido'));
            """);
}
