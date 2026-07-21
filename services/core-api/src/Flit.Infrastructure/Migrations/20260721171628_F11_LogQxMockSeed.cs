using System;
using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// Feature #10792 (LOG QX) — data-only. Siembra 5 radicaciones Quipux de ejemplo (una por cada
    /// estado: pendiente/registrado/aprobado/rechazado/fallido) con su trámite, placa, línea de tiempo
    /// completa (duración/origen/código en el detail, errores y reintentos, enmascarado de PII y
    /// eventos sin payload) para poder revisar el módulo LOG QX (?m=log-qx) sin la integración real.
    /// SOLO en DEV/QA (o FLIT_DEV_SEED), mismo gate que <c>FEATURE10707_AvaluoMockSeed</c> / HU10200.
    /// No modifica el modelo: el snapshot no cambia. El DDL embebido es idempotente y determinista.
    /// </remarks>
    public partial class F11_LogQxMockSeed : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Dato de prueba, no de negocio: no se siembra en producción real.
            if (!IsDevSeedEnabled())
            {
                return;
            }

            migrationBuilder.Sql(EmbeddedDdl.LoadUp("35-F11-log-qx-mock-seed.sql"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // El DELETE de las radicaciones cascada a sus eventos (FK ON DELETE CASCADE).
            migrationBuilder.Sql(@"
SET LOCAL row_security = off;

DELETE FROM tramites.quipux_submissions WHERE id IN (
  '5eed5b01-0000-4000-8000-000000000001','5eed5b01-0000-4000-8000-000000000002',
  '5eed5b01-0000-4000-8000-000000000003','5eed5b01-0000-4000-8000-000000000004',
  '5eed5b01-0000-4000-8000-000000000005');

ALTER TABLE tramites.procedure_instance_field_values DISABLE TRIGGER tr_procedure_instance_field_values_immutable;
DELETE FROM tramites.procedure_instance_field_values
 WHERE field_key = 'plate' AND procedure_instance_id IN (
  '5eed0001-0000-4000-8000-000000000001','5eed0001-0000-4000-8000-000000000002',
  '5eed0001-0000-4000-8000-000000000003','5eed0001-0000-4000-8000-000000000004',
  '5eed0001-0000-4000-8000-000000000005');
ALTER TABLE tramites.procedure_instance_field_values ENABLE TRIGGER tr_procedure_instance_field_values_immutable;

DELETE FROM tramites.procedure_instances WHERE id IN (
  '5eed0001-0000-4000-8000-000000000001','5eed0001-0000-4000-8000-000000000002',
  '5eed0001-0000-4000-8000-000000000003','5eed0001-0000-4000-8000-000000000004',
  '5eed0001-0000-4000-8000-000000000005');");
        }

        // Gate por entorno: siembra solo en Development/QA o con FLIT_DEV_SEED activo (patrón HU10200 /
        // FEATURE10707_AvaluoMockSeed).
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
