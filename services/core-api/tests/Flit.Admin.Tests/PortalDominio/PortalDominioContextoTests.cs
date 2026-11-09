using Flit.Admin.Application.Companies.Domains;
using Flit.Api.Authorization;
using Flit.Api.Middleware;
using Flit.Modules.Security.Application.Auth;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using NSubstitute;
using Xunit;

namespace Flit.Admin.Tests.PortalDominio;

/// <summary>HU #14383 (Feature #14380) — el host de un portal se resuelve a <c>Portal</c>, no abre el login de FLIT y no acota ninguna red.</summary>
public sealed class PortalDominioContextoTests
{
    private static readonly Guid Organismo = Guid.Parse("07000000-0000-4000-8000-000000014383");
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly DomainOptions Opciones = new() { Reserved = ["*.flitsas.online"] };

    [Fact]
    public async Task AC1_UnHostDePortalActivoSeResuelveAPortalSinRedNiCabeza()
    {
        var resolver = Substitute.For<ITenantDomainResolver>();
        resolver.ResolveAsync("tramites.miorganismo.gov.co", Arg.Any<CancellationToken>()).Returns(NetworkResolution.Portal(Organismo));

        var ctx = await DomainContextMiddleware.ResolveAsync("Tramites.MiOrganismo.gov.co", resolver, Opciones, Ct);

        ctx.Kind.Should().Be(DomainKind.Portal);
        ctx.TransitOfficeId.Should().Be(Organismo);
        ctx.HeadTenantId.Should().BeNull("no acota el acceso de ninguna red");
        ctx.Host.Should().Be("tramites.miorganismo.gov.co");
    }

    [Fact]
    public async Task AC4_UnHostNoActivoOReservadoSigueSiendoFlit()
    {
        var resolver = Substitute.For<ITenantDomainResolver>();
        resolver.ResolveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(NetworkResolution.None);

        (await DomainContextMiddleware.ResolveAsync("pendiente.miorganismo.gov.co", resolver, Opciones, Ct)).Kind.Should().Be(DomainKind.Flit);
        (await DomainContextMiddleware.ResolveAsync("dev.flitsas.online", resolver, Opciones, Ct)).Kind.Should().Be(DomainKind.Flit);
        (await DomainContextMiddleware.ResolveAsync(null, resolver, Opciones, Ct)).Kind.Should().Be(DomainKind.Flit);
    }

    [Fact]
    public async Task UnaRedDeMarcaBlancaSeResuelveIgualQueAntes()
    {
        var cabeza = Guid.NewGuid();
        var resolver = Substitute.For<ITenantDomainResolver>();
        resolver.ResolveAsync("app.cliente.com", Arg.Any<CancellationToken>()).Returns(NetworkResolution.Head(cabeza));

        var ctx = await DomainContextMiddleware.ResolveAsync("app.cliente.com", resolver, Opciones, Ct);

        ctx.Should().Be(DomainContext.Network("app.cliente.com", cabeza));
        ctx.TransitOfficeId.Should().BeNull();
    }

    [Theory]
    [InlineData("/api/v1/auth/login")]
    [InlineData("/api/v1/auth/forgot-password")]
    [InlineData("/connect/token")]
    [InlineData("/.well-known/openid-configuration")]
    [InlineData("/auth/token")]
    [InlineData("/api/v1/platform/issuers")]
    [InlineData("/api/v1/public/portal/abc")]
    [InlineData("/api/v1/integrations/ot")]
    [InlineData("/api/v1/persona-naturalx")]
    public async Task AC3_PorUnDominioDePortalElLoginNoSeAbre(string ruta)
    {
        var (contexto, llamado) = Peticion(ruta, DomainContext.Portal("tramites.miorganismo.gov.co", Organismo));

        await new PortalDomainGuardMiddleware(c => { llamado[0] = true; return Task.CompletedTask; }).InvokeAsync(contexto);

        llamado[0].Should().BeFalse();
        contexto.Response.StatusCode.Should().Be(404);
        Cuerpo(contexto).Should().Contain("PORTAL_SIN_LOGIN");
    }

    [Theory]
    [InlineData("/api/v1/persona-natural/punto/marca/OT01", true)]
    [InlineData("/api/v1/auth/login", false)]
    public async Task OtrasRutasDelPortalYElLoginDeFlitOdeUnaRedSiguenIgual(string ruta, bool portal)
    {
        var dominio = portal
            ? DomainContext.Portal("tramites.miorganismo.gov.co", Organismo)
            : DomainContext.Network("app.cliente.com", Guid.NewGuid());
        var (contexto, llamado) = Peticion(ruta, dominio);

        await new PortalDomainGuardMiddleware(c => { llamado[0] = true; return Task.CompletedTask; }).InvokeAsync(contexto);

        llamado[0].Should().BeTrue();
        contexto.Response.StatusCode.Should().Be(200);
    }

    [Fact]
    public async Task ElLoginDelDominioDeFlitNoCambia()
    {
        var (contexto, llamado) = Peticion("/api/v1/auth/login", DomainContext.Flit("dev.flitsas.online"));

        await new PortalDomainGuardMiddleware(c => { llamado[0] = true; return Task.CompletedTask; }).InvokeAsync(contexto);

        llamado[0].Should().BeTrue();
    }

    private static (DefaultHttpContext Contexto, bool[] Llamado) Peticion(string ruta, DomainContext dominio)
    {
        var contexto = new DefaultHttpContext();
        contexto.Request.Path = ruta;
        contexto.Response.Body = new MemoryStream();
        contexto.Items[DomainContextMiddleware.ItemKey] = dominio;
        return (contexto, new bool[1]);
    }

    private static string Cuerpo(DefaultHttpContext contexto)
    {
        contexto.Response.Body.Position = 0;
        return new StreamReader(contexto.Response.Body).ReadToEnd();
    }
}
