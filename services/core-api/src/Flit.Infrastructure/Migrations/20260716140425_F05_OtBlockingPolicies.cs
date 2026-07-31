using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// FEATURE 05 — política de bloqueo de preflight por criterio (soat/rtm/estado_vehiculo/fines/rnmc)
    /// y Organismo de Tránsito de la compañía. Tabla dispersa: solo existen filas para los pares
    /// (tenant, OT, criterio) que el admin tocó; ausencia de fila = default del criterio (sin backfill,
    /// cero cambio de comportamiento). El DDL embebido (34-F05-ot-blocking-policies.sql) trae, además
    /// de la tabla, el CHECK cerrado de <c>criterion</c>, el COMMENT que documenta la colisión de
    /// vocabulario, RLS estricta por tenant y los triggers de <c>row_version</c>/auditoría — piezas que
    /// <c>CreateTable</c> por sí solo no genera. Mismo patrón que F05_OtConsultationRestrictions.
    /// </remarks>
    public partial class F05_OtBlockingPolicies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(EmbeddedDdl.LoadUp("34-F05-ot-blocking-policies.sql"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "tenant_transit_office_blocking_policies",
                schema: "admin");
        }
    }
}
