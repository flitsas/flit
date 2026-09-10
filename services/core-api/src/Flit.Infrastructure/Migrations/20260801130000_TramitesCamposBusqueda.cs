using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations
{
    /// <remarks>
    /// Denormaliza en <c>tramites.procedure_instances</c> las columnas <c>vin</c>, <c>plate</c>,
    /// <c>vendedor_nombre</c> y <c>comprador_nombre</c> — hoy solo viven en las tablas hijas
    /// <c>procedure_instance_field_values</c> (field_key 'vin'/'plate') y <c>procedure_instance_actors</c>
    /// (actor_type 'vendedor'/'comprador'). Con paginación real (LIMIT/OFFSET) el listado de trámites ya
    /// no puede filtrar/ordenar en memoria: necesita WHERE/ORDER BY directo sobre la fila padre.
    ///
    /// Se mantienen con TRIGGER (no desde el aplicativo) para que cualquier escritor —API, jobs,
    /// migraciones de datos futuras— quede sincronizado sin depender de recordar el duplicado. No se usa
    /// un índice funcional porque los 4 valores viven en tablas HIJAS (relación 1:N): un índice funcional
    /// solo puede indexar una expresión de columnas de LA MISMA fila, así que denormalizar es la única
    /// forma de que el planner filtre/ordene sin una agregación por fila. Ver el detalle comentado
    /// (incluida esa justificación) en <c>Persistence/Sql/Ddl/47-tramites-campos-busqueda.sql</c>.
    /// </remarks>
    [DbContext(typeof(FlitDbContext))]
    [Migration("20260801130000_TramitesCamposBusqueda")]
    public partial class TramitesCamposBusqueda : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder) =>
            migrationBuilder.Sql(EmbeddedDdl.LoadUp("47-tramites-campos-busqueda.sql"));

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP TRIGGER IF EXISTS tr_procedure_instance_field_values_denorm ON tramites.procedure_instance_field_values;
                DROP TRIGGER IF EXISTS tr_procedure_instance_actors_denorm ON tramites.procedure_instance_actors;
                DROP FUNCTION IF EXISTS tramites.trg_procedure_instance_denorm_field_value();
                DROP FUNCTION IF EXISTS tramites.trg_procedure_instance_denorm_actor();

                DROP INDEX IF EXISTS tramites.ix_procedure_instances_tenant_id_vin;
                DROP INDEX IF EXISTS tramites.ix_procedure_instances_tenant_id_plate;
                DROP INDEX IF EXISTS tramites.ix_procedure_instances_tenant_id_comprador_nombre;
                DROP INDEX IF EXISTS tramites.ix_procedure_instances_tenant_id_vendedor_nombre;
                DROP INDEX IF EXISTS tramites.ix_procedure_instances_tenant_id_created_at;
                DROP INDEX IF EXISTS tramites.ix_procedure_instances_tenant_id_updated_at;
                DROP INDEX IF EXISTS tramites.ix_procedure_instances_tenant_id_created_by_user_id;

                ALTER TABLE tramites.procedure_instances
                  DROP COLUMN IF EXISTS vin,
                  DROP COLUMN IF EXISTS plate,
                  DROP COLUMN IF EXISTS vendedor_nombre,
                  DROP COLUMN IF EXISTS comprador_nombre;
                """);
        }
    }
}
