using Flit.Diagnostico.Domain.Personas;
using FluentAssertions;
using Xunit;

namespace Flit.Diagnostico.Tests.Dominio;

/// <summary>
/// HU #13749 (F02-H4), escenario 9 (F02-C13) — forma del documento del locatario: tipo CC, CE, NIT, PAS o TI y número solo
/// dígitos salvo PAS. Ausente (sin tipo ni número) es válido porque la columna es opcional (R-3). Documentos inventados.
/// </summary>
/// <example>
/// <code>
/// ValidadorDocumentoPersona.Validar("cc", " 1020304050 ") // → ("CC", "1020304050"), EsValido y Presente
/// </code>
/// </example>
public sealed class ValidadorDocumentoPersonaTests
{
    [Theory]
    [InlineData("CC", "1020304050", "CC", "1020304050")]
    [InlineData(" cc ", " 1020304050 ", "CC", "1020304050")]
    [InlineData("CE", "445566", "CE", "445566")]
    [InlineData("NIT", "900111222", "NIT", "900111222")]
    [InlineData("TI", "1122334455", "TI", "1122334455")]
    [InlineData("PAS", "ab12345", "PAS", "AB12345")]
    public void Acepta_los_cinco_tipos_con_numero_de_forma_valida(string tipo, string numero, string tipoEsperado, string numeroEsperado)
    {
        var resultado = ValidadorDocumentoPersona.Validar(tipo, numero);

        resultado.EsValido.Should().BeTrue(resultado.Mensaje);
        resultado.Presente.Should().BeTrue();
        resultado.Tipo.Should().Be(tipoEsperado);
        resultado.Numero.Should().Be(numeroEsperado);
    }

    [Theory]
    [InlineData("CC", "12AB34", ValidadorDocumentoPersona.CodigoNumeroInvalido)]
    [InlineData("NIT", "900111222-7", ValidadorDocumentoPersona.CodigoNumeroInvalido)]
    [InlineData("TI", "11 22", ValidadorDocumentoPersona.CodigoNumeroInvalido)]
    [InlineData("PAS", "AB-123", ValidadorDocumentoPersona.CodigoNumeroInvalido)]
    [InlineData("CC", "123456789012345678901", ValidadorDocumentoPersona.CodigoNumeroInvalido)]
    [InlineData("RC", "1020304050", ValidadorDocumentoPersona.CodigoTipoInvalido)]
    [InlineData("CC", "", ValidadorDocumentoPersona.CodigoIncompleto)]
    [InlineData(null, "1020304050", ValidadorDocumentoPersona.CodigoIncompleto)]
    public void Rechaza_numero_con_letras_tipo_desconocido_o_documento_incompleto(string? tipo, string? numero, string codigo)
    {
        var resultado = ValidadorDocumentoPersona.Validar(tipo, numero);

        resultado.EsValido.Should().BeFalse();
        resultado.Presente.Should().BeFalse();
        resultado.Codigo.Should().Be(codigo);
        resultado.Mensaje.Should().NotBeNullOrWhiteSpace();
        if (!string.IsNullOrEmpty(numero))
            resultado.Mensaje.Should().NotContain(numero, "el mensaje no repite el documento");
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", "  ")]
    public void Sin_tipo_ni_numero_el_documento_esta_ausente(string? tipo, string? numero)
    {
        var resultado = ValidadorDocumentoPersona.Validar(tipo, numero);

        resultado.Should().Be(ResultadoDocumento.Ausente);
        resultado.EsValido.Should().BeTrue();
        resultado.Presente.Should().BeFalse();
    }
}
