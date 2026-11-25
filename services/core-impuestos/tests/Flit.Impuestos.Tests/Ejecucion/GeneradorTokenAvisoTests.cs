using System.Security.Cryptography;
using System.Text;
using Flit.Impuestos.Api.Modulos.Ejecucion;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Flit.Impuestos.Tests.Ejecucion;

/// <summary>
/// HU #14694 (M09; plan §1.5, DA-4) — token opaco por ítem para el <c>callbackUrl</c>: 32 bytes aleatorios en base64url (43
/// caracteres), en la base solo <c>SHA-256(token)</c>; <c>callbackUrl = {Impuestos:Avisos:UrlBase}/{token}</c>, https y de 2048
/// caracteres como máximo, sin usuario ni fragmento (guía §2.2); http solo en loopback con <c>Impuestos:Avisos:PermitirHttpLocal</c>.
/// Sin la base configurada (o si no cumple) no hay <c>callbackUrl</c>. El token no se imprime.
/// </summary>
/// <example>
/// <code>
/// var token = generador.Generar();          // { Token, Hash }
/// var url = generador.CallbackUrl(token.Token);
/// </code>
/// </example>
public sealed class GeneradorTokenAvisoTests
{
    private static GeneradorTokenAviso Con(string? urlBase) =>
        new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { [GeneradorTokenAviso.ClaveUrlBase] = urlBase }).Build());

    [Fact]
    public void El_token_tiene_43_caracteres_base64url_y_256_bits_distintos_cada_vez()
    {
        var generador = Con(EntornoEjecucion.UrlAvisos);

        var tokens = Enumerable.Range(0, 500).Select(_ => generador.Generar()).ToList();

        tokens.Should().OnlyContain(t => t.Token.Length == 43 && t.Token.All(c => char.IsAsciiLetterOrDigit(c) || c == '-' || c == '_'));
        tokens.Select(t => t.Token).Should().OnlyHaveUniqueItems();
        Convert.FromBase64String(tokens[0].Token.Replace('-', '+').Replace('_', '/') + "=").Should().HaveCount(32);
    }

    [Fact]
    public void El_hash_es_el_SHA256_del_token_y_es_el_mismo_que_calcula_el_receptor()
    {
        var token = Con(EntornoEjecucion.UrlAvisos).Generar();

        token.Hash.Should().Equal(SHA256.HashData(Encoding.UTF8.GetBytes(token.Token)));
        GeneradorTokenAviso.Hash(token.Token).Should().Equal(token.Hash);
    }

    [Fact]
    public void La_url_es_la_base_mas_el_token_sin_doble_barra()
    {
        var generador = Con(EntornoEjecucion.UrlAvisos + "/");
        var token = generador.Generar();

        generador.Configurado.Should().BeTrue();
        generador.CallbackUrl(token.Token).Should().Be(EntornoEjecucion.UrlAvisos + "/" + token.Token);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("http://dev.flit.prueba/avisos")]
    [InlineData("no-es-url")]
    public void Sin_base_https_no_hay_callbackUrl(string? urlBase)
    {
        var generador = Con(urlBase);

        generador.Configurado.Should().BeFalse();
        generador.CallbackUrl(generador.Generar().Token).Should().BeNull();
    }

    [Theory]
    [InlineData("https://u:p@dev.flit.prueba/avisos")]
    [InlineData("https://dev.flit.prueba/avisos#ancla")]
    [InlineData("http://127.0.0.1:4033/avisos")]
    public void Sin_PermitirHttpLocal_rechaza_usuario_fragmento_y_http(string urlBase) =>
        GeneradorTokenAviso.Validar(urlBase, permitirHttpLocal: false).Should().BeNull();

    [Fact]
    public void PermitirHttpLocal_admite_http_solo_en_loopback()
    {
        GeneradorTokenAviso.Validar("http://127.0.0.1:4033/api/v1/impuestos/avisos", permitirHttpLocal: true).Should().Be("http://127.0.0.1:4033/api/v1/impuestos/avisos");
        GeneradorTokenAviso.Validar("http://localhost:4033/avisos/", permitirHttpLocal: true).Should().Be("http://localhost:4033/avisos");
        GeneradorTokenAviso.Validar("http://dev.flit.prueba/avisos", permitirHttpLocal: true).Should().BeNull("fuera de loopback siempre https");
        var generador = new GeneradorTokenAviso(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [GeneradorTokenAviso.ClaveUrlBase] = "http://127.0.0.1:4033/avisos",
            [GeneradorTokenAviso.ClavePermitirHttpLocal] = "true",
        }).Build());
        generador.Configurado.Should().BeTrue();
    }

    [Fact]
    public void Una_url_de_mas_de_2048_caracteres_no_se_usa()
    {
        var generador = Con("https://dev.flit.prueba/" + new string('a', 2048));

        generador.CallbackUrl(generador.Generar().Token).Should().BeNull();
    }

    [Fact]
    public void El_token_no_se_imprime()
    {
        var token = Con(EntornoEjecucion.UrlAvisos).Generar();

        token.ToString().Should().NotContain(token.Token).And.Contain("REDACTADO");
    }
}
