using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations;

/// <summary>
/// HU #12919 (Épica #12718, ADR-0060) — schema <c>dr_flit</c> y contador diario de mensajes del chat
/// de DR. FLIT por tenant, usuario y día Colombia. Sin entidad EF: el repositorio incrementa con un
/// único <c>INSERT … ON CONFLICT</c> atómico. DDL: <c>119-HU12919-dr-flit-daily-message-usage.sql</c>.
/// </summary>
[DbContext(typeof(FlitDbContext))]
[Migration("20260925100000_HU12919_DrFlitDailyMessageUsage")]
public partial class HU12919_DrFlitDailyMessageUsage : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(EmbeddedDdl.LoadUp("119-HU12919-dr-flit-daily-message-usage.sql"));

    /// <inheritdoc />
    /// <remarks>
    /// Solo quita la tabla. El schema <c>dr_flit</c> lo comparten las tablas de la Épica #12718
    /// (casos de soporte, HU #12924) y se borra solo si queda vacío.
    /// </remarks>
    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(
            """
            DROP TABLE IF EXISTS dr_flit.daily_message_usage;
            DO $$
            BEGIN
                IF NOT EXISTS (SELECT 1 FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace WHERE n.nspname = 'dr_flit') THEN
                    DROP SCHEMA IF EXISTS dr_flit;
                END IF;
            END $$;
            """);
}
