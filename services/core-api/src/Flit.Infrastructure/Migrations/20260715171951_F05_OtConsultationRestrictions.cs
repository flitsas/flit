using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// HU #10759 — restricciones de consulta (RNMC, comparendos) por Organismo de Tránsito
    /// puntual de la compañía. Tabla dispersa: solo existen filas para los pares (tenant, OT,
    /// kind) que el admin tocó explícitamente; ausencia de fila = permisivo (sin backfill).
    /// El DDL embebido (33-F05-ot-consultation-restrictions.sql) trae, además de la tabla, el
    /// CHECK cerrado de <c>consultation_kind</c>, el COMMENT que documenta la colisión de
    /// vocabulario con <c>ConsultationKind</c> de Tramites, RLS estricta por tenant y los
    /// triggers de <c>row_version</c>/auditoría — piezas que <c>CreateTable</c> por sí solo no
    /// genera. Mismo patrón que F02_FinesQuerySource.
    /// </remarks>
    public partial class F05_OtConsultationRestrictions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(EmbeddedDdl.LoadUp("33-F05-ot-consultation-restrictions.sql"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "tenant_transit_office_consultation_restrictions",
                schema: "admin");
        }
    }
}
