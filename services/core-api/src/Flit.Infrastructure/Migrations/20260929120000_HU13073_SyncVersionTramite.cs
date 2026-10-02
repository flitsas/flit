using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations;

/// <summary>
/// HU #13073 (Feature #13062, Épica #12737) — versión de sincronización en el trámite:
/// <c>sync_version</c> (secuencia global <c>tramites.procedure_sync_seq</c>) y <c>sync_changed_at</c>,
/// asignadas por el trigger <c>tr_procedure_instances_sync_stamp</c>. Sin cambios en el modelo de EF:
/// la aplicación nunca escribe estas columnas. DDL: <c>122-HU13073-sync-version-tramite.sql</c>.
/// </summary>
[DbContext(typeof(FlitDbContext))]
[Migration("20260929120000_HU13073_SyncVersionTramite")]
public partial class HU13073_SyncVersionTramite : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(EmbeddedDdl.LoadUp("122-HU13073-sync-version-tramite.sql"));

    /// <inheritdoc />
    /// <remarks>
    /// Quitar la columna se lleva también la secuencia (OWNED BY); el DROP SEQUENCE explícito cubre
    /// el caso de que la columna ya no existiera.
    /// </remarks>
    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(
            """
            DROP TRIGGER IF EXISTS tr_procedure_instances_sync_stamp ON tramites.procedure_instances;
            DROP FUNCTION IF EXISTS tramites.trg_procedure_sync_stamp();
            ALTER TABLE tramites.procedure_instances
                DROP COLUMN IF EXISTS sync_changed_at,
                DROP COLUMN IF EXISTS sync_version;
            DROP SEQUENCE IF EXISTS tramites.procedure_sync_seq;
            """);
}
