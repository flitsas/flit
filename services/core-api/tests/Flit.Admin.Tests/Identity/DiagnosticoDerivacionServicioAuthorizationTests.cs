using System.Security.Claims;
using System.Text.Json;
using Flit.Api.Authorization;
using Flit.Api.Identity;
using Flit.Platform.Sdk.Grpc;
using FluentAssertions;
using Grpc.Core;
using Xunit;

namespace Flit.Admin.Tests.Identity;

/// <summary>
/// ADR-0084 (Punto 4, incremento 1) — el rpc de creación de borradores desde Diagnóstico solo lo atiende
/// <c>svc-diagnostico</c> con <c>aud=tramites</c> y el scope <c>platform.tramites.derivacion</c>. Mismo patrón que
/// <see cref="PersonaNaturalServicioAuthorizationTests"/>, sin candado de empresa: Diagnóstico actúa por la empresa del lote.
/// </summary>
/// <remarks>Uso de ejemplo: <c>DiagnosticoDerivacionServicioAuthorization.EsTokenDeDiagnostico(identity)</c>.</remarks>
public sealed class DiagnosticoDerivacionServicioAuthorizationTests
{
    private const string Scope = "platform.tramites.derivacion";

    private static ClaimsIdentity Token(string sub, string aud = "tramites", string scope = Scope, bool authenticated = true) =>
        new(
            [new Claim("sub", sub), new Claim("aud", aud), new Claim("scope", scope)],
            authenticated ? "Bearer" : null);

    [Fact]
    public void Diagnostico_ConSuScopeYAudiencia_Entra() =>
        DiagnosticoDerivacionServicioAuthorization.EsTokenDeDiagnostico(Token("svc-diagnostico")).Should().BeTrue();

    [Fact]
    public void ElScopeVieneEntreVarios_Entra() =>
        DiagnosticoDerivacionServicioAuthorization.EsTokenDeDiagnostico(Token("svc-diagnostico", scope: $"platform.tramites.catalogos {Scope}")).Should().BeTrue();

    [Theory]
    [InlineData("svc-ict", "tramites", Scope, true, "otro cliente de servicio, aunque pida el scope")]
    [InlineData("svc-personanatural", "tramites", Scope, true, "otro cliente de servicio, aunque pida el scope")]
    [InlineData("svc-diagnostico", "tramites", "platform.tramites.catalogos", true, "sin el scope de derivación")]
    [InlineData("svc-diagnostico", "plataforma", Scope, true, "audiencia de otro servicio")]
    [InlineData("11111111-1111-1111-1111-111111111111", "tramites", Scope, true, "token de usuario, no de servicio")]
    [InlineData("svc-diagnostico", "tramites", Scope, false, "sin autenticar")]
    public void CualquierOtroToken_NoEntra(string sub, string aud, string scope, bool autenticado, string motivo) =>
        DiagnosticoDerivacionServicioAuthorization.EsTokenDeDiagnostico(Token(sub, aud, scope, autenticado)).Should().BeFalse(motivo);

    [Fact]
    public void ElLlamanteDiagnostico_ConCualquierEmpresa_PasaElCandado()
    {
        var act = () => DiagnosticoDerivacionServicioAuthorization.VerificarLlamante(new PlatformServiceCaller("svc-diagnostico", Guid.NewGuid()));

        act.Should().NotThrow();
    }

    [Theory]
    [InlineData("svc-ict")]
    [InlineData("svc-personanatural")]
    public void OtroLlamante_RecibePermissionDenied(string cliente)
    {
        var act = () => DiagnosticoDerivacionServicioAuthorization.VerificarLlamante(new PlatformServiceCaller(cliente, Guid.NewGuid()));

        act.Should().Throw<RpcException>().Which.StatusCode.Should().Be(StatusCode.PermissionDenied);
    }

    [Fact]
    public void SinEmpresa_RecibeInvalidArgument()
    {
        var act = () => DiagnosticoDerivacionServicioAuthorization.VerificarLlamante(new PlatformServiceCaller("svc-diagnostico", Guid.Empty));

        act.Should().Throw<RpcException>().Which.StatusCode.Should().Be(StatusCode.InvalidArgument);
    }

    [Fact]
    public void ElScopeNuevoApuntaALaAudienciaDeTramites() =>
        OidcDefaults.ServiceScopeAudiences[Scope].Should().Be(ServiceAudiences.Tramites);

    [Fact]
    public void ElClienteSvcDiagnostico_TieneElScopeDeDerivacionEnLaConfiguracionDeIdentidad()
    {
        // core-identity empaqueta el appsettings.json de Flit.Api (Dockerfile): ahí se declaran los scopes del cliente.
        var raiz = AppContext.BaseDirectory;
        string? ruta = null;
        for (var dir = new DirectoryInfo(raiz); dir is not null && ruta is null; dir = dir.Parent)
        {
            var candidato = Path.Combine(dir.FullName, "src", "Flit.Api", "appsettings.json");
            if (File.Exists(candidato))
                ruta = candidato;
        }

        ruta.Should().NotBeNull("el appsettings.json de Flit.Api debe localizarse subiendo desde la salida de las pruebas");
        using var json = JsonDocument.Parse(File.ReadAllText(ruta!), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        var scopes = json.RootElement.GetProperty("Suite").GetProperty("Oidc").GetProperty("ServiceClients")
            .GetProperty("svc-diagnostico").GetProperty("Scopes").EnumerateArray().Select(s => s.GetString()).ToList();

        scopes.Should().Contain(Scope);
    }

    [Fact]
    public void LaDerivacionVieneApagadaPorDefecto()
    {
        var raiz = AppContext.BaseDirectory;
        string? ruta = null;
        for (var dir = new DirectoryInfo(raiz); dir is not null && ruta is null; dir = dir.Parent)
        {
            var candidato = Path.Combine(dir.FullName, "src", "Flit.Api", "appsettings.json");
            if (File.Exists(candidato))
                ruta = candidato;
        }

        using var json = JsonDocument.Parse(File.ReadAllText(ruta!), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        var derivacion = json.RootElement.GetProperty("Tramites").GetProperty("Derivacion");

        derivacion.GetProperty("Habilitada").GetBoolean().Should().BeFalse();
        derivacion.GetProperty("ConcurrenciaMaxima").GetInt32().Should().Be(8);
    }
}
