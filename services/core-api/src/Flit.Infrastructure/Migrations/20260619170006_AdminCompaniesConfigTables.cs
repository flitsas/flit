using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations
{
    /// <summary>
    /// NO-OP tras el merge del rework de trámites (#10128).
    ///
    /// <para>Esta migración (scaffolding EF de develop) recreaba en C# las tablas
    /// <c>admin.tenant_config_audit_logs</c>, <c>tenant_operational_policies</c>,
    /// <c>tenant_transit_office_grants</c> y <c>tenant_whitelist_users</c>. Pero esas
    /// CUATRO tablas YA las crea <c>07-HU10154-admin-tenants.sql</c> (EmbeddedDdl del base
    /// compartido), que corre mucho antes en la cadena. En una base desde cero el
    /// <c>CreateTable</c> de C# fallaba con <c>relation already exists</c>.</para>
    ///
    /// <para>Se neutraliza a no-op: las tablas (y su estructura) las define el DDL SQL.
    /// En entornos ya migrados esta migración figura aplicada en <c>__EFMigrationsHistory</c>
    /// y no se re-ejecuta, así que vaciar el cuerpo no tiene efecto allí; solo desbloquea
    /// las bases nuevas (onboarding local, CI de integración, entorno limpio).</para>
    /// </summary>
    public partial class AdminCompaniesConfigTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // No-op: las tablas admin las crea 07-HU10154-admin-tenants.sql (ver resumen).
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // No-op.
        }
    }
}
