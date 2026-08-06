using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations
{
    /// <remarks>
    /// Consultas guardadas del organismo: cada usuario arma sus propias búsquedas sobre los trámites
    /// y las persiste para volver a ejecutarlas.
    ///
    /// <para>El DDL va en crudo, como el resto de <c>admin.*</c>, porque lleva un índice único sobre
    /// una expresión —el nombre normalizado, para que dos consultas del mismo usuario no puedan
    /// llamarse igual— y los triggers de <c>row_version</c> y auditoría. Nada de eso sabe expresarlo
    /// el generador de EF. Ver el detalle comentado en
    /// <c>Persistence/Sql/Ddl/57-ot-consultas-guardadas.sql</c>.</para>
    /// </remarks>
    public partial class OtConsultasGuardadas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder) =>
            migrationBuilder.Sql(EmbeddedDdl.LoadUp("57-ot-consultas-guardadas.sql"));

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) =>
            migrationBuilder.Sql("DROP TABLE IF EXISTS admin.ot_saved_queries;");
    }
}
