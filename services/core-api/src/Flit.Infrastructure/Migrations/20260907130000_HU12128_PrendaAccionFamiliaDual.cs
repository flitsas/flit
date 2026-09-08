using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations;

/// <summary>
/// HU #12128 (Feature #10585 / ADR-0055, Opción B) — agrega <c>accion_familia</c> a
/// <c>tramites.procedure_instance_prenda</c> (derivada de <c>decision</c>: constitucion vs.
/// levantamiento) y reemplaza <c>uq_procedure_instance_prenda_vigente</c> por un índice único
/// parcial sobre <c>(procedure_instance_id, accion_familia)</c>: permite hasta dos hechos vigentes
/// por instancia (uno por familia), nunca dos de la misma. Matrícula/Traspaso no cambian de
/// comportamiento real (no activan la acción complementaria; eso lo gatea la aplicación en
/// HU-DOM-APP #12129). DDL: <c>103-prenda-accion-familia-dual.sql</c>.
/// </summary>
[DbContext(typeof(FlitDbContext))]
[Migration("20260907130000_HU12128_PrendaAccionFamiliaDual")]
public partial class HU12128_PrendaAccionFamiliaDual : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(EmbeddedDdl.LoadUp("103-prenda-accion-familia-dual.sql"));

    /// <inheritdoc />
    /// <remarks>
    /// Rollback condicionado: solo es seguro si ninguna instancia quedó con dos hechos vigentes
    /// simultáneos (el propósito mismo de esta HU). Con dos vigentes reales, recrear el índice
    /// antiguo (una sola vigente por instancia) violaría la constraint sobre datos ya existentes;
    /// el Down aborta explícitamente en vez de truncar filas en silencio. La columna
    /// <c>accion_familia</c> es puramente derivada de <c>decision</c>, así que eliminarla no pierde
    /// información: <c>decision</c> permanece intacta.
    /// </remarks>
    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("""
            DO $$
            DECLARE
                duplicadas int;
            BEGIN
                SELECT count(*) INTO duplicadas
                  FROM (
                      SELECT procedure_instance_id
                        FROM tramites.procedure_instance_prenda
                       WHERE estado = 'vigente'
                       GROUP BY procedure_instance_id
                      HAVING count(*) > 1
                  ) dup;

                IF duplicadas > 0 THEN
                    RAISE EXCEPTION
                        'Rollback de HU #12128 abortado: % instancia(s) tienen dos hechos de prenda '
                        'vigentes simultáneos. El Down solo es seguro cuando cada instancia tiene a '
                        'lo sumo una fila vigente (comportamiento anterior a esta HU).', duplicadas;
                END IF;
            END $$;

            DROP INDEX IF EXISTS tramites.uq_procedure_instance_prenda_vigente_familia;

            CREATE UNIQUE INDEX IF NOT EXISTS uq_procedure_instance_prenda_vigente
                ON tramites.procedure_instance_prenda (procedure_instance_id)
                WHERE estado = 'vigente';

            ALTER TABLE tramites.procedure_instance_prenda
                DROP CONSTRAINT IF EXISTS ck_procedure_instance_prenda_accion_familia;

            ALTER TABLE tramites.procedure_instance_prenda
                DROP COLUMN IF EXISTS accion_familia;
            """);
}
