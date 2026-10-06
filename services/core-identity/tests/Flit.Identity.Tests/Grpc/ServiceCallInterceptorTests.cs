using System.Security.Claims;
using Flit.Identity.Api.Grpc;
using FluentAssertions;
using Grpc.Core;
using Xunit;

namespace Flit.Identity.Tests.Grpc;

/// <summary>
/// HU #13334 (Epic #13316) — validación local de una llamada gRPC entre servicios (contrato v1.3 §3 y §6.1), hasta que
/// el SDK traiga su interceptor (HU #13337). La firma, el emisor y la vigencia los valida antes ASP.NET Core.
/// </summary>
public sealed class ServiceCallInterceptorTests
{
    private const string Scope = "platform.identidad.read";
    private static readonly string Tenant = Guid.NewGuid().ToString();
    private readonly ServiceCallInterceptor _interceptor = new(Scope, "plataforma");

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

    [Fact]
    public void PageToken_IdaYVuelta_YUnoInventadoEsInvalidArgument()
    {
        IdentidadGrpcService.PageToken.Decode(IdentidadGrpcService.PageToken.Encode(150)).Should().Be(150);
        IdentidadGrpcService.PageToken.Decode(string.Empty).Should().Be(0);
        CodeOf(() => IdentidadGrpcService.PageToken.Decode("basura")).Should().Be(StatusCode.InvalidArgument);
        CodeOf(() => IdentidadGrpcService.PageToken.Decode(Convert.ToBase64String("v1:-3"u8.ToArray()))).Should().Be(StatusCode.InvalidArgument);
    }
}
