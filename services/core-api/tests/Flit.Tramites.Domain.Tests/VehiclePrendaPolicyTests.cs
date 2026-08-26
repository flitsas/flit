using Flit.Tramites.Domain.Tramites.Services;
using Flit.Tramites.Domain.Tramites.ValueObjects;
using FluentAssertions;
using Xunit;

namespace Flit.Tramites.Domain.Tests;

/// <summary>
/// Precondición registral del levantamiento de prenda: no se puede levantar un gravamen que el RUNT
/// no reporta. Sin él no hay acreedor que nombrar en el numeral 20 —lo precarga el propio gravamen—
/// ni acto que soportar.
/// </summary>
public sealed class VehiclePrendaPolicyTests
{
    [Fact]
    public void SinGravamenEnRunt_Bloquea()
    {
        var block = VehiclePrendaPolicy.Evaluar("LEVANTAMIENTO_PRENDA", "ok");

        block.Should().NotBeNull();
        block!.ProcedureType.Should().Be(VehiclePrendaPolicy.ProcedureTypeLevantamiento);
    }

    [Theory]
    [InlineData("warn")]
    [InlineData("fail")]
    public void ConGravamenReportado_NoBloquea(string estado)
    {
        VehiclePrendaPolicy.Evaluar("LEVANTAMIENTO_PRENDA", estado).Should().BeNull();
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData(null)]
    [InlineData("")]
    public void SinInformacionDeGravamenes_NoBloquea(string? estado)
    {
        // «No se sabe» NO es «no tiene»: convertir un dato ausente del RUNT en un trámite imposible
        // de radicar castigaría al gestor por una falla ajena. Mismo criterio que en carrocería.
        VehiclePrendaPolicy.Evaluar("LEVANTAMIENTO_PRENDA", estado).Should().BeNull();
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
        VehiclePrendaPolicy.Evaluar(code, "ok").Should().BeNull();
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
