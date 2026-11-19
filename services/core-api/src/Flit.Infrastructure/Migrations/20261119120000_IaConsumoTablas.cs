using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations;

/// <summary>
/// Registro de consumo de APIs de IA — contrato ia-consumo v1 (ADR-0090): <c>admin.ia_consumo_log</c>,
/// <c>admin.ia_consumo_detalle</c> y <c>admin.ia_tarifa_modelo</c> con la semilla de tarifas de Anthropic.
/// </summary>
/// <remarks>
/// Globales de plataforma sin RLS (tenant_id es texto informativo). El modelo EF las mapea con
/// <c>ExcludeFromMigrations</c> (el SDK las escribe por lotes) y el snapshot solo lleva ese delta.
/// DDL: <c>Persistence/Sql/Ddl/203-ia-consumo-log.sql</c>. Sin Designer: sin los atributos no se descubre.
/// </remarks>
[DbContext(typeof(FlitDbContext))]
[Migration("20261119120000_IaConsumoTablas")]
public partial class IaConsumoTablas : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(EmbeddedDdl.LoadUp("203-ia-consumo-log.sql"));

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("""
            DROP TABLE IF EXISTS admin.ia_consumo_detalle;
            DROP TABLE IF EXISTS admin.ia_consumo_log;
            DROP TABLE IF EXISTS admin.ia_tarifa_modelo;
            """);
}
