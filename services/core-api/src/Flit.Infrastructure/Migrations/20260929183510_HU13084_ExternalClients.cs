using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// HU #13084 (Feature #13065, Épica #12737) — schema <c>integrations</c> y tabla
    /// <c>external_clients</c>: clientes de integración externos, calcados del modelo de
    /// <c>ict.integration_clients</c> pero sin compañía. La entidad está <c>ExcludeFromMigrations</c>:
    /// el esquema lo crea el DDL <c>125-HU13084-integrations-external-clients.sql</c>; esta migración
    /// solo lo aplica y deja la entidad en el snapshot del modelo.
    /// </remarks>
    public partial class HU13084_ExternalClients : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(EmbeddedDdl.LoadUp("125-HU13084-integrations-external-clients.sql"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP TABLE IF EXISTS integrations.external_clients;
                DROP SCHEMA IF EXISTS integrations;
                """);
        }
    }
}
