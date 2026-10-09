using Flit.Api.Identity;
using FluentAssertions;
using Xunit;

namespace Flit.Identity.Tests;

/// <summary>
/// HU #13333 (Epic #13316, contrato v1.3 §3): el <c>aud</c> de un token de servicio es el servicio que atiende sus scopes.
/// </summary>
public sealed class ServiceTokenAudienceTests
{
    [Theory]
    [InlineData("platform.manifest", "plataforma")]
    [InlineData("platform.me.read", "plataforma")]
    [InlineData("platform.identidad.read", "plataforma")]
    [InlineData("platform.consultas", "consultas")]
    [InlineData("platform.notificaciones.send", "notificaciones")]
    [InlineData("platform.tramites.ict", "tramites")]
    public void CadaScopeVaASuServicio(string scope, string audiencia) =>
        OidcDefaults.AudiencesForServiceScopes([scope]).Should().Equal(audiencia);

    [Fact]
    public void VariosScopesLlevanTodasSusAudienciasSinRepetir() =>
        OidcDefaults.AudiencesForServiceScopes(["platform.consultas", "platform.identidad.read", "platform.manifest"])
            .Should().Equal("consultas", "plataforma");

    [Fact]
    public void SinScopesDeServicioQuedaPlataformaComoAntes() =>
        OidcDefaults.AudiencesForServiceScopes(["openid"]).Should().Equal("plataforma");

    [Fact]
    public void LosScopesRegistradosSonExactamenteLosDelMapa() =>
        OidcDefaults.ServiceScopes.Should().BeEquivalentTo(
            ["platform.manifest", "platform.me.read", "platform.identidad.read", "platform.consultas", "platform.consultas.admin", "platform.notificaciones.send", "platform.notificaciones.admin", "platform.tramites.ict"]);
}
