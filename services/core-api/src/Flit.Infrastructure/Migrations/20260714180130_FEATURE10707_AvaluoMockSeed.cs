using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// Feature #10707 (HU #10708) — data-only. Cablea el proveedor <c>fasecolda</c> en la fuente
    /// externa FASECOLDA (todas las envs) y siembra valores de referencia de avalúo de prueba
    /// SOLO en DEV/QA (gate por entorno, patrón HU10200). No modifica el modelo: el snapshot no cambia.
    /// Idempotente (UPDATE + ON CONFLICT DO NOTHING) y reversible.
    /// </remarks>
    public partial class FEATURE10707_AvaluoMockSeed : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Binding del proveedor de avalúo en la fuente externa ya sembrada (HU10151).
            // Catálogo global (sin tenant); aplica en todos los entornos.
            migrationBuilder.Sql(@"
UPDATE tramites.external_data_sources
   SET external_refs = jsonb_set(COALESCE(external_refs, '{}'::jsonb), '{provider}', '""fasecolda""'::jsonb, true),
       updated_at = now()
 WHERE code = 'FASECOLDA';");

            // Datos de prueba de avalúo: solo DEV/QA (no producción). Habilitan el modo mock
            // de los proveedores sin credenciales reales. Fixtures validados contra la API real.
            if (IsDevSeedEnabled())
            {
                migrationBuilder.Sql(@"
INSERT INTO tramites.avaluo_mock_values (id, match_key, source, value_cop) VALUES
    (uuidv7(), '93Y9SR333RJ563653', 'fasecolda',     105600000),
    (uuidv7(), '93Y9SR333RJ563653', 'base_gravable',  98000000),
    (uuidv7(), '93Y9SR333RJ563653', 'mercado_libre', 112000000),
    (uuidv7(), '1FTFW1ET5DFC12345', 'fasecolda',     119900000),
    (uuidv7(), '1FTFW1ET5DFC12345', 'base_gravable', 110000000),
    (uuidv7(), '1FTFW1ET5DFC12345', 'mercado_libre', 125000000)
ON CONFLICT (match_key, source) DO NOTHING;");
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"DELETE FROM tramites.avaluo_mock_values
 WHERE match_key IN ('93Y9SR333RJ563653', '1FTFW1ET5DFC12345');");

            migrationBuilder.Sql(@"
UPDATE tramites.external_data_sources
   SET external_refs = external_refs - 'provider', updated_at = now()
 WHERE code = 'FASECOLDA';");
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
