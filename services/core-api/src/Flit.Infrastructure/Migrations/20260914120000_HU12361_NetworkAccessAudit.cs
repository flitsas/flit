using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations;

/// <summary>
/// HU #12361 (Feature #12257) — <c>tramites.network_access_audit</c>: auditoría append-only del acceso
/// consolidado de una cabeza de red a los datos de sus hijos (DDL embebido 113-).
/// </summary>
[DbContext(typeof(FlitDbContext))]
[Migration("20260914120000_HU12361_NetworkAccessAudit")]
public partial class HU12361_NetworkAccessAudit : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(EmbeddedDdl.LoadUp("113-HU12361-network-access-audit.sql"));

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("""
            DROP TRIGGER IF EXISTS tr_network_access_audit_immutable ON tramites.network_access_audit;
            DROP FUNCTION IF EXISTS tramites.trg_network_access_audit_immutable();
            DROP TABLE IF EXISTS tramites.network_access_audit;
            """);
}
