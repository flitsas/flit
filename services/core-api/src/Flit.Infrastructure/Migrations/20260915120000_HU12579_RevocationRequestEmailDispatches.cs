using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations
{
    /// <remarks>
    /// HU #12579 (Feature #12565) — esquema de <c>tramites.revocation_request_email_dispatches</c>
    /// (DDL 116): cola de despachos de correo por hito del sub-flujo de revocatoria
    /// (solicitada|aprobada|rechazada), ADR-0046 (Opción B) extendido al sub-flujo de revocatoria.
    ///
    /// Migración de solo SQL crudo, sin Designer.cs — mismo criterio que
    /// 20260812140000_PlateAssignmentEmailDispatches y 20260915110000_HU12570_ProcedureRevocationRequests
    /// (ver memoria del repo: toda migración DDL-only necesita ser descubrible por EF vía atributos
    /// [DbContext]/[Migration] inline o nunca corre en el arranque).
    /// </remarks>
    [DbContext(typeof(FlitDbContext))]
    [Migration("20260915120000_HU12579_RevocationRequestEmailDispatches")]
    public partial class HU12579_RevocationRequestEmailDispatches : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder) =>
            migrationBuilder.Sql(EmbeddedDdl.LoadUp("116-HU12579-revocation-request-email-dispatch.sql"));

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) =>
            migrationBuilder.Sql(
                """
                DROP POLICY IF EXISTS tenant_isolation ON tramites.revocation_request_email_dispatches;
                DROP TABLE IF EXISTS tramites.revocation_request_email_dispatches;
                """);
    }
}
