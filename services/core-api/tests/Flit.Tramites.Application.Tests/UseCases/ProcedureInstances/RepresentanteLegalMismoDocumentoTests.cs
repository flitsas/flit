using Flit.Tramites.Application.UseCases.ProcedureInstances;
using FluentAssertions;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

/// <summary>
/// Bug #13194 (review PR #510, L1 de security) — <see cref="RepresentanteLegalDesdeDirectorio.MismoDocumento"/>
/// no puede emparejar a dos personas distintas: el tipo cuenta cuando ambos lados lo traen, los documentos
/// con letras se comparan exactos y un número sin dígitos no empareja con nadie.
/// </summary>
/// <remarks>
/// Uso de ejemplo: <c>RepresentanteLegalDesdeDirectorio.MismoDocumento("CC", "9.000.000.801", "CC", "9000000801")</c> → true.
/// Datos ficticios.
/// </remarks>
public sealed class RepresentanteLegalMismoDocumentoTests
{
    [Theory]
    [InlineData("CC", "9.000.000.801", "CC", "9000000801")]
    [InlineData("CC", "009000000801", "cc", "9000000801")]
    [InlineData(null, "9000000801", "CC", "9000000801")]
    [InlineData("C.C.", "9.000.000.801", "CC", "9000000801")]
    [InlineData("PA", "ab-123456", "PA", "AB123456")]
    public void MismaPersona_Empareja(string? tipoA, string numeroA, string? tipoB, string numeroB) =>
        RepresentanteLegalDesdeDirectorio.MismoDocumento(tipoA, numeroA, tipoB, numeroB).Should().BeTrue();

    [Fact]
    public void CcFrenteACe_ConLosMismosDigitos_NoEmpareja() =>
        RepresentanteLegalDesdeDirectorio.MismoDocumento("CC", "9000000801", "CE", "9000000801").Should().BeFalse();

    [Theory]
    [InlineData("", "9000000801")]
    [InlineData("0000", "0")]
    [InlineData("---", "---")]
    [InlineData(null, "9000000801")]
    public void SinDigitosOVacio_NoEmpareja(string? numeroA, string numeroB) =>
        RepresentanteLegalDesdeDirectorio.MismoDocumento("CC", numeroA, "CC", numeroB).Should().BeFalse();

    [Fact]
    public void PasaportesDistintosConLosMismosDigitos_NoEmparejan() =>
        RepresentanteLegalDesdeDirectorio.MismoDocumento("PA", "AB123456", "PA", "XY123456").Should().BeFalse();
}
