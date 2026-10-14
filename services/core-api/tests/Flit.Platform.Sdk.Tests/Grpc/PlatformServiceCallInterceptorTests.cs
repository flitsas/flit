using System.Security.Claims;
using Flit.Platform.Sdk.Grpc;
using FluentAssertions;
using Grpc.Core;
using Xunit;

namespace Flit.Platform.Sdk.Tests.Grpc;

/// <summary>
/// HU #13337 (Epic #13316; nació local en core-identity con la HU #13334) — validación de una llamada gRPC entre
/// servicios (contrato v1.3 §3 y §6.1). La firma, el emisor y la vigencia los valida antes ASP.NET Core.
/// </summary>
public sealed class PlatformServiceCallInterceptorTests
{
    private const string Scope = "platform.identidad.read";
    private static readonly string Tenant = Guid.NewGuid().ToString();
    private readonly PlatformServiceCallInterceptor _interceptor = new(Scope, "plataforma");

    private static ClaimsPrincipal Token(string sub, string aud = "plataforma", string scope = Scope) =>
        new(new ClaimsIdentity([new Claim("sub", sub), new Claim("aud", aud), new Claim("scope", scope)], "Bearer"));

    private static StatusCode CodeOf(Action act) => act.Should().Throw<RpcException>().Which.StatusCode;

    [Fact]
    public void TokenDeServicioConScopeYEmpresa_Pasa()
    {
        var caller = _interceptor.Validate(Token("svc-consultas", scope: "platform.manifest platform.identidad.read"), Tenant);

        caller.ClientId.Should().Be("svc-consultas");
        caller.TenantId.Should().Be(Guid.Parse(Tenant));
    }

    [Fact]
    public void SinToken_Unauthenticated() =>
        CodeOf(() => _interceptor.Validate(new ClaimsPrincipal(new ClaimsIdentity()), Tenant)).Should().Be(StatusCode.Unauthenticated);

    [Fact]
    public void TokenDeUsuario_Unauthenticated_AunqueTraigaElScopeYLaEmpresa() =>
        CodeOf(() => _interceptor.Validate(Token(Guid.NewGuid().ToString()), Tenant)).Should().Be(StatusCode.Unauthenticated);

    [Fact]
    public void ClienteQueNoEsDeServicio_Unauthenticated() =>
        CodeOf(() => _interceptor.Validate(Token("frontend-tramites"), Tenant)).Should().Be(StatusCode.Unauthenticated);

    [Fact]
    public void TokenParaOtroServicio_PermissionDenied() =>
        CodeOf(() => _interceptor.Validate(Token("svc-tramites", aud: "consultas"), Tenant)).Should().Be(StatusCode.PermissionDenied);

    [Fact]
    public void SinElScope_PermissionDenied() =>
        CodeOf(() => _interceptor.Validate(Token("svc-demo", scope: "platform.manifest"), Tenant)).Should().Be(StatusCode.PermissionDenied);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("no-es-uuid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public void SinEmpresaValida_InvalidArgument(string? tenant) =>
        CodeOf(() => _interceptor.Validate(Token("svc-consultas"), tenant)).Should().Be(StatusCode.InvalidArgument);
}
