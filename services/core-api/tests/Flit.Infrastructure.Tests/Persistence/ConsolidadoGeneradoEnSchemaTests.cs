using System.Reflection;
using System.Text.RegularExpressions;
using Flit.Infrastructure.Migrations;
using Flit.Infrastructure.Persistence;
using Flit.Tramites.Domain.Entities;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Xunit;

namespace Flit.Infrastructure.Tests.Persistence;

/// <summary>
/// HU #12790 (Épica #12760) AC1 — la migración <c>HU12790_ConsolidadoGeneradoEn</c> agrega
/// <c>consolidado_wizard_generado_en</c> y <c>consolidado_maestro_generado_en</c> (timestamptz,
/// nulables) a <c>tramites.procedure_instances</c> de forma idempotente, EF la descubre (lleva
/// <c>[DbContext]</c> + <c>[Migration]</c>) y el modelo mapea ambas propiedades. Sin conexión: la
/// tabla es <c>ExcludeFromMigrations</c>, así que lo verificable es el SQL crudo del Up/Down y el
/// modelo. La reaplicación real contra PostgreSQL se verifica aplicando la migración en local.
/// <para>
/// Uso de ejemplo: <c>new HU12790_ConsolidadoGeneradoEn().UpOperations.OfType&lt;SqlOperation&gt;()</c>.
/// </para>
/// </summary>
public sealed class ConsolidadoGeneradoEnSchemaTests
{
    private const string MigrationId = "20260923135047_HU12790_ConsolidadoGeneradoEn";

    private static readonly string[] DdlAjeno = ["CREATE TABLE", "CREATE INDEX", "ADD CONSTRAINT", "UPDATE ", "DROP "];

    private static string UpSql() =>
        Normalize(new HU12790_ConsolidadoGeneradoEn().UpOperations.OfType<SqlOperation>().Single().Sql);

    private static string DownSql() =>
        Normalize(new HU12790_ConsolidadoGeneradoEn().DownOperations.OfType<SqlOperation>().Single().Sql);

    private static string Normalize(string sql) => Regex.Replace(sql, @"\s+", " ");

    private static string SinLiterales(string sql) => Regex.Replace(sql, @"'[^']*'", "''");

    private static FlitDbContext NewContext() =>
        new(new DbContextOptionsBuilder<FlitDbContext>()
            .UseNpgsql("Host=localhost;Database=flit_model_only;Username=none;Password=none")
            .Options);

    [Fact]
    public void AC1_Up_AgregaAmbasColumnasTimestamptzNulablesConIfNotExists()
    {
        var up = UpSql();

        up.Should().Contain(
            "ALTER TABLE tramites.procedure_instances ADD COLUMN IF NOT EXISTS consolidado_wizard_generado_en timestamptz NULL;");
        up.Should().Contain(
            "ALTER TABLE tramites.procedure_instances ADD COLUMN IF NOT EXISTS consolidado_maestro_generado_en timestamptz NULL;");
    }

    [Fact]
    public void AC1_Up_EsIdempotente_CadaAddColumnLlevaIfNotExistsYNoHayOtroDdl()
    {
        var sentencias = SinLiterales(UpSql());

        Regex.Matches(sentencias, @"ADD COLUMN(?! IF NOT EXISTS)").Should().BeEmpty(
            "reaplicar la migración no debe fallar: todo ADD COLUMN es IF NOT EXISTS");
        Regex.Matches(sentencias, @"ADD COLUMN IF NOT EXISTS").Should().HaveCount(2);
        sentencias.Should().NotContainAny(
            DdlAjeno, "solo agrega columnas; no rellena históricos (AC5) ni toca otras estructuras");
        // COMMENT ON COLUMN es idempotente por naturaleza (sobrescribe).
        sentencias.Should().Contain("COMMENT ON COLUMN tramites.procedure_instances.consolidado_wizard_generado_en IS");
        sentencias.Should().Contain("COMMENT ON COLUMN tramites.procedure_instances.consolidado_maestro_generado_en IS");
    }

    [Fact]
    public void AC5_Up_NoFijaDefaultNiNotNull_LosHistoricosQuedanEnNull()
    {
        var sentencias = SinLiterales(UpSql());

        sentencias.Should().NotContain("DEFAULT");
        sentencias.Should().NotContain("NOT NULL");
    }

    [Fact]
    public void AC1_Down_RevierteAmbasColumnasConIfExists()
    {
        var down = DownSql();

        down.Should().Contain(
            "ALTER TABLE tramites.procedure_instances DROP COLUMN IF EXISTS consolidado_maestro_generado_en;");
        down.Should().Contain(
            "ALTER TABLE tramites.procedure_instances DROP COLUMN IF EXISTS consolidado_wizard_generado_en;");
        Regex.Matches(down, @"DROP COLUMN(?! IF EXISTS)").Should().BeEmpty();
    }

    [Fact]
    public void AC1_LaMigracionLaDescubreEf_LlevaDbContextYMigration()
    {
        var tipo = typeof(HU12790_ConsolidadoGeneradoEn);

        tipo.GetCustomAttribute<DbContextAttribute>()!.ContextType.Should().Be<FlitDbContext>();
        tipo.GetCustomAttribute<MigrationAttribute>()!.Id.Should().Be(MigrationId);

        using var ctx = NewContext();
        ctx.GetService<IMigrationsAssembly>().Migrations.Keys.Should().Contain(MigrationId);
    }

    [Fact]
    public void AC1_ElModeloMapeaAmbosSellosComoTimestamptzNulables()
    {
        using var ctx = NewContext();
        var entity = ctx.Model.FindEntityType(typeof(ProcedureInstance))!;

        var wizard = entity.FindProperty(nameof(ProcedureInstance.ConsolidadoWizardGeneradoEn))!;
        wizard.GetColumnName().Should().Be("consolidado_wizard_generado_en");
        wizard.GetColumnType().Should().Be("timestamptz");
        wizard.IsNullable.Should().BeTrue();

        var maestro = entity.FindProperty(nameof(ProcedureInstance.ConsolidadoMaestroGeneradoEn))!;
        maestro.GetColumnName().Should().Be("consolidado_maestro_generado_en");
        maestro.GetColumnType().Should().Be("timestamptz");
        maestro.IsNullable.Should().BeTrue();
    }

    [Fact]
    public void AC1_LaTablaSigueExcluidaDeMigraciones_ElDiffEfNoGeneraDdl()
    {
        var operaciones = new HU12790_ConsolidadoGeneradoEn().UpOperations;

        operaciones.Should().ContainSingle().Which.Should().BeOfType<SqlOperation>(because:
            "procedure_instances es ExcludeFromMigrations: el DDL va solo por SQL crudo idempotente");
    }
}
