using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations;

/// <summary>
/// HU #12967 (Feature #12888, FLIT Suite, tarea B-07) — copia <c>tramites_module_enabled</c> y
/// <c>comparendos_module_enabled</c> a <c>platform.tenant_products</c>. Desde aquí la configuración de empresa
/// y el dashboard leen la habilitación de productos. Sin cambio de modelo. DDL:
/// <c>121-HU12967-booleans-a-habilitacion.sql</c>.
/// </summary>
[DbContext(typeof(FlitDbContext))]
[Migration("20260925200000_HU12967_BooleansAHabilitacion")]
public partial class HU12967_BooleansAHabilitacion : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(EmbeddedDdl.LoadUp("121-HU12967-booleans-a-habilitacion.sql"));

    /// <inheritdoc />
    /// <remarks>
    /// Los booleans nunca se modificaron, así que no hay que devolverles nada. Se retiran las filas de
    /// Comparendos creadas por esta migración y se vuelve a encender Trámites donde esta migración lo apagó.
    /// </remarks>
    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("""
            DELETE FROM platform.tenant_products
             WHERE product_code = 'comparendos' AND updated_by IS NULL
               AND notes = 'Migrado desde comparendos_module_enabled (HU #12967)';
            UPDATE platform.tenant_products
               SET enabled = true, notes = 'Habilitado por la migración de la suite (HU #12958)', updated_at = now()
             WHERE product_code = 'tramites' AND updated_by IS NULL
               AND notes = 'Migrado desde tramites_module_enabled (HU #12967)';
            COMMENT ON COLUMN admin.tenant_operational_policies.tramites_module_enabled IS NULL;
            COMMENT ON COLUMN admin.tenant_operational_policies.comparendos_module_enabled IS NULL;
            """);
}
