using System;
using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// HU #10200 (DEV-ONLY) — Seed de desarrollo para Tab Operación | Feature #10116.
    /// Inserta el tenant/user fijos que el frontend de Operación usa por defecto y
    /// publica MATRICULA_NUEVA con configuración mínima (step→section→fields) para
    /// que el dropdown (AC1) y el wizard (AC2) funcionen end-to-end.
    /// Idempotente: el .sql usa ON CONFLICT DO NOTHING / WHERE NOT EXISTS.
    ///
    /// GATE POR ENTORNO (REX CRITICAL): este repo aplica las migraciones vía
    /// `dotnet ef database update` (no hay `Database.Migrate()` en runtime, ver
    /// Flit.Api/Program.cs + InfrastructureExtensions), por lo que NO existe un
    /// runner propio donde excluir la migración. El gate se hace DENTRO de Up():
    /// la migración SIEMPRE queda registrada en __EFMigrationsHistory (para no
    /// desincronizar el historial entre entornos), pero el SQL del seed SOLO se
    /// emite cuando ASPNETCORE_ENVIRONMENT == "Development" o FLIT_DEV_SEED activo.
    /// En prod el Up() es un no-op: NO inserta tenant/user ni publica nada.
    /// </remarks>
    public partial class HU10200_DevSeed : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // DEV-ONLY: solo sembrar en Development o cuando el flag FLIT_DEV_SEED
            // esté activo. En cualquier otro entorno (prod incluido) es no-op.
            if (!IsDevSeedEnabled())
            {
                return;
            }

            migrationBuilder.Sql(EmbeddedDdl.LoadUp("12-HU10200-dev-seed.sql"));
        }

        private static bool IsDevSeedEnabled()
        {
            var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
            if (string.Equals(environment, "Development", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // Escotilla explícita para sembrar fuera de Development (p. ej. CI/QA local).
            var devSeedFlag = Environment.GetEnvironmentVariable("FLIT_DEV_SEED");
            return string.Equals(devSeedFlag, "1", StringComparison.Ordinal)
                || string.Equals(devSeedFlag, "true", StringComparison.OrdinalIgnoreCase);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // DEV-ONLY seed: revierte publicación y elimina tenant/user de desarrollo.
            migrationBuilder.Sql(@"
DELETE FROM tramites.procedure_steps s
  USING tramites.procedure_types pt
  WHERE s.procedure_type_id = pt.id AND pt.code = 'MATRICULA_NUEVA' AND s.code = 'DATOS_VEHICULO';
UPDATE tramites.procedure_types
  SET publication_status = 'draft', published_at = NULL
  WHERE code = 'MATRICULA_NUEVA';
DELETE FROM identity.users   WHERE id = '22222222-2222-2222-2222-222222222222';
DELETE FROM identity.tenants WHERE id = '11111111-1111-1111-1111-111111111111';
");
        }
    }
}
