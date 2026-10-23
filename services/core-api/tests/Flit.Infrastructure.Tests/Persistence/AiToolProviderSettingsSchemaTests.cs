using System.Text;
using System.Text.RegularExpressions;
using Flit.Infrastructure.Persistence;
using FluentAssertions;
using Xunit;

namespace Flit.Infrastructure.Tests.Persistence;

/// <summary>
/// Feature #13508 — verificación estática del DDL multi-proveedor IA (sin Postgres en suite).
/// </summary>
public sealed class AiToolProviderSettingsSchemaTests
{
    private const string DdlResource =
        "Flit.Infrastructure.Persistence.Sql.Ddl.130-Feature13508-ai-tool-provider-settings.sql";

    private static string Load()
    {
        var assembly = typeof(FlitDbContext).Assembly;
        using var stream = assembly.GetManifestResourceStream(DdlResource);
        stream.Should().NotBeNull($"el DDL embebido {DdlResource} debe existir");
        using var reader = new StreamReader(stream!, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private static string Statements() => Regex.Replace(Load(), @"(?m)^\s*--.*$", string.Empty);

    [Fact]
    public void CreaLasTresTablasAdminGlobales()
    {
        var sql = Load();
        sql.Should().Contain("CREATE TABLE IF NOT EXISTS admin.ai_tool_provider_settings");
        sql.Should().Contain("CREATE TABLE IF NOT EXISTS admin.ai_provider_transitions");
        sql.Should().Contain("CREATE TABLE IF NOT EXISTS admin.ai_failover_alerts");
    }

    [Fact]
    public void SinTenantIdNiRlsEnStatements()
    {
        var sql = Statements();
        sql.Should().NotContain("tenant_id");
        sql.Should().NotContain("ENABLE ROW LEVEL SECURITY");
        sql.Should().NotContain("CREATE POLICY");
    }

    [Fact]
    public void SettingsTieneUnicidadPorToolYChecksDeCatalogo()
    {
        var sql = Load();
        sql.Should().Contain("CONSTRAINT uq_ai_tool_provider_settings_tool UNIQUE (tool_code)");
        sql.Should().Contain("CHECK (tool_code IN ('ocr', 'dr_flit'))");
        sql.Should().Contain("CHECK (active_provider IN ('anthropic', 'openai', 'kimi', 'deepseek'))");
        sql.Should().Contain("providers_json          jsonb         NOT NULL");
    }

    [Fact]
    public void SettingsTieneTriggersRowVersionYAudit()
    {
        var sql = Statements();
        sql.Should().Contain("tr_ai_tool_provider_settings_row_version");
        sql.Should().Contain("public.trg_row_version()");
        sql.Should().Contain("tr_ai_tool_provider_settings_audit");
        sql.Should().Contain("public.trg_audit_log()");
    }

    [Fact]
    public void TransitionsSonAppendOnlyConIndicePorTool()
    {
        var sql = Statements();
        sql.Should().Contain("ix_ai_provider_transitions_tool_occurred");
        sql.Should().Contain("tr_ai_provider_transitions_immutable");
        sql.Should().Contain("CHECK (reason IN ('manual', 'failover_auto', 'auto_return', 'seed'))");
    }

    [Fact]
    public void AlertsTieneFkRestrictYUnicidadPorTransition()
    {
        var sql = Load();
        sql.Should().Contain("CONSTRAINT fk_ai_failover_alerts_transition");
        sql.Should().Contain("ON DELETE RESTRICT ON UPDATE CASCADE");
        sql.Should().Contain("CONSTRAINT uq_ai_failover_alerts_transition UNIQUE (transition_id)");
        sql.Should().Contain("ix_ai_failover_alerts_acked_occurred");
    }

    [Fact]
    public void SiembraOcrYDrFlitConPrioridadDefault()
    {
        var sql = Statements();
        sql.Should().Contain("INSERT INTO admin.ai_tool_provider_settings");
        sql.Should().Contain("VALUES ('ocr'), ('dr_flit')");
        sql.Should().Contain("\"providerCode\":\"anthropic\"");
        sql.Should().Contain("\"providerCode\":\"openai\"");
        sql.Should().Contain("\"providerCode\":\"kimi\"");
        sql.Should().Contain("\"providerCode\":\"deepseek\"");
        sql.Should().Contain("WHERE NOT EXISTS");
    }

    [Fact]
    public void DocumentaExcepcionGlobalYAdr()
    {
        var sql = Load();
        sql.Should().Contain("ADR-0070");
        sql.Should().Contain("#13508");
        sql.Should().Contain("sin tenant_id");
    }
}
