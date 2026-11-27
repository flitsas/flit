using Flit.Tramites.Application.UseCases.ConsolidadoLotes;
using FluentAssertions;
using Xunit;

namespace Flit.Infrastructure.Tests.Documents;

/// <summary>
/// HU #12371 AC8 + HU #14735 — todos los documentos que FLIT genera se llaman igual:
/// <c>{tipo}_{radicado}_{placa}.pdf</c>, con el guion del radicado intacto (<c>FT1-0000012</c>, no
/// <c>FT1_0000012</c>). El armado vive en un único punto (<see cref="NombreDocumentoTramite"/>) y los
/// generadores no limpian el radicado por su cuenta.
/// </summary>
public sealed class NombreDeArchivoConRadicadoTests
{
    [Theory]
    [InlineData("mandato")]
    [InlineData("tramite_virtual")]
    [InlineData("fur")]
    [InlineData("compraventa")]
    public void Generar_ConservaElGuionDelRadicado(string tipo)
    {
        NombreDocumentoTramite.Generar(tipo, "FT1-0000012", "abc123")
            .Should().Be($"{tipo}_FT1-0000012_ABC123.pdf");
    }

    [Fact]
    public void Generar_SinPlaca_UsaSinPlaca()
    {
        NombreDocumentoTramite.Generar("mandato", "FT1-0000012", null)
            .Should().Be("mandato_FT1-0000012_SIN-PLACA.pdf");
    }
}
