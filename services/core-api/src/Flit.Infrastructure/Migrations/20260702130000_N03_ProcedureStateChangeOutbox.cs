using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// N 03 (RNF01, ADR-0022) — outbox transaccional de cambios de estado del trámite.
    /// El DDL (22-n03-procedure-state-change-outbox.sql) crea tramites.procedure_state_change_outbox
    /// con RLS e índices (cola de pendientes published_at IS NULL). Igual que en TramitesKyverum,
    /// el snapshot reconcilia el modelo EF (no se emite CreateTable desde el MigrationBuilder);
    /// migración hand-authored: atributos inline y sin Designer (patrón HU #10198).
    /// </remarks>
    [DbContext(typeof(FlitDbContext))]
    [Migration("20260702130000_N03_ProcedureStateChangeOutbox")]
    public partial class N03_ProcedureStateChangeOutbox : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(EmbeddedDdl.LoadUp("22-n03-procedure-state-change-outbox.sql"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TABLE IF EXISTS tramites.procedure_state_change_outbox;");
        }
    }
}
