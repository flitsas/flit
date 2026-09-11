using FluentAssertions;
using Xunit;

namespace Flit.Admin.Tests.Ict;

/// <summary>
/// HU #12515 — la guía y el ADR de procesos periódicos existen en el repo
/// (AC1) y no reutilizan ADR-0055 ni se auto-aceptan (AC2).
/// Uso de ejemplo:
/// File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "docs", "estandar-jobs-periodicos-flit.md"));
/// </summary>
public sealed class JobsPeriodicStandardDocsTests
{
    private static readonly string GuidePath = Path.Combine(
        AppContext.BaseDirectory, "docs", "estandar-jobs-periodicos-flit.md");

    private static readonly string AdrPath = Path.Combine(
        AppContext.BaseDirectory, "docs", "decisions",
        "ADR-0059-estandar-procesos-automaticos-periodicos.md");

    [Fact]
    public void Guia_y_adr_existen_con_referencias_del_feature()
    {
        File.Exists(GuidePath).Should().BeTrue("docs/estandar-jobs-periodicos-flit.md debe versionarse");
        File.Exists(AdrPath).Should().BeTrue("ADR-0059 debe versionarse en docs/decisions/");

        var guide = File.ReadAllText(GuidePath);
        var adr = File.ReadAllText(AdrPath);

        foreach (var text in new[] { guide, adr })
        {
            text.Should().Contain("#12122");
            text.Should().Contain("#10710");
            text.Should().Contain("migracion-ict");
            text.Should().Contain("ADR-0024");
            text.Should().Contain("IctJobSettingsProvider");
        }

        guide.Should().Contain("/api/v1/admin/ict/job-settings");
        adr.Should().Contain("/api/v1/admin/ict/job-settings");
    }

    [Fact]
    public void Adr_0059_queda_propuesto_y_no_reutiliza_0055()
    {
        var adr = File.ReadAllText(AdrPath);
        var fileName = Path.GetFileName(AdrPath);

        fileName.Should().StartWith("ADR-0059-");
        fileName.Should().NotContain("0055");
        adr.Should().Contain("# ADR-0059:");
        adr.Should().Contain("**Status**: Propuesto");
        adr.Should().NotContain("**Status**: Aceptado");
        adr.Should().Contain("No reutiliza ADR-0055");
        adr.Should().Contain("ADR-0055-captura-dual-hecho-prenda-levantar-inscribir.md");
    }
}
