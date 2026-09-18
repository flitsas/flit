using System.Security.Claims;
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
/// await new DomainBindingMiddleware(next).InvokeAsync(context, domainContextAccessor);
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

    private static IDomainContextAccessor NewDomainContext(DomainKind kind, string? host)
    {
        var accessor = Substitute.For<IDomainContextAccessor>();
        accessor.Kind.Returns(kind);
        accessor.Host.Returns(host);
        return accessor;
    }

    [Fact]
    public async Task SinClaimDom_BajoDominioFlit_PasaComoFlit()
    {
        var nextCalled = false;
        var middleware = new DomainBindingMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });
        var context = NewHttpContext(authenticated: true, domClaim: null);

        await middleware.InvokeAsync(context, NewDomainContext(DomainKind.Flit, null));

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

        await middleware.InvokeAsync(context, NewDomainContext(DomainKind.Network, "app.red-a.com"));

        nextCalled.Should().BeFalse();
        context.Response.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
    }

    [Fact]
    public async Task DomIgualAlSelloDeLaRed_Pasa()
    {
        var nextCalled = false;
        var middleware = new DomainBindingMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });
        var context = NewHttpContext(authenticated: true, domClaim: "app.red-a.com");

        await middleware.InvokeAsync(context, NewDomainContext(DomainKind.Network, "app.red-a.com"));

        nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task DomDeUnaRedDistinta_Rechaza()
    {
        var nextCalled = false;
        var middleware = new DomainBindingMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });
        var context = NewHttpContext(authenticated: true, domClaim: "app.red-a.com");

        await middleware.InvokeAsync(context, NewDomainContext(DomainKind.Network, "app.red-b.com"));

        nextCalled.Should().BeFalse();
        context.Response.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
    }

    [Fact]
    public async Task DomFlit_BajoDominioDeUnaRed_Rechaza()
    {
        var nextCalled = false;
        var middleware = new DomainBindingMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });
        var context = NewHttpContext(authenticated: true, domClaim: "flit");

        await middleware.InvokeAsync(context, NewDomainContext(DomainKind.Network, "app.red-a.com"));

        nextCalled.Should().BeFalse();
    }

    [Fact]
    public async Task DomDeUnaRed_BajoDominioFlit_Rechaza()
    {
        var nextCalled = false;
        var middleware = new DomainBindingMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });
        var context = NewHttpContext(authenticated: true, domClaim: "app.red-a.com");

        await middleware.InvokeAsync(context, NewDomainContext(DomainKind.Flit, null));

        nextCalled.Should().BeFalse();
    }

    [Fact]
    public async Task RutaAnonima_NoSeVeAfectadaPorElDesajusteDeDominio()
    {
        var nextCalled = false;
        var middleware = new DomainBindingMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });
        var context = NewHttpContext(authenticated: false, domClaim: null);

        await middleware.InvokeAsync(context, NewDomainContext(DomainKind.Network, "app.red-a.com"));

        nextCalled.Should().BeTrue();
        context.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
    }
}
