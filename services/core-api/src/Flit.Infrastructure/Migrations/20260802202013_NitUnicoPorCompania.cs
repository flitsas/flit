using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations
{
    /// <summary>
    /// Unicidad del NIT por compañía. El DDL embebido (<c>53-nit-unico-por-compania.sql</c>) crea el
    /// índice único sobre <c>identity.tenants.tax_id</c>, pero <b>solo si no hay duplicados</b>: a secas
    /// haría fallar la migración en cualquier base que ya los tenga y dejaría el despliegue a medias por
    /// un dato histórico que este cambio no puede arreglar solo. Si los hay, el DDL avisa por WARNING y
    /// el índice queda pendiente.
    ///
    /// <para>La puerta de entrada queda cerrada en ambos casos: <c>CreateCompanyHandler</c> rechaza un
    /// NIT repetido con 422 antes de llegar a la base.</para>
    /// </summary>
    public partial class NitUnicoPorCompania : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(EmbeddedDdl.LoadUp("53-nit-unico-por-compania.sql"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS identity.uq_tenants_tax_id;");
        }
    }
}
