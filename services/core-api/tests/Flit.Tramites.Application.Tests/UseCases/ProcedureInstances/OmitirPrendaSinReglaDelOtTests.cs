using System.Reflection;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Tramites.ValueObjects;
using FluentAssertions;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

/// <summary>
/// HU #13777 (AC6, Bug #13768 punto 1) — «Omitir prenda» ya no depende de la familia ni de la política
/// del OT, así que el código de error <c>prenda_omitir_no_admitido</c> y la regla
/// <c>PrendaDecision.OmitirAdmitido</c> desaparecen del código de core-api y del contrato OpenAPI.
/// <para>Uso de ejemplo: <c>typeof(PrendaDecision).GetMethod("OmitirAdmitido")</c> → <c>null</c>.</para>
/// </summary>
public sealed class OmitirPrendaSinReglaDelOtTests
{
    private const string CodigoRetirado = "prenda_omitir_no_admitido";

    [Fact]
    public void PrendaDecision_YaNoExponeLaReglaOmitirAdmitido()
    {
        typeof(PrendaDecision)
            .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            .Should().NotContain(m => m.Name == "OmitirAdmitido");
    }

    [Fact]
    public void RegistrarPrendaHandler_YaNoDeclaraElErrorOmitirNoAdmitido()
    {
        typeof(RegistrarPrendaHandler)
            .GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            .Should().NotContain(f => f.Name == "OmitirNoAdmitidoError"
                || (f.IsLiteral && Equals(f.GetRawConstantValue(), CodigoRetirado)));
    }

    [Fact]
    public void ElCodigoRetirado_NoApareceNiEnElCodigoNiEnElOpenApi()
    {
        var raiz = RaizDelRepo();
        var yaml = File.ReadAllText(Path.Combine(raiz, "contracts", "openapi", "core-api.v1.yaml"));
        yaml.Should().NotContain(CodigoRetirado);

        var src = Path.Combine(raiz, "services", "core-api", "src");
        var conCodigo = Directory
            .EnumerateFiles(src, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .Where(f => File.ReadAllText(f).Contains(CodigoRetirado, StringComparison.Ordinal))
            .Select(f => Path.GetRelativePath(raiz, f))
            .ToList();
        conCodigo.Should().BeEmpty();
    }

    private static string RaizDelRepo()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "contracts", "openapi", "core-api.v1.yaml")))
            dir = dir.Parent;
        dir.Should().NotBeNull("el test corre dentro del repo con contracts/openapi/core-api.v1.yaml");
        return dir!.FullName;
    }
}
