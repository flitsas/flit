using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations
{
    /// <remarks>
    /// Consultas guardadas sobre pre-trámites de ICT: el gemelo de
    /// <c>analytics.company_saved_queries</c>, mismo alcance empresa + usuario, para el pipeline de
    /// Integración con Terceros.
    ///
    /// <para>El DDL va en crudo por la misma razón que <c>ConsultasEmpresa</c>: lleva un índice
    /// único sobre una expresión —el nombre normalizado— que el generador de EF no sabe expresar.
    /// Ver el detalle comentado en <c>Persistence/Sql/Ddl/77-ict-consultas-guardadas.sql</c>.</para>
    ///
    /// <para>Esta migración NO toca <c>report_schedules</c> ni <c>catalogs.vehicle_service_types</c>:
    /// el scaffolding automático de <c>dotnet ef migrations add</c> las incluyó por un desfase
    /// preexistente entre el modelo EF y el snapshot (esas tablas/columnas ya existen en la base,
    /// creadas por migraciones SQL crudas anteriores — <c>69-vehicle-service-types-catalog.sql</c> y
    /// <c>76-reportes-programados-alertas-ot.sql</c>). Se retiraron de <c>Up</c>/<c>Down</c> para que
    /// esta migración sea exclusivamente la de <c>ict_saved_queries</c>; el snapshot del modelo
    /// (<c>.Designer.cs</c>) sí queda al día, que es lo que corrige ese desfase sin re-ejecutar DDL
    /// que ya corrió.</para>
    /// </remarks>
    public partial class IctConsultasGuardadas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder) =>
            migrationBuilder.Sql(EmbeddedDdl.LoadUp("77-ict-consultas-guardadas.sql"));

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) =>
            migrationBuilder.Sql("DROP TABLE IF EXISTS analytics.ict_saved_queries;");
    }
}
