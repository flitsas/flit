using System;
using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// HU #10240 — Analítica · Seed DEV y refresh de agregados desde trámites | Feature #10139.
    ///
    /// Dos artefactos:
    ///   1. analytics.refresh_procedure_aggregates(tenant, from, to) — función de refresh
    ///      que recalcula procedure_metrics_daily y user_productivity_daily desde
    ///      tramites.procedure_instances + procedure_instance_status_history (AC2).
    ///      Se crea en TODOS los entornos.
    ///   2. Seed DEV-ONLY: instancias + historial sintéticos (~35 días) para el tenant
    ///      DEV y refresh inicial (AC1). Solo se emite en Development o con FLIT_DEV_SEED.
    ///
    /// GATE POR ENTORNO (igual que HU10200_DevSeed): la migración SIEMPRE queda
    /// registrada en __EFMigrationsHistory; el SQL del seed SOLO se emite cuando
    /// ASPNETCORE_ENVIRONMENT == "Development" o FLIT_DEV_SEED activo. En prod el seed
    /// es no-op, pero la función de refresh sí se crea.
    /// </remarks>
    public partial class HU10240_AnalyticsSeedRefresh : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Función de refresh: todos los entornos.
            migrationBuilder.Sql(EmbeddedDdl.LoadUp("20-HU10240-analytics-refresh.sql"));

            // Seed sintético: solo Development o FLIT_DEV_SEED.
            if (IsDevSeedEnabled())
            {
                migrationBuilder.Sql(EmbeddedDdl.LoadUp("21-HU10240-analytics-dev-seed.sql"));
            }
        }

        private static bool IsDevSeedEnabled()
        {
            var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
            if (string.Equals(environment, "Development", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var devSeedFlag = Environment.GetEnvironmentVariable("FLIT_DEV_SEED");
            return string.Equals(devSeedFlag, "1", StringComparison.Ordinal)
                || string.Equals(devSeedFlag, "true", StringComparison.OrdinalIgnoreCase);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Limpia el seed DEV (no-op en prod: no existen filas SEED-ANL-) y elimina
            // los agregados del tenant DEV; luego elimina la función de refresh.
            migrationBuilder.Sql(@"
DELETE FROM tramites.procedure_instances
  WHERE tenant_id = '11111111-1111-1111-1111-111111111111'
    AND reference_number LIKE 'SEED-ANL-%';
DELETE FROM analytics.procedure_metrics_daily
  WHERE tenant_id = '11111111-1111-1111-1111-111111111111';
DELETE FROM analytics.user_productivity_daily
  WHERE tenant_id = '11111111-1111-1111-1111-111111111111';
DROP FUNCTION IF EXISTS analytics.refresh_procedure_aggregates(uuid, date, date);
");
        }
    }
}
