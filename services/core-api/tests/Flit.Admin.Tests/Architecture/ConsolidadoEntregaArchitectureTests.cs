using FluentAssertions;
using Xunit;

namespace Flit.Admin.Tests.Architecture;

/// <summary>
/// Épica #12760 (code-review menor 2 / security M1, HU #12785) — <c>EntregarConsolidadoHandler</c>
/// REGENERA el consolidado (sube un PDF y borra el anterior). Las rutas <c>/network</c> (la cabeza de red
/// leyendo trámites de sus hijas) son de LECTURA: no pueden reutilizarlo. El barrido es léxico sobre el
/// código (sin comentarios) de <c>Flit.Api</c> y falla nombrando el archivo:
/// <list type="bullet">
///   <item>Solo las rutas conocidas cablean el handler (gestor/SuperAdmin y consola OT). Un archivo nuevo
///   que lo use obliga a revisar esta regla y ampliar la lista a conciencia.</item>
///   <item>Ningún archivo que mapee rutas <c>/network</c> (o <c>Network*.cs</c>) lo menciona.</item>
/// </list>
/// <para>Uso de ejemplo: no se invoca; corre en CI.</para>
/// </summary>
public sealed class ConsolidadoEntregaArchitectureTests
{
    private const string Handler = "EntregarConsolidadoHandler";

    private static readonly string[] RutasPermitidas = ["ConsolidadoEndpoints.cs", "AdminOtEndpoints.cs"];

    [Fact]
    public void SoloLasRutasDeGestorYOtCablean_EntregarConsolidadoHandler()
    {
        var usos = ArchivosApi()
            .Where(f => CodigoSinComentarios(f).Contains(Handler, StringComparison.Ordinal))
            .Select(Path.GetFileName)
            .ToList();

        usos.Should().NotBeEmpty("el barrido debe encontrar las rutas que hoy lo usan (no en verde por vacuidad)");
        usos.Should().BeSubsetOf(RutasPermitidas,
            "EntregarConsolidadoHandler regenera: no se cablea en rutas nuevas (en particular /network) sin revisar esta regla");
    }

    [Fact]
    public void NingunaRutaNetwork_ReutilizaEntregarConsolidadoHandler()
    {
        var red = ArchivosApi()
            .Where(f => Path.GetFileName(f).StartsWith("Network", StringComparison.Ordinal)
                || CodigoSinComentarios(f).Contains("/network", StringComparison.Ordinal))
            .ToList();

        red.Should().NotBeEmpty("existen rutas /network (NetworkProcedureEndpoints, NetworkAttachmentEndpoints…)");
        red.Where(f => CodigoSinComentarios(f).Contains(Handler, StringComparison.Ordinal))
            .Select(Path.GetFileName)
            .Should().BeEmpty("las rutas /network son de lectura y el handler de entrega regenera el consolidado");
    }

    private static List<string> ArchivosApi()
    {
        var dir = Path.Combine(LocateCoreApiSourceDirectory(), "Flit.Api");
        return Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();
    }

    private static string CodigoSinComentarios(string file) =>
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
