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

    [Theory]
    [InlineData("7600100000005652501", 19)]
    [InlineData("760010000000565250170", 21)]
    [InlineData("7600-1000.0000565 2501", 19)]
    [InlineData("76001000000056525017", null)]
    [InlineData("D05001000000054280231", null)]
    [InlineData("1100128527016", null)]
    [InlineData(null, null)]
    public void AvisoDeLectura_SoloCon19o21Digitos(string? texto, int? digitos)
    {
        var aviso = NumeroComparendo.AvisoDeLectura(texto);
        if (digitos is null)
            aviso.Should().BeNull();
        else
            aviso.Should().Contain($"{digitos} dígitos");
    }
}

public sealed class TextoNormalizadoTests
{
    [Theory]
    [InlineData("Secretaría  Distrital de Movilidad de Bogotá ", "SECRETARIA DISTRITAL DE MOVILIDAD DE BOGOTA")]
    [InlineData("F. DPTAL TTO VICHADA/PUERTO CARREÑO", "F. DPTAL TTO VICHADA/PUERTO CARRENO")]
    [InlineData(null, "")]
    public void De_quita_tildes_y_espacios(string? texto, string esperado) =>
        Flit.Comparendos.Domain.Textos.TextoNormalizado.De(texto).Should().Be(esperado);

    /// <summary>Hallazgos B6-2 y B9-5: el NIT del infractor se muestra «base-DV» aunque la fuente lo traiga pegado.</summary>
    [Theory]
    [InlineData("8110117798", "811011779", "811011779-8")]
    [InlineData("811011779", null, "811011779-8")]
    [InlineData("890903938-8", null, "890903938-8")]
    [InlineData("900.123.456", null, "900123456-8")]
    public void ConDigito_ComoEnLosDocumentos(string documento, string? conocida, string esperado) =>
        Nit.ConDigito(documento, conocida).Should().Be(esperado);
}
