using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations
{
    /// <summary>
    /// Para qué empresas representadas firma un mandatario en cada organismo. El DDL embebido
    /// (<c>54-mandatario-empresas-representadas.sql</c>) crea
    /// <c>admin.mandate_signer_represented_companies</c> con su único parcial y sus índices de lectura.
    ///
    /// <para>La entidad está <c>ExcludeFromMigrations</c> (el esquema lo lleva el DDL crudo, patrón del
    /// resto de puentes del mandatario), así que el scaffolding no emite nada y el cuerpo se escribe a
    /// mano.</para>
    /// </summary>
    public partial class MandatarioEmpresasRepresentadas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(EmbeddedDdl.LoadUp("54-mandatario-empresas-representadas.sql"));
        }

        /// <inheritdoc />
        /// <remarks>
        /// Reversible sin pérdida funcional: sin asociaciones, todos los mandatarios vuelven a aplicar a
        /// todas las empresas, que es el comportamiento anterior. Lo que se pierde es la acotación.
        /// </remarks>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TABLE IF EXISTS admin.mandate_signer_represented_companies;");
        }
    }
}
