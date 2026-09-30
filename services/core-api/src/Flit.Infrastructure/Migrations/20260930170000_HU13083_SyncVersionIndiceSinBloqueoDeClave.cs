using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations;

/// <summary>
/// HU #13083 (Feature #13066, Épica #12737) — el índice único de <c>sync_version</c> pasa a ser parcial para
/// que el sello de sincronización no tome el bloqueo de clave (FOR UPDATE) y deje de provocar deadlocks con las
/// escrituras en tablas hijas. DDL: <c>128-HU13083-sync-version-indice-sin-bloqueo-de-clave.sql</c>.
/// </summary>
[DbContext(typeof(FlitDbContext))]
[Migration("20260930170000_HU13083_SyncVersionIndiceSinBloqueoDeClave")]
public partial class HU13083_SyncVersionIndiceSinBloqueoDeClave : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(EmbeddedDdl.LoadUp("128-HU13083-sync-version-indice-sin-bloqueo-de-clave.sql"));

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(
            """
            DROP INDEX IF EXISTS tramites.uq_procedure_instances_sync_version;
            CREATE UNIQUE INDEX uq_procedure_instances_sync_version ON tramites.procedure_instances (sync_version);
            """);
}
