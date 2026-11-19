using System.Security.Claims;
using System.Text.Json;
using Flit.Api.Authorization;
using Flit.Api.Identity;
using Flit.Api.Usuarios;
using Flit.Platform.Sdk.Grpc;
using FluentAssertions;
using Grpc.Core;
using Xunit;

namespace Flit.Admin.Tests.Identity;

/// <summary>
/// ADR-0089 regla 1-2 / SP-14 — los rpc de usuarios de Diagnóstico solo los atiende <c>svc-diagnostico</c> con <c>aud=tramites</c>
/// y su scope dedicado; dentro, el producto queda atado al cliente, el actor es obligatorio y se reverifica en la base
/// (<see cref="ReglaActorUsuariosProducto"/>). Mismo patrón que <see cref="DiagnosticoDerivacionServicioAuthorizationTests"/>.
/// </summary>
/// <remarks>Uso de ejemplo: <c>DiagnosticoUsuariosServicioAuthorization.EsTokenDeDiagnostico(identity, Scope)</c>.</remarks>
public sealed class DiagnosticoUsuariosServicioAuthorizationTests
{
    private const string Scope = "platform.usuarios.diagnostico";
    private const string ScopeConteos = "platform.usuarios.diagnostico.conteos";
    private static readonly Guid Actor = Guid.Parse("00000000-0000-4000-8000-000000000001");

    private static ClaimsIdentity Token(string sub, string aud = "tramites", string scope = Scope, bool authenticated = true) =>
        new([new Claim("sub", sub), new Claim("aud", aud), new Claim("scope", scope)], authenticated ? "Bearer" : null);

    private static Metadata ConActor(string? actor = null) => actor is null ? [] : new Metadata { { "x-flit-actor-id", actor } };

    [Fact]
    public void Diagnostico_ConCadaScopeYAudiencia_Entra()
    {
        DiagnosticoUsuariosServicioAuthorization.EsTokenDeDiagnostico(Token("svc-diagnostico"), Scope).Should().BeTrue();
        DiagnosticoUsuariosServicioAuthorization.EsTokenDeDiagnostico(Token("svc-diagnostico", scope: $"platform.tramites.derivacion {ScopeConteos}"), ScopeConteos).Should().BeTrue();
    }

    [Theory]
    [InlineData("svc-ict", "tramites", Scope, true, "otro cliente de servicio, aunque pida el scope")]
    [InlineData("svc-comparendos", "tramites", Scope, true, "otro producto, aunque pida el scope")]
    [InlineData("svc-diagnostico", "tramites", "platform.tramites.derivacion", true, "sin el scope de usuarios")]
    [InlineData("svc-diagnostico", "tramites", ScopeConteos, true, "el scope de conteos no abre las operaciones por empresa")]
    [InlineData("svc-diagnostico", "plataforma", Scope, true, "audiencia de otro servicio")]
    [InlineData("11111111-1111-1111-1111-111111111111", "tramites", Scope, true, "token de usuario, no de servicio")]
    [InlineData("svc-diagnostico", "tramites", Scope, false, "sin autenticar")]
    public void CualquierOtroToken_NoEntra(string sub, string aud, string scope, bool autenticado, string motivo) =>
        DiagnosticoUsuariosServicioAuthorization.EsTokenDeDiagnostico(Token(sub, aud, scope, autenticado), Scope).Should().BeFalse(motivo);

    [Fact]
    public void LlamanteDiagnostico_ConProductoYActor_DevuelveElActor() =>
        DiagnosticoUsuariosServicioAuthorization.VerificarLlamante(new PlatformServiceCaller("svc-diagnostico", Guid.NewGuid()), "diagnostico", ConActor(Actor.ToString()))
            .Should().Be(Actor);

    [Theory]
    [InlineData("svc-ict", "diagnostico", "00000000-0000-4000-8000-000000000001", "otro cliente")]
    [InlineData("svc-diagnostico", "tramites", "00000000-0000-4000-8000-000000000001", "producto ajeno al cliente")]
    [InlineData("svc-diagnostico", "", "00000000-0000-4000-8000-000000000001", "sin producto")]
    [InlineData("svc-diagnostico", "diagnostico", null, "sin actor")]
    [InlineData("svc-diagnostico", "diagnostico", "no-es-uuid", "actor inválido")]
    [InlineData("svc-diagnostico", "diagnostico", "00000000-0000-0000-0000-000000000000", "actor vacío")]
    public void LlamanteOProductoOActorInvalido_RecibePermissionDenied(string cliente, string producto, string? actor, string motivo)
    {
        var act = () => DiagnosticoUsuariosServicioAuthorization.VerificarLlamante(new PlatformServiceCaller(cliente, Guid.NewGuid()), producto, ConActor(actor));

        act.Should().Throw<RpcException>(motivo).Which.StatusCode.Should().Be(StatusCode.PermissionDenied);
    }

    [Fact]
    public void SinEmpresa_RecibeInvalidArgument_SalvoElServicioGlobal()
    {
        var caller = new PlatformServiceCaller("svc-diagnostico", Guid.Empty);

        FluentActions.Invoking(() => DiagnosticoUsuariosServicioAuthorization.VerificarLlamante(caller, "diagnostico", ConActor(Actor.ToString())))
            .Should().Throw<RpcException>().Which.StatusCode.Should().Be(StatusCode.InvalidArgument);
        DiagnosticoUsuariosServicioAuthorization.VerificarLlamante(caller, "diagnostico", ConActor(Actor.ToString()), exigeEmpresa: false).Should().Be(Actor);
    }

    [Fact]
    public void LosScopesNuevosApuntanALaAudienciaDeCoreApi()
    {
        OidcDefaults.ServiceScopeAudiences[Scope].Should().Be(ServiceAudiences.Tramites);
        OidcDefaults.ServiceScopeAudiences[ScopeConteos].Should().Be(ServiceAudiences.Tramites);
    }

    [Fact]
    public void ElClienteSvcDiagnostico_TieneLosDosScopesEnLaConfiguracionDeIdentidad()
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
        var scopes = json.RootElement.GetProperty("Suite").GetProperty("Oidc").GetProperty("ServiceClients")
            .GetProperty("svc-diagnostico").GetProperty("Scopes").EnumerateArray().Select(s => s.GetString()).ToList();

        scopes.Should().Contain([Scope, ScopeConteos]);
    }

    [Fact]
    public void Actor_AdminDelProductoConPermiso_Entra_SinPermiso_NoEntra()
    {
        var conPermiso = new SnapshotActor("active", SuperAdmin: false, AdminProducto: true, Suspendido: false, ["diagnostico.usuarios.gestionar", "diagnostico.tablero.ver"]);
        var sinPermiso = conPermiso with { Permisos = ["diagnostico.tablero.ver"] };

        ReglaActorUsuariosProducto.Decidir(Actor, conPermiso, "diagnostico", global: false).Should().Be(new Flit.Modules.Security.Application.UserRoles.ProductRoles.ProductActor(Actor, false));
        ReglaActorUsuariosProducto.Decidir(Actor, sinPermiso, "diagnostico", global: false).Should().BeNull("H-U5: sin el permiso del manifiesto no entra");
    }

    [Fact]
    public void Actor_Suspendido_Inactivo_SinRolAdmin_OInexistente_NoEntra()
    {
        var baseActor = new SnapshotActor("active", false, true, false, ["diagnostico.usuarios.gestionar"]);

        ReglaActorUsuariosProducto.Decidir(Actor, baseActor with { Suspendido = true }, "diagnostico", false).Should().BeNull();
        ReglaActorUsuariosProducto.Decidir(Actor, baseActor with { Estado = "suspended" }, "diagnostico", false).Should().BeNull();
        ReglaActorUsuariosProducto.Decidir(Actor, baseActor with { AdminProducto = false }, "diagnostico", false).Should().BeNull("usuario_diagnostico no gestiona usuarios");
        ReglaActorUsuariosProducto.Decidir(Actor, null, "diagnostico", false).Should().BeNull();
    }

    [Fact]
    public void Actor_SuperAdmin_EntraPorEmpresaYGlobal_ElAdminSoloPorEmpresa()
    {
        var super = new SnapshotActor("active", SuperAdmin: true, AdminProducto: false, Suspendido: false, []);
        var admin = new SnapshotActor("active", false, true, false, ["diagnostico.usuarios.gestionar"]);

        ReglaActorUsuariosProducto.Decidir(Actor, super, "diagnostico", global: false)!.IsSuperAdmin.Should().BeTrue();
        ReglaActorUsuariosProducto.Decidir(Actor, super, "diagnostico", global: true)!.IsSuperAdmin.Should().BeTrue();
        ReglaActorUsuariosProducto.Decidir(Actor, admin, "diagnostico", global: true).Should().BeNull("los conteos globales son solo del SuperAdmin");
    }
}
