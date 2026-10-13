using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace Flit.Tramites.Application.Tests.ConsolidadoLotes;

/// <summary>
/// HU #13371 (patrón <c>ConsolidadoEntregaArchitectureTests</c>) — invariantes del lote que permiten generar
/// en estado final sin abrir la puerta del endpoint individual. Barrido léxico del código (sin comentarios)
/// de <c>services/core-api/src</c>; falla nombrando el archivo:
/// <list type="bullet">
///   <item>Solo el lote pasa <c>soloSiNoExiste</c>: fuera de los dos generadores que lo declaran, ningún
///   archivo lo menciona salvo los de una carpeta <c>ConsolidadoLotes</c>.</item>
///   <item>El lote no referencia la entrega individual (<c>EntregarConsolidadoHandler</c>), el POST del
///   gestor (handler, endpoint o ruta) ni la cola de regeneración anticipada de #12760.</item>
///   <item>El lote siempre genera con <c>force: false</c> y <c>soloSiNoExiste: true</c>.</item>
///   <item>El <c>POST /instances/{id}/consolidado</c> sigue pasando por <c>GeneracionDocumentalGestorGuard</c>
///   (AC7: en estado final responde <c>generacion_bloqueada_estado_final</c>).</item>
/// </list>
/// <para>Uso de ejemplo: no se invoca; corre en CI.</para>
/// </summary>
public sealed class ConsolidadoLoteArchitectureTests
{
    private const string Guarda = "soloSiNoExiste";

    private static readonly string[] GeneradoresQueDeclaranLaGuarda = ["ConsolidadoCommand.cs", "ConsolidadoMaestroCommand.cs"];

    private static readonly string[] ProhibidosEnElLote =
    [
        "EntregarConsolidadoHandler",
        "EntregarConsolidadoRequest",
        "GenerarConsolidadoConRespaldoHandler",
        "ConsolidadoEndpoints",
        "GenerarProcedureInstanceConsolidado",
        "/instances/{id:guid}/consolidado",
        "GeneracionDocumentalGestorGuard",
        "IConsolidadoRegeneracionQueue",
        "RegenerarConsolidadoAnticipadoHandler",
    ];

    [Fact]
    public void SoloElLote_PasaSoloSiNoExiste()
    {
        var usos = ArchivosSrc()
            .Where(f => Codigo(f).Contains(Guarda, StringComparison.Ordinal))
            .ToList();

        usos.Where(EsDelLote).Should().NotBeEmpty("el entregador del lote pasa la guarda (no en verde por vacuidad)");
        usos.Where(f => !EsDelLote(f))
            .Select(Path.GetFileName)
            .Should().BeSubsetOf(GeneradoresQueDeclaranLaGuarda,
                "soloSiNoExiste es exclusivo del lote de descarga masiva (HU #13371): ningún otro llamador lo pasa");
    }

    [Fact]
    public void ElLote_NoReferencia_LaEntregaIndividual_NiElPostDelGestor_NiLaCola()
    {
        var lote = ArchivosSrc().Where(EsDelLote).ToList();
        lote.Should().NotBeEmpty();

        var violaciones = (from f in lote
                           let codigo = Codigo(f)
                           from token in ProhibidosEnElLote
                           where codigo.Contains(token, StringComparison.Ordinal)
                           select $"{Path.GetFileName(f)} → {token}").ToList();

        violaciones.Should().BeEmpty("el lote genera por el handler oficial, nunca por la entrega ni por el POST del gestor");
    }

    [Fact]
    public void ElLote_GeneraSiempreConForceFalse_YSoloSiNoExisteTrue()
    {
        var entregador = Codigo(ArchivosSrc().Single(f => Path.GetFileName(f) == "ConsolidadoLoteEntregador.cs"));

        Regex.Matches(entregador, @"force:\s*true").Should().BeEmpty();
        Regex.Matches(entregador, @"soloSiNoExiste:\s*false").Should().BeEmpty();
        Regex.Matches(entregador, @"soloSiNoExiste:\s*true").Should().HaveCount(2, "wizard y maestro");
        Regex.Matches(entregador, @"userId:\s*null").Should().HaveCount(1, "Q3: sin usuario, sin impronta en cascada");
    }

    [Fact]
    public void AC7_ElPostIndividual_SigueCerradoEnEstadoFinal_PorElGuardDelGestor()
    {
        var endpoints = Codigo(ArchivosSrc().Single(f => Path.GetFileName(f) == "ConsolidadoEndpoints.cs"));
        var post = endpoints[..endpoints.IndexOf("GenerarProcedureInstanceConsolidado", StringComparison.Ordinal)];

        post.Should().Contain("GeneracionDocumentalGestorGuard estadoGuard");
        post.Should().Contain("estadoGuard.CheckAsync(");
        post.Should().Contain("GeneracionEstadoProblem.From(estadoError)");
        post.IndexOf("estadoGuard.CheckAsync(", StringComparison.Ordinal)
            .Should().BeLessThan(post.IndexOf("handler.HandleAsync(", StringComparison.Ordinal),
                "el guard corre antes de generar");
    }

    private static bool EsDelLote(string file) =>
        file.Contains($"{Path.DirectorySeparatorChar}ConsolidadoLotes{Path.DirectorySeparatorChar}", StringComparison.Ordinal);

    private static List<string> ArchivosSrc()
    {
        var dir = LocateCoreApiSourceDirectory();
        return Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();
    }

    private static string Codigo(string file) =>
        string.Join('\n', File.ReadAllLines(file).Where(l =>
        {
            var t = l.TrimStart();
            return !t.StartsWith("//", StringComparison.Ordinal) && !t.StartsWith('*');
        }));

    private static string LocateCoreApiSourceDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "src");
            if (File.Exists(Path.Combine(candidate, "Flit.Api", "Flit.Api.csproj")))
                return candidate;
            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("No se encontró src/Flit.Api subiendo desde " + AppContext.BaseDirectory);
    }
}
