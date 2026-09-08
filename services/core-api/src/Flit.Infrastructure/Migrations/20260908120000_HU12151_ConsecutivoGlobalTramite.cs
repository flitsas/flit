using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// HU #12151 (Feature #12150) — el radicado pasa de <c>TRM-{año}-{seq por tenant}</c> a un
    /// consecutivo GLOBAL asignado por <c>tramites.procedure_instance_reference_seq</c>.
    /// DDL en <c>Persistence/Sql/Ddl/103-HU12151-consecutivo-global-tramite.sql</c>.
    ///
    /// El script trae su propio guard: la renumeración solo corre si aún no existe el índice único
    /// global, así que re-aplicarlo no puede reasignar identificadores ya emitidos (AC5).
    ///
    /// El índice de expresión para ordenar por <c>::bigint</c> NO va aquí: en la misma transacción
    /// que la renumeración falla con «invalid input syntax for type bigint» porque el UPDATE es
    /// no-HOT y CREATE INDEX evalúa también las versiones viejas de fila. Va en la HU #12153, en su
    /// propia migración.
    /// </remarks>
    [DbContext(typeof(FlitDbContext))]
    [Migration("20260908120000_HU12151_ConsecutivoGlobalTramite")]
    public partial class HU12151_ConsecutivoGlobalTramite : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Sin BEGIN/COMMIT en el .sql: chocaría con la transacción de EF.
            migrationBuilder.Sql(EmbeddedDdl.LoadUp("103-HU12151-consecutivo-global-tramite.sql"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // El Down devuelve la ESTRUCTURA, no los radicados viejos: la renumeración es
            // irreversible (el valor original no se guarda en ninguna parte). Se deja explícito
            // para que nadie cuente con recuperarlos revirtiendo.
            migrationBuilder.Sql(
                """
                ALTER TABLE tramites.procedure_instances
                    ALTER COLUMN reference_number DROP DEFAULT;

                ALTER TABLE tramites.procedure_instances
                    DROP CONSTRAINT IF EXISTS ck_procedure_instances_reference_numerico;

                DROP INDEX IF EXISTS tramites.uq_procedure_instances_reference;

                ALTER TABLE tramites.procedure_instances
                    ADD CONSTRAINT uq_procedure_instances_tenant_reference
                    UNIQUE (tenant_id, reference_number);

                DROP SEQUENCE IF EXISTS tramites.procedure_instance_reference_seq;
                """);
        }
    }
}
