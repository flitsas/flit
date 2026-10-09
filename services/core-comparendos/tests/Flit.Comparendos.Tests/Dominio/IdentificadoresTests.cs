using Flit.Comparendos.Domain.Identificadores;
using FluentAssertions;
using Xunit;

namespace Flit.Comparendos.Tests.Dominio;

public sealed class IdentificadoresTests
{
    [Theory]
    [InlineData("890903938", 8)]
    [InlineData("899999068", 1)]
    [InlineData("800197268", 4)]
    public void DigitoVerificacion_de_NITs_conocidos(string nit, int dv) => Nit.DigitoVerificacion(nit).Should().Be(dv);

    [Theory]
    [InlineData("890.903.938-8", "890903938", 8)]
    [InlineData(" 890903938 ", "890903938", null)]
    [InlineData("0890903938", "890903938", null)]
    public void Separar_limpia_puntos_guion_y_ceros(string texto, string baseNit, int? dv) =>
        Nit.Separar(texto).Should().Be((baseNit, dv));

    [Theory]
    [InlineData("")]
    [InlineData("ABC123")]
    [InlineData("890-90-3")]
    [InlineData("890903938-88")]
    public void Separar_rechaza_lo_que_no_es_NIT(string texto) => Nit.Separar(texto).Should().BeNull();

    [Fact]
    public void Variantes_consulta_con_y_sin_digito() =>
        Nit.Variantes("890903938").Should().Equal("890903938", "8909039388");

    [Theory]
    [InlineData("abc-123", "ABC123")]
    [InlineData(" kxl 12f ", "KXL12F")]
    [InlineData("A1", null)]
    public void Placa_normaliza(string texto, string? esperada) => Placa.Normalizar(texto).Should().Be(esperada);
}

public sealed class NumeroComparendoTests
{
    [Theory]
    [InlineData(" d-123 456 ", "D123456")]
    [InlineData("11001000000012345678", "11001000000012345678")]
    [InlineData("  ", null)]
    [InlineData("---", null)]
    public void Normalizar_conserva_prefijo_y_quita_separadores(string texto, string? esperado) =>
        NumeroComparendo.Normalizar(texto).Should().Be(esperado);
}
