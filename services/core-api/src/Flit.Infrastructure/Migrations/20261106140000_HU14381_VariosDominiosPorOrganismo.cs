using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations;

/// <summary>
/// HU #14381 (Feature #14380, Épica #14297) — varios dominios de portal por organismo, con uno principal
/// (<c>is_primary</c>). DDL: <c>186-HU14381-varios-dominios-por-organismo.sql</c>. Sin Designer: sin atributos no se descubre.
/// </summary>
[DbContext(typeof(FlitDbContext))]
[Migration("20261106140000_HU14381_VariosDominiosPorOrganismo")]
public partial class HU14381_VariosDominiosPorOrganismo : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(EmbeddedDdl.LoadUp("186-HU14381-varios-dominios-por-organismo.sql"));

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder) =>
        // Vuelve a un dominio vigente por organismo: conserva el principal (o el más antiguo) y retira lógicamente el resto.
        migrationBuilder.Sql("""
            UPDATE admin.transit_office_domains d
               SET deleted_at = now(), is_primary = false, next_check_at = NULL
             WHERE d.deleted_at IS NULL
               AND d.id <> (SELECT k.id FROM admin.transit_office_domains k
                             WHERE k.transit_office_id = d.transit_office_id AND k.deleted_at IS NULL
                             ORDER BY k.is_primary DESC, k.created_at, k.id LIMIT 1);
            DROP INDEX IF EXISTS admin.uq_transit_office_domains_primary;
            ALTER TABLE admin.transit_office_domains DROP COLUMN IF EXISTS is_primary;
            CREATE UNIQUE INDEX IF NOT EXISTS uq_transit_office_domains_office
                ON admin.transit_office_domains (transit_office_id) WHERE deleted_at IS NULL;
            """);
}
