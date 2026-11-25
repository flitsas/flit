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
    [InlineData("platform.identidad.empresas", "plataforma")]
    [InlineData("platform.identidad.credenciales.impuesto", "plataforma")]
    [InlineData("platform.consultas", "consultas")]
    [InlineData("platform.notificaciones.send", "notificaciones")]
    [InlineData("platform.tramites.ict", "tramites")]
    [InlineData("platform.tramites.catalogos", "tramites")]
    [InlineData("platform.tramites.persona_natural", "tramites")]
    [InlineData("platform.tramites.derivacion", "tramites")]
    [InlineData("platform.usuarios.diagnostico", "tramites")]
    [InlineData("platform.usuarios.diagnostico.conteos", "tramites")]
    [InlineData("platform.ia.diagnostico", "tramites")]
    [InlineData("platform.personanatural.leer", "personanatural")]
    [InlineData("platform.personanatural.caja", "personanatural")]
    // HU #14694 (ADR-0093): scopes de svc-impuestos. Los tres primeros los atiende core-identity; los de usuarios, core-api.
    [InlineData("platform.identidad.empresas.alta", "plataforma")]
    [InlineData("platform.identidad.credenciales.impuesto.copia", "plataforma")]
    [InlineData("platform.identidad.credenciales.impuesto.actualizar", "plataforma")]
    // HU #14711: secreto del webhook del impuesto para svc-consultas (lo atiende core-identity).
    [InlineData("platform.identidad.credenciales.impuesto.webhook", "plataforma")]
    [InlineData("platform.aprovisionamiento.reportar", "plataforma")]
    [InlineData("platform.usuarios.impuestos", "tramites")]
    [InlineData("platform.usuarios.impuestos.conteos", "tramites")]
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
            ["platform.manifest", "platform.me.read", "platform.identidad.read", "platform.identidad.empresas", "platform.identidad.credenciales.impuesto", "platform.consultas", "platform.consultas.admin", "platform.notificaciones.send", "platform.notificaciones.admin", "platform.tramites.ict", "platform.tramites.catalogos", "platform.tramites.persona_natural", "platform.tramites.derivacion", "platform.usuarios.diagnostico", "platform.usuarios.diagnostico.conteos", "platform.ia.diagnostico", "platform.personanatural.leer", "platform.personanatural.caja", "platform.identidad.empresas.alta", "platform.identidad.credenciales.impuesto.copia", "platform.identidad.credenciales.impuesto.actualizar", "platform.aprovisionamiento.reportar", "platform.usuarios.impuestos", "platform.usuarios.impuestos.conteos", "platform.identidad.credenciales.impuesto.webhook"]);

    /// <summary>
    /// HU #14694 (plan §5.3): el cliente de servicio svc-impuestos de core-api tiene exactamente sus scopes. Lo lee del
    /// appsettings.json de Flit.Api, el mismo que usa core-identity.
    /// </summary>
    [Fact]
    public void SvcImpuestos_TieneExactamenteLosScopesDelPlan()
    {
        var archivo = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        using var json = System.Text.Json.JsonDocument.Parse(File.ReadAllText(archivo));
        var cliente = json.RootElement.GetProperty("Suite").GetProperty("Oidc").GetProperty("ServiceClients");

        cliente.TryGetProperty("svc-impuestos", out var svc).Should().BeTrue("svc-impuestos debe estar en Suite:Oidc:ServiceClients");
        svc.GetProperty("Secret").GetString().Should().BeEmpty("el secreto llega por variable de entorno, nunca en el repo");
        svc.GetProperty("Scopes").EnumerateArray().Select(s => s.GetString()).Should().BeEquivalentTo(
            ["platform.manifest", "platform.identidad.read", "platform.identidad.empresas", "platform.identidad.empresas.alta",
             "platform.identidad.credenciales.impuesto.copia", "platform.identidad.credenciales.impuesto.actualizar", "platform.aprovisionamiento.reportar", "platform.consultas",
             "platform.tramites.catalogos", "platform.usuarios.impuestos", "platform.usuarios.impuestos.conteos"]);
        svc.GetProperty("Scopes").EnumerateArray().Select(s => s.GetString())
            .Should().OnlyContain(s => OidcDefaults.ServiceScopes.Contains(s!), "todo scope del cliente debe existir en el mapa de audiencias");
    }

    /// <summary>HU #14711 (plan §3.2): svc-consultas lee la llave y, con su scope propio, el secreto del webhook del impuesto.</summary>
    [Fact]
    public void SvcConsultas_TieneLosScopesDeLlaveYSecretoDelWebhook()
    {
        var archivo = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        using var json = System.Text.Json.JsonDocument.Parse(File.ReadAllText(archivo));
        var svc = json.RootElement.GetProperty("Suite").GetProperty("Oidc").GetProperty("ServiceClients").GetProperty("svc-consultas");

        svc.GetProperty("Secret").GetString().Should().BeEmpty("el secreto llega por variable de entorno, nunca en el repo");
        svc.GetProperty("Scopes").EnumerateArray().Select(s => s.GetString()).Should().BeEquivalentTo(
            ["platform.identidad.read", "platform.identidad.credenciales.impuesto", "platform.identidad.credenciales.impuesto.webhook"]);
        svc.GetProperty("Scopes").EnumerateArray().Select(s => s.GetString())
            .Should().OnlyContain(s => OidcDefaults.ServiceScopes.Contains(s!), "todo scope del cliente debe existir en el mapa de audiencias");
    }

    /// <summary>HU #14694 (F6-CF6): Impuestos se muestra como «Próximamente» hasta su liberación en cada ambiente.</summary>
    [Fact]
    public void Impuestos_EstaEnComingSoon()
    {
        var archivo = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        using var json = System.Text.Json.JsonDocument.Parse(File.ReadAllText(archivo));
        json.RootElement.GetProperty("Suite").GetProperty("Hosts").GetProperty("ComingSoon").EnumerateArray()
            .Select(s => s.GetString()).Should().Contain("impuestos");
    }
}
