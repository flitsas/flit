using Flit.Infrastructure.KyverumRunt;
using FluentAssertions;
using Xunit;

namespace Flit.Infrastructure.Tests.KyverumRunt;

/// <summary>
/// HU #10478 — normalización del tipo de documento FLIT/Verifik → códigos Kyverum RUNT. Un código
/// desconocido devuelve null (Kyverum omite tipoDocumento y prueba C,T,E,Y,P).
/// </summary>
public sealed class KyverumRuntDocTypeTests
{
    [Theory]
    [InlineData("CC", "C")]
    [InlineData("C", "C")]
    [InlineData("cc", "C")]
    [InlineData("TI", "T")]
    [InlineData("CE", "E")]
    [InlineData("NIT", "N")]
    [InlineData("N", "N")]
    [InlineData("PAS", "P")]
    [InlineData("PPT", "P")]
    [InlineData(" ce ", "E")]
    public void Normalize_MapeaCodigosConocidos(string input, string expected) =>
        KyverumRuntDocType.Normalize(input).Should().Be(expected);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("XX")]
    [InlineData("DESCONOCIDO")]
    public void Normalize_DesconocidoOVacio_DevuelveNull(string? input) =>
        KyverumRuntDocType.Normalize(input).Should().BeNull();
}
