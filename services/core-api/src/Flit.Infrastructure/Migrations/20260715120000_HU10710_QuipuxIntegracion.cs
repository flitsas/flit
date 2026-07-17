using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// Integración Quipux (QX) — radicación de trámites en secretarías de tránsito. Crea
    /// <c>admin.quipux_settings</c> (configuración editable en caliente, secretos cifrados con
    /// Data Protection), <c>tramites.quipux_submissions</c> (estado de la radicación, con el
    /// índice único parcial <c>uq_quipux_submissions_activa</c> que garantiza en BD una sola
    /// radicación viva por trámite), <c>tramites.quipux_submission_events</c> (bitácora por
    /// trámite) y <c>admin.quipux_job_runs</c> (bitácora por ejecución del worker), con RLS
    /// <c>tenant_isolation</c> en las dos tablas de <c>tramites</c>. DDL embebido (ADR-0018).
    /// </remarks>
    // Los atributos van inline porque esta migración no tiene .Designer.cs (el diff EF sale vacío:
    // las tablas están ExcludeFromMigrations y el DDL lo lleva el .sql embebido). Sin ellos EF NO
    // descubre la migración y las tablas nunca se crean — el defecto que ya sufre en silencio
    // HU10133_OtAdminDevSeed, ausente de __EFMigrationsHistory.
    [DbContext(typeof(FlitDbContext))]
    [Migration("20260715120000_HU10710_QuipuxIntegracion")]
    public partial class HU10710_QuipuxIntegracion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(EmbeddedDdl.LoadUp("32-HU10710-quipux-integracion.sql"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
DROP TABLE IF EXISTS tramites.quipux_submission_events CASCADE;
DROP TABLE IF EXISTS tramites.quipux_submissions CASCADE;
DROP TABLE IF EXISTS admin.quipux_job_runs CASCADE;
DROP TABLE IF EXISTS admin.quipux_settings CASCADE;
");
        }
    }
}
