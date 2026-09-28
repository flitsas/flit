using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations;

/// <summary>
/// HU #12958 (Feature #12888, FLIT Suite frente B, tarea B-03, ADR-0063) — schema <c>platform</c> con
/// el catálogo de productos (<c>platform.products</c>) y su habilitación por empresa
/// (<c>platform.tenant_products</c>). Enciende <c>tramites</c> para todas las empresas existentes y,
/// hasta B-12, para cada empresa nueva. DDL en <c>119-HU12958-platform-products.sql</c>, que es la
/// fuente de verdad; esta migración solo lo ejecuta. Las entidades EF están <c>ExcludeFromMigrations</c>.
/// </summary>
public partial class HU12958_PlatformProducts : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(EmbeddedDdl.LoadUp("119-HU12958-platform-products.sql"));

    /// <inheritdoc />
    /// <remarks>
    /// Reversible: Trámites no deja de funcionar porque hoy nada lee estas tablas. Primero el disparador
    /// sobre <c>identity.tenants</c> (depende de la función), luego las tablas y el schema.
    /// </remarks>
    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("""
            DROP TRIGGER IF EXISTS tr_tenants_default_products ON identity.tenants;
            DROP FUNCTION IF EXISTS platform.trg_tenant_default_products();
            DROP TABLE IF EXISTS platform.tenant_products;
            DROP TABLE IF EXISTS platform.products;
            DROP SCHEMA IF EXISTS platform;
            """);
}
