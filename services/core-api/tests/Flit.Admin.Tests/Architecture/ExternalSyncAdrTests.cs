using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace Flit.Admin.Tests.Architecture;

/// <summary>
/// HU #13080 (Feature #13066, Épica #12737) — los ADR de la épica existen, están en Propuesto (la aceptación
/// es del líder técnico en un PR aparte), comparan alternativas y lo que nombran existe en el código: un ADR que
/// describe un componente renombrado o borrado deja de pasar.
/// </summary>
public sealed partial class ExternalSyncAdrTests
{
    public static TheoryData<string> AdrsDeLaEpica => new()
    {
        "ADR-0066-marca-de-agua-sincronizacion-tramites.md",
        "ADR-0067-clientes-de-integracion-externos.md",
        "ADR-0068-cursor-keyset-opaco-feed-sincronizacion.md",
        "ADR-0069-lectura-entre-companias-acotada-y-auditada.md",
    };

    [Theory]
    [MemberData(nameof(AdrsDeLaEpica))]
    public void AC2_NingunAdrDeLaEpicaQuedaAceptadoSinElLiderTecnico(string archivo)
    {
        var texto = Leer(archivo);

        Estado().Match(texto).Groups[1].Value.Trim().Should().Be("Propuesto");
        texto.Should().Contain("Aceptación: Líder Técnico");
    }

    [Theory]
    [InlineData("ADR-0068-cursor-keyset-opaco-feed-sincronizacion.md")]
    [InlineData("ADR-0069-lectura-entre-companias-acotada-y-auditada.md")]
    public void AC1_LosAdrDeCursorYDeLecturaEntreCompaniasComparanAlternativas(string archivo)
    {
        var texto = Leer(archivo);

        Alternativa().Matches(texto).Count.Should().BeGreaterThanOrEqualTo(2, "siempre 2 o 3 alternativas");
        texto.Should().Contain("## Decisión").And.Contain("## Tradeoff aceptado").And.Contain("## Consecuencias");
    }

    [Theory]
    [InlineData("ADR-0068-cursor-keyset-opaco-feed-sincronizacion.md", "src/Flit.Tramites.Domain/ExternalSync/ExternalSyncCursor.cs", "ExternalSyncCursor")]
    [InlineData("ADR-0068-cursor-keyset-opaco-feed-sincronizacion.md", "src/Flit.Infrastructure/Persistence/Sql/Ddl/126-HU13076-sync-cursor.sql", "ix_procedure_instances_sync_cursor")]
    [InlineData("ADR-0069-lectura-entre-companias-acotada-y-auditada.md", "src/Flit.Infrastructure/Persistence/ExternalSync/ExternalSyncReadScope.cs", "ExternalSyncReadScope")]
    [InlineData("ADR-0069-lectura-entre-companias-acotada-y-auditada.md", "tests/Flit.Admin.Tests/Architecture/ExternalSyncScopeArchitectureTests.cs", "ExternalSyncScopeArchitectureTests")]
    [InlineData("ADR-0069-lectura-entre-companias-acotada-y-auditada.md", "src/Flit.Infrastructure/Persistence/Sql/Ddl/127-HU13086-integrations-external-access-log.sql", "external_access_log")]
    public void AC1_LoQueNombraElAdrExisteEnElCodigo(string archivo, string rutaCodigo, string simbolo)
    {
        Leer(archivo).Should().Contain(simbolo);
        var ruta = Path.Combine(RaizCoreApi(), rutaCodigo);
        File.Exists(ruta).Should().BeTrue($"{archivo} nombra {simbolo}, que vive en {rutaCodigo}");
        File.ReadAllText(ruta).Should().Contain(simbolo);
    }

    private static string Leer(string archivo)
    {
        var ruta = Path.Combine(RaizCoreApi(), "docs", "adr", archivo);
        File.Exists(ruta).Should().BeTrue($"el ADR {archivo} debe existir");
        return File.ReadAllText(ruta);
    }

    private static string RaizCoreApi()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Flit.slnx")))
        {
            dir = dir.Parent;
        }

        dir.Should().NotBeNull("las pruebas corren dentro de services/core-api (Flit.slnx)");
        return dir!.FullName;
    }

    [GeneratedRegex(@"^\*\*Status\*\*:\s*(.+)$", RegexOptions.Multiline)]
    private static partial Regex Estado();

    [GeneratedRegex(@"^### Opción \d", RegexOptions.Multiline)]
    private static partial Regex Alternativa();
}
