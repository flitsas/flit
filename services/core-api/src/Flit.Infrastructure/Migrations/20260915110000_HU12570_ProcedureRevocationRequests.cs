using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations
{
    /// <remarks>
    /// HU #12570 (Feature #12565) — tabla propia <c>tramites.procedure_revocation_requests</c>
    /// para acumular todos los intentos de revocatoria de un trámite Aprobado sin sobrescribir
    /// tramites.procedure_instances ni tocar TramiteEstado/TramiteStateMachine (ADR-0022): el
    /// trámite permanece 'Aprobado' durante todo este sub-flujo. No reutiliza
    /// <c>subsanacion_activa</c> ni <c>SttWorkflow</c> (código muerto) — el ciclo de vida de
    /// reintentos vive únicamente aquí vía <c>attempt_number</c>, mismo espíritu ortogonal que
    /// PlateFlowStatus/PlateFlowStateMachine.
    ///
    /// DDL en <c>Persistence/Sql/Ddl/115-HU12570-procedure-revocation-requests.sql</c>: tabla con
    /// RLS por tenant (mismo patrón <c>tenant_isolation</c> que el resto de <c>tramites</c>),
    /// CHECK de estado y de consistencia de decisión, unicidad (procedure_instance_id,
    /// attempt_number) y el índice único parcial que exige el AC2 (como máximo una solicitud
    /// activa —'solicitada'|'en_revision'— por procedure_instance_id), y triggers estándar
    /// row_version + audit_log.
    ///
    /// Migración de solo SQL crudo, sin entidad EF asociada (eso lo agrega el backend-agent junto
    /// con el repositorio/use case) — por eso usa atributos [DbContext]/[Migration] inline en vez
    /// de Designer.cs, igual que 20260728200000_SubsanacionFlag.
    /// </remarks>
    [DbContext(typeof(FlitDbContext))]
    [Migration("20260915110000_HU12570_ProcedureRevocationRequests")]
    public partial class HU12570_ProcedureRevocationRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder) =>
            migrationBuilder.Sql(EmbeddedDdl.LoadUp("115-HU12570-procedure-revocation-requests.sql"));

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) =>
            migrationBuilder.Sql(
                """
                DROP TRIGGER IF EXISTS tr_procedure_revocation_requests_audit ON tramites.procedure_revocation_requests;
                DROP TRIGGER IF EXISTS tr_procedure_revocation_requests_row_version ON tramites.procedure_revocation_requests;
                DROP POLICY IF EXISTS tenant_isolation ON tramites.procedure_revocation_requests;

                DROP TABLE IF EXISTS tramites.procedure_revocation_requests;
                """);
    }
}
