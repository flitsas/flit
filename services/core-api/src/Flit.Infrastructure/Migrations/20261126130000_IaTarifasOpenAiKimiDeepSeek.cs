using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations;

/// <summary>
/// Tarifas de OpenAI, Kimi y DeepSeek en <c>admin.ia_tarifa_modelo</c> (contrato ia-consumo v1, ADR-0090) y recálculo del
/// costo de las llamadas de esos proveedores que ya quedaron con NULL. DDL:
/// <c>206-ia-tarifas-openai-kimi-deepseek.sql</c>. Lleva los atributos inline (sin Designer): sin ellos EF no la
/// descubre y no corre al arrancar. No cambia el modelo EF: el snapshot no se toca.
/// </summary>
[DbContext(typeof(FlitDbContext))]
[Migration("20261126130000_IaTarifasOpenAiKimiDeepSeek")]
public partial class IaTarifasOpenAiKimiDeepSeek : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(EmbeddedDdl.LoadUp("206-ia-tarifas-openai-kimi-deepseek.sql"));

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("""
            UPDATE admin.ia_consumo_log SET costo_usd_estimado = NULL
             WHERE proveedor IN ('openai', 'kimi', 'deepseek');
            DELETE FROM admin.ia_tarifa_modelo
             WHERE vigente_desde = DATE '2026-11-26'
               AND (proveedor, modelo) IN (('openai', 'gpt-4o'), ('openai', 'gpt-4o-mini'),
                                           ('kimi', 'kimi-k3'), ('deepseek', 'deepseek-flash'));
            """);
}
