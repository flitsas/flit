using System.Text;
using System.Text.RegularExpressions;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Admin;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Xunit;

namespace Flit.Infrastructure.Tests.Persistence;

/// <summary>
/// HU #14771 — verificación estática del DDL y del modelo EF de <c>admin.dr_flit_settings</c>.
/// </summary>
public sealed class DrFlitSettingsSchemaTests
{
    private const string DdlResource =
        "Flit.Infrastructure.Persistence.Sql.Ddl.207-HU14771-dr-flit-settings.sql";

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
    public void AC1_CreaTablaConUiEnabledYSemillaFalse()
    {
        var sql = Load();
        sql.Should().Contain("CREATE TABLE IF NOT EXISTS admin.dr_flit_settings");
        sql.Should().Contain("ui_enabled boolean NOT NULL DEFAULT false");
        sql.Should().Contain("uq_dr_flit_settings_singleton");
        Statements().Should().MatchRegex(
            @"INSERT INTO admin\.dr_flit_settings \(ui_enabled\)\s*\r?\n?\s*SELECT false");
    }

    [Fact]
    public void AC1_SinTenantIdNiRls()
    {
        var sql = Statements();
        sql.Should().NotMatchRegex(@"(?m)^\s*tenant_id\s");
        sql.Should().NotContain("ROW LEVEL SECURITY");
        sql.Should().NotContain("CREATE POLICY");
    }

    [Fact]
    public void AC2_ModeloEfMapeaUiEnabledYConcurrency()
    {
        using var db = NewContext();
        var entity = db.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(DrFlitSettingsRow));
        entity.Should().NotBeNull("la configuración EF debe registrar la entidad");
        var e = entity!;
        e.GetTableName().Should().Be("dr_flit_settings");
        e.GetSchema().Should().Be("admin");
        e.FindProperty(nameof(DrFlitSettingsRow.UiEnabled))!.GetColumnName().Should().Be("ui_enabled");
        e.FindProperty(nameof(DrFlitSettingsRow.RowVersion))!.IsConcurrencyToken.Should().BeTrue();
        e.IsTableExcludedFromMigrations().Should().BeTrue();
    }

    private static FlitDbContext NewContext() =>
        new(new DbContextOptionsBuilder<FlitDbContext>()
            .UseNpgsql("Host=localhost;Database=flit_model_only;Username=none;Password=none")
            .Options);
}
