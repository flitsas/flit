using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Admin;
using Flit.Tramites.Domain.Documents;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace Flit.Infrastructure.Tests.Persistence;

/// <summary>
/// HU #13169 (Feature #13118, Épica #13090) — DDL 124: configuración y versiones de cada formato de mandato.
/// Verificación estática del DDL embebido (no hay Postgres en esta suite, igual que <c>StandaloneDocumentsSchemaTests</c>)
/// y del descubrimiento de la migración; el DDL se aplicó además con <c>dotnet ef database update</c> a la base local.
/// </summary>
public sealed class MandateFormatSchemaTests
{
    private const string DdlResource =
        "Flit.Infrastructure.Persistence.Sql.Ddl.124-HU13169-mandate-format-settings-versions.sql";

    private static string Statements()
    {
        using var stream = typeof(FlitDbContext).Assembly.GetManifestResourceStream(DdlResource);
        stream.Should().NotBeNull("el DDL 124 debe estar embebido");
        using var reader = new StreamReader(stream!, Encoding.UTF8);
        return Regex.Replace(reader.ReadToEnd(), @"(?m)^\s*--.*$", string.Empty);
    }

    [Fact]
    public void AC1_Seed_UnaFilaPorFormatoDelCatalogo_ConLosValoresActuales()
    {
        var sql = Statements();

        foreach (var f in MandatoFormatCatalog.All)
        {
            sql.Should().Contain($"('{f.Code}',", $"el formato {f.Code} del catálogo debe tener su fila inicial");
            sql.Should().Contain($"'{f.DefaultName}'");
        }

        sql.Should().Contain("ON CONFLICT (format_code) DO NOTHING", "idempotente: no pisa lo editado");
        sql.Should().NotContain("current_version) VALUES", "sin plantilla personalizada al nacer");
    }

    [Fact]
    public void AC2_LasVersionesSonInmutables_TriggerRechazaUpdateYDelete()
    {
        var sql = Statements();

        sql.Should().Contain("BEFORE UPDATE OR DELETE ON admin.mandate_format_versions");
        sql.Should().Contain("RAISE EXCEPTION");
        sql.Should().Contain("body_sha256");
        sql.Should().Contain("uq_mandate_format_versions_number");
    }

    [Fact]
    public void AC4_ConcurrenciaYNombreUnico_RowVersionConTriggerEIndiceDeNombre()
    {
        var sql = Statements();

        sql.Should().Contain("row_version        bigint        NOT NULL DEFAULT 0");
        sql.Should().Contain("tr_mandate_format_settings_row_version");
        sql.Should().Contain("public.trg_row_version()");
        sql.Should().Contain("uq_mandate_format_settings_display_name");
    }

    [Fact]
    public void AC7_CuerpoAcotado_Check100000()
    {
        Statements().Should().Contain("char_length(body) BETWEEN 1 AND 100000");
    }

    [Fact]
    public void DdlIdempotente_YSinTiposProhibidos()
    {
        var sql = Statements();

        sql.Should().Contain("CREATE TABLE IF NOT EXISTS admin.mandate_format_settings");
        sql.Should().Contain("CREATE TABLE IF NOT EXISTS admin.mandate_format_versions");
        sql.Should().Contain("DROP TRIGGER IF EXISTS");
        sql.Should().NotMatchRegex(@"(?i)\b(serial|float|real)\b");
        sql.Should().NotContain("timestamp without", "fechas con zona horaria");
        sql.Should().NotContain("tenant_id", "catálogo global de plataforma, sin RLS");
    }

    [Fact]
    public void AC6_LaMigracionSeDescubreAlArrancar_ConAtributosYDesigner()
    {
        var migration = typeof(FlitDbContext).Assembly.GetTypes()
            .Single(t => t.Name == "HU13169_MandateFormatSettingsVersions");

        migration.GetCustomAttribute<DbContextAttribute>()!.ContextType.Should().Be<FlitDbContext>();
        migration.GetCustomAttribute<MigrationAttribute>()!.Id.Should().EndWith("_HU13169_MandateFormatSettingsVersions");
        typeof(Migration).IsAssignableFrom(migration).Should().BeTrue();
    }

    [Fact]
    public void ElModeloEf_MapeaLasColumnasDelDdl()
    {
        using var db = new FlitDbContext(new DbContextOptionsBuilder<FlitDbContext>()
            .UseInMemoryDatabase("mandate-format-model").Options);

        var settings = db.Model.FindEntityType(typeof(MandateFormatSettingEntity))!;
        settings.GetTableName().Should().Be("mandate_format_settings");
        settings.GetSchema().Should().Be("admin");
        settings.GetProperties().Select(p => p.GetColumnName()).Should().BeEquivalentTo(
            "id", "format_code", "display_name", "assignment_mode", "current_version", "row_version",
            "created_at", "created_by", "updated_at", "updated_by");
        settings.FindProperty(nameof(MandateFormatSettingEntity.RowVersion))!.IsConcurrencyToken.Should().BeTrue();

        var versions = db.Model.FindEntityType(typeof(MandateFormatVersionEntity))!;
        versions.GetTableName().Should().Be("mandate_format_versions");
        versions.GetProperties().Select(p => p.GetColumnName()).Should().BeEquivalentTo(
            "id", "format_setting_id", "version_number", "body", "body_sha256", "created_at", "created_by");
    }
}
