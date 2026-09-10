using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations
{
    /// <remarks>
    /// Consultas guardadas de la empresa gestora: el gemelo de <c>admin.ot_saved_queries</c>, del
    /// otro lado del trámite. Cada usuario arma sus propias búsquedas sobre los trámites que su
    /// empresa gestiona y las persiste para volver a ejecutarlas.
    ///
    /// <para>El DDL va en crudo porque lleva un índice único sobre una expresión —el nombre
    /// normalizado, para que dos consultas del mismo usuario no puedan llamarse igual— y eso no sabe
    /// expresarlo el generador de EF. Ver el detalle comentado en
    /// <c>Persistence/Sql/Ddl/58-consultas-empresa.sql</c>.</para>
    /// </remarks>
    public partial class ConsultasEmpresa : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder) =>
            migrationBuilder.Sql(EmbeddedDdl.LoadUp("58-consultas-empresa.sql"));

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) =>
            migrationBuilder.Sql("DROP TABLE IF EXISTS analytics.company_saved_queries;");
    }
}
