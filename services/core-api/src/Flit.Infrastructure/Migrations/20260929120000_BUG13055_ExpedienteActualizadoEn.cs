using Flit.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations;

/// <summary>
/// Bug #13055 — <c>tramites.procedure_instances.expediente_actualizado_en</c> (timestamptz, nulable,
/// sin default): último cambio de los datos que imprime el FUR, sellado por
/// <c>ConsolidadoVigenciaTracker</c>. Con ella el FUR y los consolidados se regeneran solos ante
/// cualquier cambio del expediente. Los trámites existentes quedan en null (sin cambios registrados):
/// la migración no rellena ni invalida nada. La tabla está <c>ExcludeFromMigrations</c>, por eso la
/// columna se agrega con SQL idempotente (<c>IF NOT EXISTS</c>).
/// </summary>
[DbContext(typeof(FlitDbContext))]
[Migration("20260929120000_BUG13055_ExpedienteActualizadoEn")]
public partial class BUG13055_ExpedienteActualizadoEn : Migration
{
    private const string UpSql =
        """
        ALTER TABLE tramites.procedure_instances
          ADD COLUMN IF NOT EXISTS expediente_actualizado_en timestamptz NULL;

        COMMENT ON COLUMN tramites.procedure_instances.expediente_actualizado_en IS
          'Bug #13055 - ultimo cambio de los datos que imprime el FUR (campos, actores, participantes, comercial, prenda, firmas, identidad). Un FUR anterior se regenera antes de consolidar. NULL = sin cambios registrados.';
        """;

    private const string DownSql =
        """
        ALTER TABLE tramites.procedure_instances
          DROP COLUMN IF EXISTS expediente_actualizado_en;
        """;

    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql(UpSql);

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql(DownSql);
}
