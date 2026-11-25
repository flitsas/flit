using System.Text;
using Flit.Impuestos.Api.Modulos.Avisos;
using Flit.Impuestos.Tests.Aislamiento;
using FluentAssertions;
using Xunit;

namespace Flit.Impuestos.Tests.Avisos;

/// <summary>
/// HU #14694 (M10; F2b-CF1, F2b-CF2) — la firma del webhook del proveedor (guía de integración §6): <c>v1=</c> + hex de
/// HMAC-SHA256 con el <c>webhook_secret</c> completo (con su prefijo <c>whsec_</c>) sobre <c>"{X-Flit-Timestamp}.{cuerpo crudo}"</c>,
/// comparada en tiempo constante, con el timestamp a ±300 s del reloj. Incluye el vector de prueba de la guía.
/// </summary>
/// <example>
/// <code>
/// VerificadorFirma.Verificar(secreto, timestamp, firma, cuerpoCrudo, ahora); // ResultadoFirma.Valida
/// </code>
/// </example>
public sealed class VerificadorFirmaTests
{
    // Vector de la guía de integración §6 «Probar la verificación de la firma».
    private const string SecretoVector = "whsec_prueba";
    private const string TimestampVector = "1700000000";
    private const string CuerpoVector = "{\"a\":1}";
    private const string FirmaVector = "v1=0ca0bb9c916989fb6c1d1eb729a180da30cb078b7cbe551c8f8da0ec452ac9db";

    private static readonly byte[] Cuerpo = Encoding.UTF8.GetBytes(CuerpoVector);
    private static readonly DateTimeOffset AhoraVector = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000);

    [Fact]
    public void El_vector_de_la_guia_da_exactamente_la_firma_esperada()
    {
        VerificadorFirma.Firmar(SecretoVector, TimestampVector, Cuerpo).Should().Be(FirmaVector);
        VerificadorFirma.Verificar(SecretoVector, TimestampVector, FirmaVector, Cuerpo, AhoraVector).Should().Be(ResultadoFirma.Valida);
    }

    [Theory]
    [InlineData(300, ResultadoFirma.Valida)]
    [InlineData(-300, ResultadoFirma.Valida)]
    [InlineData(301, ResultadoFirma.FueraDeVentana)]
    [InlineData(-301, ResultadoFirma.FueraDeVentana)]
    public void El_timestamp_vale_hasta_300_segundos_de_diferencia_con_el_reloj(int segundos, ResultadoFirma esperado) =>
        VerificadorFirma.Verificar(SecretoVector, TimestampVector, FirmaVector, Cuerpo, AhoraVector.AddSeconds(segundos)).Should().Be(esperado);

    [Theory]
    [InlineData("v1=1ca0bb9c916989fb6c1d1eb729a180da30cb078b7cbe551c8f8da0ec452ac9db")] // primer dígito
    [InlineData("v1=0ca0bb9c916989fb6c1d1eb729a180da30cb078b7cbe551c8f8da0ec452ac9dc")] // último dígito
    [InlineData("v1=0ca0bb9c916989fb6c1d1eb729a180da30cb078b7cbe551c8f8da0ec452ac9d")] // más corta
    [InlineData("0ca0bb9c916989fb6c1d1eb729a180da30cb078b7cbe551c8f8da0ec452ac9db")] // sin v1=
    [InlineData("v2=0ca0bb9c916989fb6c1d1eb729a180da30cb078b7cbe551c8f8da0ec452ac9db")] // otra versión
    [InlineData("v1=0CA0BB9C916989FB6C1D1EB729A180DA30CB078B7CBE551C8F8DA0EC452AC9DB")] // el hex del proveedor va en minúsculas
    public void Una_firma_alterada_es_invalida(string firma) =>
        VerificadorFirma.Verificar(SecretoVector, TimestampVector, firma, Cuerpo, AhoraVector).Should().Be(ResultadoFirma.FirmaInvalida);

    [Fact]
    public void La_firma_es_sobre_el_cuerpo_crudo_y_con_el_secreto_completo()
    {
        VerificadorFirma.Verificar(SecretoVector, TimestampVector, FirmaVector, Encoding.UTF8.GetBytes("{\"a\": 1}"), AhoraVector)
            .Should().Be(ResultadoFirma.FirmaInvalida, "reformatear el JSON cambia la firma");
        VerificadorFirma.Verificar("prueba", TimestampVector, FirmaVector, Cuerpo, AhoraVector)
            .Should().Be(ResultadoFirma.FirmaInvalida, "la clave es el secreto con su prefijo whsec_");
        VerificadorFirma.Verificar(SecretoVector, "1700000001", FirmaVector, Cuerpo, AhoraVector)
            .Should().Be(ResultadoFirma.FirmaInvalida, "el timestamp entra en lo firmado");
    }

    [Theory]
    [InlineData(null, FirmaVector, ResultadoFirma.SinCabeceras)]
    [InlineData(TimestampVector, null, ResultadoFirma.SinCabeceras)]
    [InlineData("", FirmaVector, ResultadoFirma.SinCabeceras)]
    [InlineData("1700000000.5", FirmaVector, ResultadoFirma.TimestampInvalido)]
    [InlineData("hoy", FirmaVector, ResultadoFirma.TimestampInvalido)]
    public void Sin_cabeceras_o_con_timestamp_no_numerico_no_verifica(string? timestamp, string? firma, ResultadoFirma esperado) =>
        VerificadorFirma.Verificar(SecretoVector, timestamp, firma, Cuerpo, AhoraVector).Should().Be(esperado);

    [Fact]
    public void En_la_ventana_de_rotacion_vale_el_secreto_vigente_o_el_anterior()
    {
        VerificadorFirma.Verificar(["whsec_vigente", SecretoVector], TimestampVector, FirmaVector, Cuerpo, AhoraVector)
            .Should().Be(ResultadoFirma.Valida, "si falla con el vigente se prueba con el anterior");
        VerificadorFirma.Verificar([SecretoVector, "whsec_anterior"], TimestampVector, FirmaVector, Cuerpo, AhoraVector).Should().Be(ResultadoFirma.Valida);
        VerificadorFirma.Verificar(["whsec_vigente", "whsec_anterior"], TimestampVector, FirmaVector, Cuerpo, AhoraVector).Should().Be(ResultadoFirma.FirmaInvalida);
        var vacia = () => VerificadorFirma.Verificar([], TimestampVector, FirmaVector, Cuerpo, AhoraVector);
        vacia.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Compara_en_tiempo_constante()
    {
        var fuente = File.ReadAllText(Path.Combine(
            IgnoreQueryFiltersSoloEnBackofficeTests.RaizDelServicio(), "src", "Flit.Impuestos.Api", "Modulos", "Avisos", "VerificadorFirma.cs"));

        fuente.Should().Contain("CryptographicOperations.FixedTimeEquals", "la comparación de la firma no corta en el primer byte distinto");
        fuente.Should().NotContain("SequenceEqual").And.NotContain("string.Equals(esperada").And.NotContain("== firma");
    }
}
