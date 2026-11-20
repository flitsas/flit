using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations;

/// <summary>
/// HU #14381 (Feature #14380, Épica #14297) — dominio del portal Persona Natural por organismo
/// (<c>admin.transit_office_domains</c>, su vista y el disparador de unicidad de host entre las dos tablas de dominios).
/// DDL: <c>183-HU14381-transit-office-domains.sql</c>. Sin Designer: sin atributos no se descubre.
/// </summary>
[DbContext(typeof(FlitDbContext))]
[Migration("20261106110000_HU14381_DominioPortalOrganismo")]
public partial class HU14381_DominioPortalOrganismo : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(EmbeddedDdl.LoadUp("183-HU14381-transit-office-domains.sql"));

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("""
            DROP VIEW IF EXISTS admin.v_active_portal_domains;
            DROP TRIGGER IF EXISTS tr_tenant_domains_host_unico ON admin.tenant_domains;
            DROP TABLE IF EXISTS admin.transit_office_domains;
            DROP FUNCTION IF EXISTS admin.trg_domain_host_unico_entre_tablas();
            """);
}
