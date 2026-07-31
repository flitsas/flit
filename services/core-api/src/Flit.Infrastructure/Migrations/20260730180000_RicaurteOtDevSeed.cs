using System;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations;

/// <summary>
/// (DEV-ONLY) Habilita "STRIA TTEyMOV CUND/RICAURTE" (code DANE 25612000) como OT operable:
/// crea el tenant OT y su perfil (la oficina ya existe en el catálogo). Así los traspasos ICT
/// cuyo vehículo esté matriculado en Ricaurte pueden materializar el borrador, en vez de caer
/// en OT_NOT_AUTHORIZED_FOR_TYPE (compuerta IOtOperabilityGate de core-api).
///
/// GATE POR ENTORNO: la migración se registra en __EFMigrationsHistory en todos los ambientes,
/// pero el SQL solo corre en Development o con FLIT_DEV_SEED (mismo criterio que HU10133). El SQL
/// es idempotente (ON CONFLICT / NOT EXISTS).
/// </summary>
[DbContext(typeof(FlitDbContext))]
[Migration("20260730180000_RicaurteOtDevSeed")]
public partial class RicaurteOtDevSeed : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        if (!IsDevSeedEnabled())
        {
            return;
        }

        migrationBuilder.Sql(EmbeddedDdl.LoadUp("45-ricaurte-ot-dev-seed.sql"));
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
    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(
            """
            DELETE FROM admin.transit_office_profiles
            WHERE tenant_id = 'bbbbbbbb-2561-4000-8000-000000000001';
            DELETE FROM identity.tenants
            WHERE id = 'bbbbbbbb-2561-4000-8000-000000000001';
            """);
}
