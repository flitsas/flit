using System.Text;
using Flit.Consultas.Api.Impuesto.Avisos;
using FluentAssertions;
using Xunit;

namespace Flit.Consultas.Tests.Impuesto.Avisos;

/// <summary>
/// HU #14711 (plan §3.3) — token opaco del <c>callbackUrl</c>: base64url de tenantId + HMAC truncado, 43 caracteres, sin base. Ida y
/// vuelta, alterado, con otra llave y sin forma.
/// <para>Uso de ejemplo: <c>TokenAvisoImpuesto.TryLeer(TokenAvisoImpuesto.Emitir(t, llave), llave, out var e)</c> → true y e == t.</para>
/// </summary>
public sealed class TokenAvisoImpuestoTests
{
    private static readonly Guid Empresa = Guid.Parse("0194aaaa-0000-7000-8000-000000000001");

    [Fact]
    public void IdaYVuelta_RecuperaLaEmpresa_Con43CaracteresBase64Url()
    {
        var token = TokenAvisoImpuesto.Emitir(Empresa, AvisosDePrueba.Llave);

        token.Should().HaveLength(43).And.MatchRegex("^[A-Za-z0-9_-]{43}$");
        token.Should().NotContain(Empresa.ToString("N"), "el token no deja leer la empresa en claro como texto");
        TokenAvisoImpuesto.TryLeer(token, AvisosDePrueba.Llave, out var empresa).Should().BeTrue();
        empresa.Should().Be(Empresa);
        TokenAvisoImpuesto.Emitir(Empresa, AvisosDePrueba.Llave).Should().Be(token, "es determinista: no necesita base");
    }

    [Fact]
    public void CadaCaracterAlterado_NoSeReconoce()
    {
        var token = TokenAvisoImpuesto.Emitir(Empresa, AvisosDePrueba.Llave);

        for (var i = 0; i < token.Length; i++)
        {
            var alterado = token[..i] + (token[i] == 'A' ? 'B' : 'A') + token[(i + 1)..];
            TokenAvisoImpuesto.TryLeer(alterado, AvisosDePrueba.Llave, out var empresa).Should().BeFalse($"posición {i}");
            empresa.Should().Be(Guid.Empty);
        }
    }

    [Fact]
    public void ConOtraLlave_NoSeReconoce_YOtraEmpresaDaOtroToken()
    {
        var token = TokenAvisoImpuesto.Emitir(Empresa, AvisosDePrueba.Llave);

        TokenAvisoImpuesto.TryLeer(token, Encoding.UTF8.GetBytes("otra-llave-de-prueba-0123456789-abcdefgh"), out _).Should().BeFalse();
        TokenAvisoImpuesto.Emitir(Guid.NewGuid(), AvisosDePrueba.Llave).Should().NotBe(token);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("corto")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA+")]
    public void SinForma_NoSeReconoce(string? token)
    {
        TokenAvisoImpuesto.TieneForma(token).Should().BeFalse();
        TokenAvisoImpuesto.TryLeer(token, AvisosDePrueba.Llave, out _).Should().BeFalse();
    }
}

/// <summary>
/// HU #14711 (guía §6) — firma <c>v1=</c> HMAC-SHA256 del <c>"{ts}.{cuerpo crudo}"</c> con el secreto completo: válida, inválida,
/// secreto anterior vigente o vencido, timestamp fuera de ±300 s y cabeceras ausentes.
/// <para>Uso de ejemplo: <c>VerificadorFirmaImpuesto.Verificar([actual, anterior], ts, firma, cuerpo, ahora)</c>.</para>
/// </summary>
public sealed class VerificadorFirmaImpuestoTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 11, 19, 15, 0, 0, TimeSpan.Zero);
    private static readonly byte[] Cuerpo = AvisosDePrueba.Cuerpo("ext-1", 1);

    [Fact]
    public void FirmaValida_ConElSecretoActual()
    {
        var ts = AvisosDePrueba.Ts(Ahora);
        var firma = AvisosDePrueba.Firmar(AvisosDePrueba.Secreto, ts, Cuerpo);

        VerificadorFirmaImpuesto.Firmar(AvisosDePrueba.Secreto, ts, Cuerpo).Should().Be(firma);
        VerificadorFirmaImpuesto.Verificar([AvisosDePrueba.Secreto], ts, firma, Cuerpo, Ahora).Should().Be(ResultadoFirmaImpuesto.Valida);
    }

    [Fact]
    public void FirmaInvalida_CuerpoAlteradoOSecretoSinPrefijo()
    {
        var ts = AvisosDePrueba.Ts(Ahora);
        var firma = AvisosDePrueba.Firmar(AvisosDePrueba.Secreto, ts, Cuerpo);
        var alterado = Cuerpo.ToArray();
        alterado[^2] ^= 1;

        VerificadorFirmaImpuesto.Verificar([AvisosDePrueba.Secreto], ts, firma, alterado, Ahora).Should().Be(ResultadoFirmaImpuesto.FirmaInvalida);
        VerificadorFirmaImpuesto.Verificar([AvisosDePrueba.Secreto["whsec_".Length..]], ts, firma, Cuerpo, Ahora)
            .Should().Be(ResultadoFirmaImpuesto.FirmaInvalida, "la clave es el secreto completo, con su prefijo whsec_");
        VerificadorFirmaImpuesto.Verificar([AvisosDePrueba.Secreto], ts, firma.ToUpperInvariant(), Cuerpo, Ahora)
            .Should().Be(ResultadoFirmaImpuesto.FirmaInvalida);
    }

    [Fact]
    public void SecretoAnteriorVigente_Valida_YVencido_NoEntraEnLosVigentes()
    {
        var ts = AvisosDePrueba.Ts(Ahora);
        var firmaVieja = AvisosDePrueba.Firmar(AvisosDePrueba.SecretoAnterior, ts, Cuerpo);
        var vigente = new SecretoWebhookImpuesto(true, AvisosDePrueba.Secreto, AvisosDePrueba.SecretoAnterior, Ahora.AddHours(1), 4);
        var vencido = vigente with { AnteriorVenceEn = Ahora.AddSeconds(-1) };

        vigente.Vigentes(Ahora).Should().Equal(AvisosDePrueba.Secreto, AvisosDePrueba.SecretoAnterior);
        VerificadorFirmaImpuesto.Verificar(vigente.Vigentes(Ahora), ts, firmaVieja, Cuerpo, Ahora).Should().Be(ResultadoFirmaImpuesto.Valida);
        vencido.Vigentes(Ahora).Should().Equal(AvisosDePrueba.Secreto);
        VerificadorFirmaImpuesto.Verificar(vencido.Vigentes(Ahora), ts, firmaVieja, Cuerpo, Ahora).Should().Be(ResultadoFirmaImpuesto.FirmaInvalida);
        SecretoWebhookImpuesto.SinConfigurar.Vigentes(Ahora).Should().BeEmpty();
    }

    [Theory]
    [InlineData(-301, "FueraDeVentana")]
    [InlineData(301, "FueraDeVentana")]
    [InlineData(-300, "Valida")]
    [InlineData(300, "Valida")]
    public void Timestamp_Tolerancia300Segundos(int desfase, string esperado)
    {
        var ts = AvisosDePrueba.Ts(Ahora.AddSeconds(desfase));
        var firma = AvisosDePrueba.Firmar(AvisosDePrueba.Secreto, ts, Cuerpo);

        VerificadorFirmaImpuesto.Verificar([AvisosDePrueba.Secreto], ts, firma, Cuerpo, Ahora).ToString().Should().Be(esperado);
    }

    [Theory]
    [InlineData(null, "v1=00", "SinCabeceras")]
    [InlineData("1700000000", null, "SinCabeceras")]
    [InlineData("17x", "v1=00", "TimestampInvalido")]
    [InlineData("-5", "v1=00", "TimestampInvalido")]
    public void CabecerasAusentesOMalFormadas(string? ts, string? firma, string esperado) =>
        VerificadorFirmaImpuesto.Verificar([AvisosDePrueba.Secreto], ts, firma, Cuerpo, Ahora).ToString().Should().Be(esperado);

    [Fact]
    public void SinSecretos_NoVerifica()
    {
        var ts = AvisosDePrueba.Ts(Ahora);
        VerificadorFirmaImpuesto.Verificar([], ts, "v1=00", Cuerpo, Ahora).Should().Be(ResultadoFirmaImpuesto.FirmaInvalida);
    }
}
