using System.Security.Claims;
using Flit.Api.Authorization;
using Flit.Api.Identity;
using Flit.Platform.Sdk;
using Flit.Platform.Sdk.Grpc;
using FluentAssertions;
using Grpc.Core;
using Xunit;

namespace Flit.Admin.Tests.Identity;

/// <summary>
/// HU #14033 (Feature #14022, Épica #14021, ADR-0075) — la fachada del portal de persona natural solo la atiende
/// <c>svc-personanatural</c>, y su token vale para UNA empresa: la de Persona Natural. Mismo patrón que
/// <c>CatalogosGrpcTests</c> (HU #13459), pero con el candado de empresa que ICT no tiene.
/// </summary>
public sealed class PersonaNaturalServicioAuthorizationTests
{
    private const string Scope = "platform.tramites.persona_natural";

    private static ClaimsIdentity Token(string sub, string aud = "tramites", string scope = Scope, bool authenticated = true) =>
        new(
            [new Claim("sub", sub), new Claim("aud", aud), new Claim("scope", scope)],
            authenticated ? "Bearer" : null);

    [Fact]
    public void ElClienteDelPortal_ConSuScopeYAudiencia_Entra() =>
        PersonaNaturalServicioAuthorization.EsTokenDelPortal(Token("svc-personanatural")).Should().BeTrue();

    [Fact]
    public void ElScopeVieneEntreVarios_Entra() =>
        PersonaNaturalServicioAuthorization.EsTokenDelPortal(Token("svc-personanatural", scope: $"platform.consultas {Scope}")).Should().BeTrue();

    [Theory]
    [InlineData("svc-ict", "tramites", Scope, true, "otro cliente de servicio, aunque pida el scope")]
    [InlineData("svc-comparendos", "tramites", Scope, true, "otro cliente de servicio, aunque pida el scope")]
    [InlineData("svc-personanatural", "tramites", "platform.tramites.catalogos", true, "sin su scope")]
    [InlineData("svc-personanatural", "plataforma", Scope, true, "audiencia de otro servicio")]
    [InlineData("11111111-1111-1111-1111-111111111111", "tramites", Scope, true, "token de usuario, no de servicio")]
    [InlineData("svc-personanatural", "tramites", Scope, false, "sin autenticar")]
    public void CualquierOtroToken_NoEntra(string sub, string aud, string scope, bool autenticado, string motivo) =>
        PersonaNaturalServicioAuthorization.EsTokenDelPortal(Token(sub, aud, scope, autenticado)).Should().BeFalse(motivo);

    [Fact]
    public void ConLaEmpresaDePersonaNatural_ElCandadoDejaPasar()
    {
        var caller = new PlatformServiceCaller("svc-personanatural", PlatformTenants.PersonaNatural);

        var act = () => PersonaNaturalServicioAuthorization.VerificarEmpresa(caller);

        act.Should().NotThrow();
    }

    [Theory]
    [MemberData(nameof(OtrasEmpresas))]
    public void ConOtraEmpresa_ElCandadoRechazaConInvalidTenant(Guid empresa)
    {
        var caller = new PlatformServiceCaller("svc-personanatural", empresa);

        var act = () => PersonaNaturalServicioAuthorization.VerificarEmpresa(caller);

        var error = act.Should().Throw<RpcException>().Which;
        error.StatusCode.Should().Be(StatusCode.InvalidArgument);
        PlatformRpcErrors.Reason(error).Should().Be("invalid_tenant");
    }

    [Fact]
    public void ConOtroLlamante_ElCandadoRechazaAunqueLaEmpresaSeaLaCorrecta()
    {
        var caller = new PlatformServiceCaller("svc-ict", PlatformTenants.PersonaNatural);

        var act = () => PersonaNaturalServicioAuthorization.VerificarEmpresa(caller);

        act.Should().Throw<RpcException>().Which.StatusCode.Should().Be(StatusCode.PermissionDenied);
    }

    [Fact]
    public void ElScopeNuevoApuntaALaAudienciaDeTramites() =>
        OidcDefaults.ServiceScopeAudiences[Scope].Should().Be(ServiceAudiences.Tramites);

    [Fact]
    public void LaEmpresaDePersonaNatural_EsDistintaDeLaPlataformaYTieneIdFijo()
    {
        PlatformTenants.PersonaNatural.Should().Be(new Guid("00000000-0000-0000-0000-0000000f1171"));
        PlatformTenants.PersonaNatural.Should().NotBe(PlatformTenants.Plataforma);
    }

    public static TheoryData<Guid> OtrasEmpresas() => new()
    {
        Guid.Empty,
        PlatformTenants.Plataforma,
        Guid.NewGuid(),
    };
}
