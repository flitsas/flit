using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations;

/// <summary>
/// HU #13075 (Feature #13062, Épica #12737) — índices que usa la lectura del feed de sincronización
/// externa: recorrido por versión, alcance «radicado», fecha de aprobación y factura más reciente.
/// DDL: <c>124-HU13075-sync-indices.sql</c>.
/// </summary>
[DbContext(typeof(FlitDbContext))]
[Migration("20260929140000_HU13075_SyncIndices")]
public partial class HU13075_SyncIndices : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(EmbeddedDdl.LoadUp("124-HU13075-sync-indices.sql"));

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(
            """
            DROP INDEX IF EXISTS tramites.ix_pi_attachments_factura;
            DROP INDEX IF EXISTS tramites.ix_pi_status_history_aprobado;
            DROP INDEX IF EXISTS tramites.ix_pi_status_history_radicado;
            DROP INDEX IF EXISTS tramites.uq_procedure_instances_sync_version;
            """);
}
