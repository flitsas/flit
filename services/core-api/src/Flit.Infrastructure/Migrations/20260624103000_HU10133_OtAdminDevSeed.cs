using System;
using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// HU #10133 (DEV-ONLY) — Seed de datos de prueba para el módulo OT
    /// (/admin/transit-offices): catálogo FK, perfil OT, grants, trámites pending_ot,
    /// feature flags, reglas, webhooks, logs, etiquetas y prelación documental.
    /// Idempotente: el .sql usa ON CONFLICT DO NOTHING / WHERE NOT EXISTS.
    ///
    /// GATE POR ENTORNO: la migración queda registrada en __EFMigrationsHistory en todos
    /// los entornos, pero el SQL solo corre en Development o con FLIT_DEV_SEED activo.
    /// </remarks>
    public partial class HU10133_OtAdminDevSeed : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            if (!IsDevSeedEnabled())
            {
                return;
            }

            migrationBuilder.Sql(EmbeddedDdl.LoadUp("16-HU10133-ot-admin-dev-seed.sql"));
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
            migrationBuilder.Sql(
                """
                DELETE FROM admin.ot_document_precedence
                WHERE tenant_id = 'bbbbbbbb-0001-4000-8000-000000000001';
                DELETE FROM admin.ot_document_tags
                WHERE tenant_id = 'bbbbbbbb-0001-4000-8000-000000000001';
                DELETE FROM admin.ot_api_call_logs
                WHERE tenant_id = 'bbbbbbbb-0001-4000-8000-000000000001';
                DELETE FROM admin.ot_webhook_subscriptions
                WHERE tenant_id = 'bbbbbbbb-0001-4000-8000-000000000001';
                DELETE FROM admin.ot_feature_flags
                WHERE tenant_id = 'bbbbbbbb-0001-4000-8000-000000000001';
                DELETE FROM tramites.procedure_instances
                WHERE reference_number IN ('OT-DEV-0001', 'OT-DEV-0002', 'OT-DEV-0003');
                DELETE FROM tramites.document_types
                WHERE code IN ('SOAT', 'CEDULA', 'TARJETA_PROPIEDAD');
                DELETE FROM admin.tenant_transit_office_grants
                WHERE id IN (
                    'bbbbbbbb-0001-4000-8000-000000000001',
                    'bbbbbbbb-0001-4000-8000-000000000002'
                );
                DELETE FROM admin.transit_office_profiles
                WHERE tenant_id = 'bbbbbbbb-0001-4000-8000-000000000001';
                DELETE FROM identity.tenants
                WHERE id = 'bbbbbbbb-0001-4000-8000-000000000001';
                DELETE FROM catalogs.transit_offices
                WHERE id IN (
                    'aaaaaaaa-0001-4000-8000-000000000001',
                    'aaaaaaaa-0001-4000-8000-000000000002',
                    'aaaaaaaa-0001-4000-8000-000000000003',
                    'aaaaaaaa-0001-4000-8000-000000000004',
                    'aaaaaaaa-0001-4000-8000-000000000005',
                    'aaaaaaaa-0001-4000-8000-000000000006'
                );
                """);
        }
    }
}
