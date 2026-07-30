using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations;

/// <summary>
/// HU #11105 — Seed RBAC Reporting V2: módulo reportes-v2, 15 slugs reporting.*,
/// depreciación detailed-report.* (ADR-0038).
/// </summary>
[DbContext(typeof(FlitDbContext))]
[Migration("20260730154500_HU11105_ReportingV2RbacSeed")]
public partial class HU11105_ReportingV2RbacSeed : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(EmbeddedDdl.LoadUp("47-HU11105-reporting-v2-rbac-seed.sql"));
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            -- Reversa parcial: no elimina grants históricos; desactiva reporting.* y borra detailed-report.*
            UPDATE security.permissions
            SET is_active = false, updated_at = now()
            WHERE slug LIKE 'reporting.%';

            DELETE FROM security.role_permissions rp
            USING security.permissions p
            WHERE rp.permission_id = p.id
              AND (p.slug LIKE 'reporting.%' OR p.slug LIKE 'detailed-report.%');

            DELETE FROM security.permissions WHERE slug LIKE 'detailed-report.%';

            UPDATE security.modules
            SET is_active = false, updated_at = now()
            WHERE code = 'reportes-v2';
            """);
    }
}
