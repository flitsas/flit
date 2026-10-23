using System.Security.Claims;
using Flit.Admin.Application.Companies.Domains;
using Flit.Api.Authorization;
using Flit.Modules.Security.Application.Auth;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using NSubstitute;
using Xunit;

namespace Flit.Admin.Tests.Middleware;

/// <summary>
/// HU #12422 AC5 (ADR-0060 D3) — <see cref="DomainBindingMiddleware"/> sin levantar el pipeline
/// HTTP completo (mismo patrón que <see cref="DomainContextTests"/>). Uso de ejemplo:
/// <code>
/// var context = NewHttpContext(authenticated: true, domClaim: "app.red.com");
/// await new DomainBindingMiddleware(next).InvokeAsync(context, domainContextAccessor, resolver);
/// </code>
/// </summary>
public sealed class DomainBindingMiddlewareTests
{
    private static HttpContext NewHttpContext(bool authenticated, string? domClaim)
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        if (authenticated)
        {
            var claims = new List<Claim>();
            if (domClaim is not null)
                claims.Add(new Claim("dom", domClaim));

            context.User = new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType: "TestAuth"));
        }
        else
        {
            context.User = new ClaimsPrincipal(new ClaimsIdentity());
        }

        return context;
    }

    private static readonly Guid RedA = Guid.NewGuid();
    private static readonly Guid RedB = Guid.NewGuid();

    /// <summary>Dominios activos: red A tiene su hub y el de Trámites (B-08); red B, solo su hub.</summary>
    private static readonly Dictionary<string, (Guid Head, string Product)> Domains = new()
    {
        ["app.red-a.com"] = (RedA, "plataforma"),
        ["tramites.red-a.com"] = (RedA, "tramites"),
        ["app.red-b.com"] = (RedB, "plataforma"),
    };

    private static ITenantDomainResolver Resolver()
    {
        var resolver = Substitute.For<ITenantDomainResolver>();
        resolver.ResolveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(call =>
            Domains.TryGetValue(call.Arg<string>(), out var d) ? NetworkResolution.Head(d.Head, d.Product) : NetworkResolution.None);
        return resolver;
    }

    private static IDomainContextAccessor NewDomainContext(DomainKind kind, string? host)
    {
        var accessor = Substitute.For<IDomainContextAccessor>();
        accessor.Kind.Returns(kind);
        accessor.Host.Returns(host);
        accessor.HeadTenantId.Returns(host is not null && Domains.TryGetValue(host, out var d) ? d.Head : (Guid?)null);
        return accessor;
    }

    [Fact]
    public async Task SinClaimDom_BajoDominioFlit_PasaComoFlit()
    {
        var nextCalled = false;
        var middleware = new DomainBindingMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });
        var context = NewHttpContext(authenticated: true, domClaim: null);

        await middleware.InvokeAsync(context, NewDomainContext(DomainKind.Flit, null), Resolver());

        nextCalled.Should().BeTrue();
        context.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
    }

    [Fact]
    public async Task SinClaimDom_BajoDominioDeRed_Rechaza()
    {
        // Token emitido antes de esta HU (sin "dom") se trata como "flit" — bajo el dominio de una
        // red NO coincide (paridad: siguen valiendo solo bajo FLIT).
        var nextCalled = false;
        var middleware = new DomainBindingMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });
        var context = NewHttpContext(authenticated: true, domClaim: null);

        await middleware.InvokeAsync(context, NewDomainContext(DomainKind.Network, "app.red-a.com"), Resolver());

        nextCalled.Should().BeFalse();
        context.Response.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
    }

    [Fact]
    public async Task DomIgualAlSelloDeLaRed_Pasa()
    {
        var nextCalled = false;
        var middleware = new DomainBindingMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });
        var context = NewHttpContext(authenticated: true, domClaim: "app.red-a.com");

        await middleware.InvokeAsync(context, NewDomainContext(DomainKind.Network, "app.red-a.com"), Resolver());

        nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task DomDeUnaRedDistinta_Rechaza()
    {
        var nextCalled = false;
        var middleware = new DomainBindingMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });
        var context = NewHttpContext(authenticated: true, domClaim: "app.red-a.com");

        await middleware.InvokeAsync(context, NewDomainContext(DomainKind.Network, "app.red-b.com"), Resolver());

        nextCalled.Should().BeFalse();
        context.Response.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
    }

    [Fact]
    public async Task DomFlit_BajoDominioDeUnaRed_Rechaza()
    {
        var nextCalled = false;
        var middleware = new DomainBindingMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });
        var context = NewHttpContext(authenticated: true, domClaim: "flit");

        await middleware.InvokeAsync(context, NewDomainContext(DomainKind.Network, "app.red-a.com"), Resolver());

        nextCalled.Should().BeFalse();
    }

    [Fact]
    public async Task DomDeUnaRed_BajoDominioFlit_Rechaza()
    {
        var nextCalled = false;
        var middleware = new DomainBindingMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });
        var context = NewHttpContext(authenticated: true, domClaim: "app.red-a.com");

        await middleware.InvokeAsync(context, NewDomainContext(DomainKind.Flit, null), Resolver());

        nextCalled.Should().BeFalse();
    }

    [Fact]
    public async Task RutaAnonima_NoSeVeAfectadaPorElDesajusteDeDominio()
    {
        var nextCalled = false;
        var middleware = new DomainBindingMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });
        var context = NewHttpContext(authenticated: false, domClaim: null);

        await middleware.InvokeAsync(context, NewDomainContext(DomainKind.Network, "app.red-a.com"), Resolver());

        nextCalled.Should().BeTrue();
        context.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
    }

    [Fact]
    public async Task DomDelHubDeLaRed_EnElDominioDeUnProductoDeLaMismaRed_Pasa()
    {
        // HU #12993 (A-08): la sesión OIDC se abre en el hub de la red y se usa en el dominio de Trámites de esa red.
        var nextCalled = false;
        var middleware = new DomainBindingMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });
        var context = NewHttpContext(authenticated: true, domClaim: "app.red-a.com");

        await middleware.InvokeAsync(context, NewDomainContext(DomainKind.Network, "tramites.red-a.com"), Resolver());

        nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task DomDeUnDominioYaInactivo_EnOtroDominioDeLaRed_Rechaza()
    {
        var nextCalled = false;
        var middleware = new DomainBindingMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });
        var context = NewHttpContext(authenticated: true, domClaim: "viejo.red-a.com");

        await middleware.InvokeAsync(context, NewDomainContext(DomainKind.Network, "tramites.red-a.com"), Resolver());

        nextCalled.Should().BeFalse();
        context.Response.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
    }
}
