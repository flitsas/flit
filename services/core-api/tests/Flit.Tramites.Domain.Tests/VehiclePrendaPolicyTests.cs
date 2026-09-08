using Flit.Tramites.Domain.Tramites.Services;
using Flit.Tramites.Domain.Tramites.ValueObjects;
using FluentAssertions;
using Xunit;

namespace Flit.Tramites.Domain.Tests;

/// <summary>
/// Precondición registral del levantamiento de prenda: el RUNT no reporta el gravamen que se
/// pretende levantar. HU #12131/#12129 — NUNCA bloquea: <see cref="VehiclePrendaPolicy.Evaluar"/>
/// solo dice si corresponde AVISAR (check <c>warn</c> + modal informativo).
/// </summary>
public sealed class VehiclePrendaPolicyTests
{
    [Fact]
    public void SinGravamenEnRunt_Avisa()
    {
        VehiclePrendaPolicy.Evaluar("LEVANTAMIENTO_PRENDA", "ok").Should().BeTrue();
    }

    [Theory]
    [InlineData("warn")]
    [InlineData("fail")]
    public void ConGravamenReportado_NoAvisa(string estado)
    {
        VehiclePrendaPolicy.Evaluar("LEVANTAMIENTO_PRENDA", estado).Should().BeFalse();
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData(null)]
    [InlineData("")]
    public void SinInformacionDeGravamenes_NoAvisa(string? estado)
    {
        // «No se sabe» NO es «no tiene»: no hay nada nuevo que informar sobre una incertidumbre que
        // el propio check "gravamenes" ya deja ver. Mismo criterio que en carrocería.
        VehiclePrendaPolicy.Evaluar("LEVANTAMIENTO_PRENDA", estado).Should().BeFalse();
    }

    [Theory]
    [InlineData("PRENDA_INSCRIPCION")]
    [InlineData("LEVANTAR_INSCRIBIR_PRENDA")]
    [InlineData("CAMBIO_ACREEDOR")]
    [InlineData("TRASPASO_STANDARD")]
    [InlineData("BLINDAJE")]
    [InlineData(null)]
    public void OtrosTipos_NoExigenPrendaPrevia(string? code)
    {
        // La inscripción CONSTITUYE el gravamen, así que no puede presuponerlo; y los dos tipos de
        // doble acción quedan fuera del alcance de este cambio a propósito.
        ProcedureTypeLayers.ExigePrendaPreviaEnRunt(code).Should().BeFalse();
        VehiclePrendaPolicy.Evaluar(code, "ok").Should().BeFalse();
    }

    [Theory]
    [InlineData("LEVANTAMIENTO_PRENDA")]
    [InlineData("  levantamiento_prenda  ")]
    public void ExigePrendaPrevia_NormalizaElCodigo(string code)
    {
        ProcedureTypeLayers.ExigePrendaPreviaEnRunt(code).Should().BeTrue();
    }

    [Theory]
    [InlineData("ok", true)]
    [InlineData("OK", true)]
    [InlineData("warn", false)]
    [InlineData("unknown", false)]
    [InlineData(null, false)]
    public void RuntAfirmaSinGravamen_SoloConOk(string? estado, bool esperado)
    {
        VehiclePrendaPolicy.RuntAfirmaSinGravamen(estado).Should().Be(esperado);
    }
}
