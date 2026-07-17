using Flit.Tramites.Application.UseCases.Consultations;
using FluentAssertions;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.Consultations;

/// <summary>
/// FEATURE 05 (HU #10758) — la tabla de verdad del resolver de proveedor de comparendos.
/// Regla: la fuente manda; el tipo de persona solo elige cuál proveedor EXTERNO.
/// </summary>
public sealed class FinesProviderResolverTests
{
    [Theory]
    // Fuente interna: el API de FLIT atiende a ambos tipos de persona.
    [InlineData("internal", true, "flit_fines")]
    [InlineData("internal", false, "flit_fines")]
    // Fuente externa: el tipo de persona decide el proveedor.
    [InlineData("external", true, "verifik_simit")]
    [InlineData("external", false, "kyverum_fines")]
    public void Resolve_AplicaLaMatrizDeFuenteYTipoDePersona(
        string source, bool isNatural, string expected) =>
        FinesProviderResolver.Resolve(source, isNatural).Should().Be(expected);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("INTERNO")]      // no es el código: el CHECK de la columna solo admite internal|external
    [InlineData("cualquier-cosa")]
    public void Resolve_FuenteAusenteODesconocida_CaeAExterna(string? source)
    {
        // Default seguro alineado con el DDL: una compañía sin configuración (o con un valor
        // corrupto) sigue consultando en línea, no se queda sin consulta.
        FinesProviderResolver.Resolve(source, isNaturalPerson: true).Should().Be("verifik_simit");
        FinesProviderResolver.Resolve(source, isNaturalPerson: false).Should().Be("kyverum_fines");
    }

    [Theory]
    [InlineData("INTERNAL")]
    [InlineData("Internal")]
    [InlineData("  internal  ")]
    public void Resolve_FuenteInterna_EsInsensibleAMayusculasYEspacios(string source) =>
        FinesProviderResolver.Resolve(source, isNaturalPerson: true).Should().Be("flit_fines");

    [Fact]
    public void Normalize_SoloReconoceLosDosCodigosDelCheckDeLaColumna()
    {
        FinesSourceCodes.Normalize("internal").Should().Be(FinesSourceCodes.Internal);
        FinesSourceCodes.Normalize("external").Should().Be(FinesSourceCodes.External);
        FinesSourceCodes.Normalize(null).Should().Be(FinesSourceCodes.External);
    }
}
