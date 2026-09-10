using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations;

/// <summary>
/// Feature #12276 (Epic #12234) — Confirmación RUNT: configuración global (HU #12277), bitácora de
/// corridas e intentos (HU #12308/#12309) y la marca <c>runt_*</c> en <c>procedure_instances</c>
/// (HU #12312). DDL en <c>107-F12276-confirmacion-runt.sql</c>, que es la fuente de verdad; esta
/// migración solo lo ejecuta. Prefijo de Feature porque el schema sirve a cinco HUs a la vez.
/// </summary>
[DbContext(typeof(FlitDbContext))]
[Migration("20260910150000_F12276_ConfirmacionRunt")]
public partial class F12276_ConfirmacionRunt : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(EmbeddedDdl.LoadUp("107-F12276-confirmacion-runt.sql"));

    /// <inheritdoc />
    /// <remarks>
    /// Reversible: las tres tablas son nuevas y las columnas de <c>procedure_instances</c> nunca
    /// tuvieron backfill, así que el rollback no pierde datos ajenos al Feature. Los intentos se
    /// van antes que las corridas y los payloads (FKs).
    /// </remarks>
    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("""
            DROP INDEX IF EXISTS tramites.ix_procedure_instances_runt_universo;
            ALTER TABLE tramites.procedure_instances
                DROP CONSTRAINT IF EXISTS ck_procedure_instances_runt_flag,
                DROP COLUMN IF EXISTS runt_flag,
                DROP COLUMN IF EXISTS runt_attempts,
                DROP COLUMN IF EXISTS runt_confirmed_at;

            DROP TABLE IF EXISTS tramites.runt_confirmation_attempts;
            DROP TABLE IF EXISTS tramites.runt_confirmation_runs;

            DROP TRIGGER IF EXISTS tr_runt_confirmation_settings_audit ON tramites.runt_confirmation_settings;
            DROP TRIGGER IF EXISTS tr_runt_confirmation_settings_row_version ON tramites.runt_confirmation_settings;
            DROP TABLE IF EXISTS tramites.runt_confirmation_settings;
            """);
}
