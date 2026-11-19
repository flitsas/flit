using System.Security.Claims;
using System.Text.Json;
using Flit.Api.Authorization;
using Flit.Api.Identity;
using FluentAssertions;
using Xunit;

namespace Flit.Admin.Tests.Identity;

/// <summary>
/// ADR-0091 · SP-15 — <c>ConfiguracionIaService</c> solo lo atiende <c>svc-diagnostico</c> con <c>aud=tramites</c> y el scope
/// <c>platform.ia.diagnostico</c>; dentro, cada cliente solo puede pedir sus herramientas (mapa en código).
/// </summary>
/// <remarks>Uso de ejemplo: <c>DiagnosticoIaServicioAuthorization.EsTokenAutorizado(identity)</c>.</remarks>
public sealed class DiagnosticoIaServicioAuthorizationTests
{
    private const string Scope = "platform.ia.diagnostico";

    private static ClaimsIdentity Token(string sub, string aud = "tramites", string scope = Scope, bool authenticated = true) =>
        new([new Claim("sub", sub), new Claim("aud", aud), new Claim("scope", scope)], authenticated ? "Bearer" : null);

    [Fact]
    public void Diagnostico_ConScopeYAudiencia_Entra()
    {
        DiagnosticoIaServicioAuthorization.EsTokenAutorizado(Token("svc-diagnostico")).Should().BeTrue();
        DiagnosticoIaServicioAuthorization.EsTokenAutorizado(Token("svc-diagnostico", scope: $"platform.manifest {Scope}")).Should().BeTrue();
    }

    [Theory]
    [InlineData("svc-ict", "tramites", Scope, true, "otro svc-* aunque traiga el scope")]
    [InlineData("svc-diagnostico", "tramites", "platform.usuarios.diagnostico", true, "svc-diagnostico sin el scope de IA")]
    [InlineData("svc-diagnostico", "plataforma", Scope, true, "audiencia de otro servicio")]
    [InlineData("11111111-1111-1111-1111-111111111111", "tramites", Scope, true, "token humano (SuperAdmin) con aud=tramites")]
    [InlineData("svc-diagnostico", "tramites", Scope, false, "sin autenticar")]
    public void CualquierOtroToken_NoEntra(string sub, string aud, string scope, bool autenticado, string motivo) =>
        DiagnosticoIaServicioAuthorization.EsTokenAutorizado(Token(sub, aud, scope, autenticado)).Should().BeFalse(motivo);

    [Fact]
    public void SvcDiagnostico_SoloPuedePedirDiagnosticoDictamen_ConAudienciaDiagnostico()
    {
        var cliente = DiagnosticoIaServicioAuthorization.ClienteDe("svc-diagnostico");

        cliente.Should().NotBeNull();
        cliente!.Audiencia.Should().Be("diagnostico");
        cliente.Herramientas.Should().BeEquivalentTo(["diagnostico_dictamen"]);
    }

    [Theory]
    [InlineData("svc-ict")]
    [InlineData("svc-tramites")]
    [InlineData("")]
    [InlineData(null)]
    public void OtroCliente_NoTieneHerramientas(string? cliente) =>
        DiagnosticoIaServicioAuthorization.ClienteDe(cliente).Should().BeNull();

    [Fact]
    public void ElScopeApuntaALaAudienciaDeCoreApi() =>
        OidcDefaults.ServiceScopeAudiences[Scope].Should().Be(ServiceAudiences.Tramites);

    [Fact]
    public void SoloSvcDiagnostico_TieneElScopeEnLaConfiguracionDeIdentidad()
    {
        string? ruta = null;
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null && ruta is null; dir = dir.Parent)
        {
            var candidato = Path.Combine(dir.FullName, "src", "Flit.Api", "appsettings.json");
            if (File.Exists(candidato))
                ruta = candidato;
        }

        ruta.Should().NotBeNull();
        using var json = JsonDocument.Parse(File.ReadAllText(ruta!), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        var conScope = json.RootElement.GetProperty("Suite").GetProperty("Oidc").GetProperty("ServiceClients").EnumerateObject()
            .Where(c => c.Value.TryGetProperty("Scopes", out var s) && s.EnumerateArray().Any(x => x.GetString() == Scope))
            .Select(c => c.Name).ToList();

        conScope.Should().Equal(["svc-diagnostico"]);
    }
}
