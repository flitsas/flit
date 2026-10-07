using System.Runtime.CompilerServices;
using System.Xml.Linq;
using FluentAssertions;
using Xunit;

namespace Flit.Identity.Tests;

/// <summary>
/// HU #13233/#13234 (Epic #13217) — lo que hace que core-identity sea un servicio aparte en el build: su árbol de
/// referencias de proyecto no incluye nada de negocio. Un cambio en Trámites, OT, reportes o consultas no puede
/// reconstruir core-identity. Si esta prueba falla, alguien agregó una referencia que rompe la separación.
/// </summary>
public sealed class BuildClosureTests
{
    private static readonly string[] Forbidden =
    [
        "Flit.Infrastructure", "Flit.Api", "Flit.Gateway", "Flit.Tramites.", "Flit.Analytics.", "Flit.DrFlit.",
        "Flit.Modules.Quipux.", "Flit.Modules.Improntas.Application", "Flit.Ict.",
    ];

    [Fact]
    public void ElArbolDeCoreIdentity_NoIncluyeCodigoDeNegocio()
    {
        var closure = Closure(HostProject());

        closure.Should().NotBeEmpty();
        closure.Select(Path.GetFileNameWithoutExtension).Should().NotContain(
            name => Forbidden.Any(f => name!.Equals(f, StringComparison.Ordinal) || (f.EndsWith('.') && name!.StartsWith(f, StringComparison.Ordinal))),
            "core-identity no puede depender de Trámites ni de Flit.Infrastructure");
    }

    [Fact]
    public void ElArbolDeCoreIdentity_EsElEsperado()
    {
        // Lista explícita: si cambia, que sea a propósito (y que el Dockerfile y el filtro del CD se actualicen).
        Closure(HostProject()).Select(Path.GetFileNameWithoutExtension).Order(StringComparer.Ordinal).Should().Equal(
            "Flit.Admin.Application",
            "Flit.Admin.Domain",
            "Flit.Identidad.Grpc.Contracts",
            "Flit.Identity.Application",
            "Flit.Identity.Infrastructure",
            "Flit.Identity.Web",
            "Flit.Modules.Improntas.Domain",
            "Flit.Modules.Notificaciones",
            "Flit.Modules.Platform",
            "Flit.Modules.Security.Application",
            "Flit.Modules.Security.Domain",
            "Flit.Platform.Sdk",
            "Flit.Queries.Domain",
            "Flit.Suite.AspNetCore");
    }

    [Fact]
    public void ElDockerfileCopiaTodoElArbol()
    {
        var dockerfile = File.ReadAllText(Path.Combine(ServiceRoot(), "Dockerfile"));

        foreach (var name in Closure(HostProject()).Where(p => p.Contains($"{Path.DirectorySeparatorChar}core-api{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                     .Select(Path.GetFileNameWithoutExtension))
        {
            dockerfile.Should().Contain($"COPY core-api/src/{name}/", $"el Dockerfile de core-identity debe copiar {name}");
        }
    }

    [Fact]
    public void LosContratosGrpcLleganALaImagenYAlFiltroDelCd()
    {
        // HU #13334: los Flit.*.Grpc.Contracts generan código desde contracts/proto, fuera del contexto services/.
        Closure(HostProject()).Should().Contain(p => p.EndsWith(".Grpc.Contracts.csproj", StringComparison.Ordinal));

        File.ReadAllText(Path.Combine(ServiceRoot(), "Dockerfile")).Should().Contain("COPY --from=contracts . /contracts/proto/");
        var cd = File.ReadAllText(Path.Combine(ServiceRoot(), "..", "..", ".github", "workflows", "cd.yml"));
        var job = cd[cd.IndexOf("build-core-identity:", StringComparison.Ordinal)..];
        job = job[..job.IndexOf("\n  build-", 1, StringComparison.Ordinal)];
        job.Should().Contain("contracts=./contracts/proto").And.Contain("            contracts/proto\n");
    }

    [Fact]
    public void ElCdReconstruyeCoreIdentityCuandoCambiaCualquierParteDeSuArbol()
    {
        // El job build-core-identity reutiliza la imagen si nada de esta lista cambió: tiene que cubrir todo el árbol.
        var cd = File.ReadAllText(Path.Combine(ServiceRoot(), "..", "..", ".github", "workflows", "cd.yml"));
        var job = cd[cd.IndexOf("build-core-identity:", StringComparison.Ordinal)..];

        job.Should().Contain("services/core-identity\n");
        foreach (var name in Closure(HostProject()).Where(p => p.Contains($"{Path.DirectorySeparatorChar}core-api{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                     .Select(Path.GetFileNameWithoutExtension))
        {
            job.Should().Contain($"services/core-api/src/{name}\n", $"el CD tiene que reconstruir core-identity si cambia {name}");
        }
    }

    internal static string ServiceRoot([CallerFilePath] string here = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", ".."));

    /// <summary>Todos los .csproj alcanzables por ProjectReference (sin incluir el anfitrión).</summary>
    internal static IReadOnlyList<string> Closure(string project)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Stack<string>([project]);
        while (pending.TryPop(out var current))
        {
            var dir = Path.GetDirectoryName(current)!;
            foreach (var reference in XDocument.Load(current).Descendants("ProjectReference"))
            {
                var path = Path.GetFullPath(Path.Combine(dir, reference.Attribute("Include")!.Value.Replace('\\', Path.DirectorySeparatorChar)));
                if (seen.Add(path))
                    pending.Push(path);
            }
        }

        return [.. seen];
    }

    internal static string HostProject([CallerFilePath] string here = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", "..", "src", "Flit.Identity.Api", "Flit.Identity.Api.csproj"));
}
