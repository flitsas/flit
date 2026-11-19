using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations;

/// <summary>
/// HU-5 (ola LLM Diagnóstico, registro diferido; ADR-0091 Propuesto) — herramienta IA <c>diagnostico_dictamen</c>:
/// amplía los 3 CHECK de <c>tool_code</c> y la siembra solo con anthropic. DDL:
/// <c>Persistence/Sql/Ddl/203-ai-tool-diagnostico-dictamen.sql</c>.
/// </summary>
/// <remarks>
/// Generada con <c>dotnet ef migrations add</c>; el cuerpo autogenerado (deriva previa del snapshot por las tablas
/// <c>admin.ot_*client_scope*</c> que crean los DDL 197/198) se sustituyó por el DDL y el snapshot no se toca porque
/// el modelo no cambia. Atributos inline (sin Designer), como las migraciones recientes del repo.
/// <para>
/// Down: borra alertas, transiciones y la fila de la herramienta (desactiva el trigger append-only solo durante su
/// borrado) y restaura los CHECK a <c>('ocr', 'dr_flit')</c>.
/// </para>
/// </remarks>
[DbContext(typeof(FlitDbContext))]
[Migration("20261119041051_HU5_AiToolDiagnosticoDictamen")]
public partial class HU5_AiToolDiagnosticoDictamen : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(EmbeddedDdl.LoadUp("203-ai-tool-diagnostico-dictamen.sql"));

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(
            """
            DELETE FROM admin.ai_failover_alerts WHERE tool_code = 'diagnostico_dictamen';
            ALTER TABLE admin.ai_provider_transitions DISABLE TRIGGER tr_ai_provider_transitions_immutable;
            DELETE FROM admin.ai_provider_transitions WHERE tool_code = 'diagnostico_dictamen';
            ALTER TABLE admin.ai_provider_transitions ENABLE TRIGGER tr_ai_provider_transitions_immutable;
            DELETE FROM admin.ai_tool_provider_settings WHERE tool_code = 'diagnostico_dictamen';

            ALTER TABLE admin.ai_tool_provider_settings DROP CONSTRAINT IF EXISTS ck_ai_tool_provider_settings_tool;
            ALTER TABLE admin.ai_tool_provider_settings
                ADD CONSTRAINT ck_ai_tool_provider_settings_tool CHECK (tool_code IN ('ocr', 'dr_flit'));
            ALTER TABLE admin.ai_provider_transitions DROP CONSTRAINT IF EXISTS ck_ai_provider_transitions_tool;
            ALTER TABLE admin.ai_provider_transitions
                ADD CONSTRAINT ck_ai_provider_transitions_tool CHECK (tool_code IN ('ocr', 'dr_flit'));
            ALTER TABLE admin.ai_failover_alerts DROP CONSTRAINT IF EXISTS ck_ai_failover_alerts_tool;
            ALTER TABLE admin.ai_failover_alerts
                ADD CONSTRAINT ck_ai_failover_alerts_tool CHECK (tool_code IN ('ocr', 'dr_flit'));

            COMMENT ON COLUMN admin.ai_tool_provider_settings.tool_code IS
              'Catálogo en código: ocr | dr_flit (extensible vía migración + CHECK).';
            """);
}
