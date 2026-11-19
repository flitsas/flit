using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Flit.Admin.Domain.Plataforma.Ai;
using Flit.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace Flit.Infrastructure.Tests.Persistence;

/// <summary>
/// HU-5 (ola LLM Diagnóstico, registro diferido) — DDL 203: la herramienta <c>diagnostico_dictamen</c> entra en los
/// 3 CHECK de <c>tool_code</c> y nace sembrada solo con anthropic. Verificación estática (la aplicación real contra
/// Postgres se hace con la base clonada de 5433).
/// </summary>
public sealed class AiToolDiagnosticoDictamenSchemaTests
{
    private const string DdlFile = "203-ai-tool-diagnostico-dictamen.sql";
    private const string DdlResource = "Flit.Infrastructure.Persistence.Sql.Ddl." + DdlFile;
    private const string MigrationId = "20261119041051_HU5_AiToolDiagnosticoDictamen";

    private static string Load()
    {
        var assembly = typeof(FlitDbContext).Assembly;
        using var stream = assembly.GetManifestResourceStream(DdlResource);
        stream.Should().NotBeNull($"el DDL embebido {DdlResource} debe existir");
        using var reader = new StreamReader(stream!, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private static string Statements() =>
        Regex.Replace(Load().Replace("\r\n", "\n", StringComparison.Ordinal), @"(?m)^\s*--.*$", string.Empty);

    [Fact]
    public void ConstanteDeCatalogoCoincideConElDdl()
    {
        AiToolCodes.DiagnosticoDictamen.Should().Be("diagnostico_dictamen");
    }

    [Theory]
    [InlineData("admin.ai_tool_provider_settings", "ck_ai_tool_provider_settings_tool")]
    [InlineData("admin.ai_provider_transitions", "ck_ai_provider_transitions_tool")]
    [InlineData("admin.ai_failover_alerts", "ck_ai_failover_alerts_tool")]
    public void AmpliaElCheckDeToolCodeEnLasTresTablasDeFormaIdempotente(string table, string constraint)
    {
        var sql = Statements();
        sql.Should().Contain($"ALTER TABLE {table}\n    DROP CONSTRAINT IF EXISTS {constraint};");
        sql.Should().Contain(
            $"ALTER TABLE {table}\n    ADD CONSTRAINT {constraint}\n    CHECK (tool_code IN ('ocr', 'dr_flit', 'diagnostico_dictamen'));");
    }

    [Fact]
    public void SiembraLaHerramientaSoloConAnthropicYTransicionSeed()
    {
        var sql = Statements();
        sql.Should().Contain("INSERT INTO admin.ai_tool_provider_settings");
        sql.Should().Contain("'diagnostico_dictamen',\n    'anthropic',");
        sql.Should().Contain("{\"providerCode\":\"anthropic\",\"enabled\":true,\"priorityRank\":1}");
        sql.Should().Contain("{\"providerCode\":\"openai\",\"enabled\":false,\"priorityRank\":2}");
        sql.Should().Contain("{\"providerCode\":\"kimi\",\"enabled\":false,\"priorityRank\":3}");
        sql.Should().Contain("{\"providerCode\":\"deepseek\",\"enabled\":false,\"priorityRank\":4}");
        Regex.Count(sql, "\"enabled\":true").Should().Be(1);
        sql.Should().Contain("INSERT INTO admin.ai_provider_transitions");
        sql.Should().Contain("'diagnostico_dictamen', NULL, 'anthropic', 'seed'");
        Regex.Count(sql, "WHERE NOT EXISTS").Should().Be(2);
    }

    [Fact]
    public void SinTenantIdNiRlsNiSecretos()
    {
        var sql = Statements();
        sql.Should().NotContain("tenant_id");
        sql.Should().NotContain("ROW LEVEL SECURITY");
        sql.Should().NotContain("CREATE POLICY");
        sql.Should().NotContain("sk-ant-");
    }

    [Fact]
    public void LaMigracionEsDescubiertaPorEfYCargaElDdl203()
    {
        var migration = typeof(FlitDbContext).Assembly.GetTypes()
            .SingleOrDefault(t => typeof(Migration).IsAssignableFrom(t)
                && t.GetCustomAttribute<MigrationAttribute>()?.Id == MigrationId);

        migration.Should().NotBeNull($"la migración {MigrationId} debe existir con [Migration] para que EF la descubra");
        migration!.GetCustomAttribute<Microsoft.EntityFrameworkCore.Infrastructure.DbContextAttribute>()!
            .ContextType.Should().Be<FlitDbContext>();
    }
}
