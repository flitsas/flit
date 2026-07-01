using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// La matrícula inicial debe nacer APAGADA: una compañía debe habilitarla explícitamente en su
    /// configuración. Antes <c>admin.tenant_operational_policies.allow_initial_registration</c> era
    /// <c>NOT NULL DEFAULT true</c>, por lo que un tenant recién creado (o sin configurar) quedaba
    /// permisivo. Esta migración cambia el DEFAULT de la columna a <c>false</c> (nuevas filas nacen
    /// apagadas) y normaliza a <c>false</c> cualquier valor ausente.
    ///
    /// Las compañías <em>sin fila</em> de configuración no se ven afectadas aquí (no hay registro que
    /// actualizar); ese caso lo cubre la validación del endpoint de creación de trámite, que ahora
    /// trata "sin configuración" como matrícula inicial NO permitida. Las compañías ya configuradas
    /// conservan su valor explícito (no se apagan en masa).
    /// </remarks>
    public partial class MatriculaInicialDefaultOff : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                -- Nuevas filas: matrícula inicial apagada por defecto (antes DEFAULT true).
                ALTER TABLE admin.tenant_operational_policies
                  ALTER COLUMN allow_initial_registration SET DEFAULT false;

                -- Normaliza valores ausentes a false. La columna es NOT NULL, así que normalmente son
                -- 0 filas (defensivo). Las compañías ya configuradas conservan su valor explícito.
                UPDATE admin.tenant_operational_policies
                   SET allow_initial_registration = false
                 WHERE allow_initial_registration IS NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                ALTER TABLE admin.tenant_operational_policies
                  ALTER COLUMN allow_initial_registration SET DEFAULT true;
                """);
        }
    }
}
