using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Consultas.Api.Persistence.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// HU #13348 (Epic #13316): los valores de avalúo del modo mock pasan de <c>tramites.avaluo_mock_values</c> al esquema
    /// de Consultas. Siembra los fixtures de la Feature #10707 solo en DEV/QA (mismo gate); los que se hayan agregado a
    /// mano en un ambiente los copia <c>deploy/postgres/migrar-valores-mock-avaluo.sql</c>.
    /// </remarks>
    public partial class HU13348_ValoresMockAvaluo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "valores_mock_avaluo",
                schema: "consultas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    clave = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    fuente = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    valor_cop = table.Column<decimal>(type: "numeric(15,2)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_valores_mock_avaluo", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "uq_valores_mock_avaluo",
                schema: "consultas",
                table: "valores_mock_avaluo",
                columns: new[] { "clave", "fuente" },
                unique: true);

            if (IsDevSeedEnabled())
            {
                migrationBuilder.Sql(@"
INSERT INTO consultas.valores_mock_avaluo (id, clave, fuente, valor_cop) VALUES
    (gen_random_uuid(), '93Y9SR333RJ563653', 'fasecolda',     105600000),
    (gen_random_uuid(), '93Y9SR333RJ563653', 'base_gravable',  98000000),
    (gen_random_uuid(), '93Y9SR333RJ563653', 'mercado_libre', 112000000),
    (gen_random_uuid(), '1FTFW1ET5DFC12345', 'fasecolda',     119900000),
    (gen_random_uuid(), '1FTFW1ET5DFC12345', 'base_gravable', 110000000),
    (gen_random_uuid(), '1FTFW1ET5DFC12345', 'mercado_libre', 125000000)
ON CONFLICT (clave, fuente) DO NOTHING;");
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "valores_mock_avaluo",
                schema: "consultas");
        }

        // Gate por entorno: siembra solo en Development/QA o con FLIT_DEV_SEED activo (patrón HU10200).
        private static bool IsDevSeedEnabled()
        {
            var env = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
            if (string.Equals(env, "Development", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(env, "QA", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var flag = Environment.GetEnvironmentVariable("FLIT_DEV_SEED");
            return string.Equals(flag, "1", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(flag, "true", StringComparison.OrdinalIgnoreCase);
        }
    }
}
