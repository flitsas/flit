using Flit.Api.UseCases.RevocationRequests;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Flit.Admin.Tests.OtClientProcedures;

/// <summary>
/// HU #12580 (Feature #12566) — retiro del disparador libre de revocación del OT.
/// <list type="bullet">
///   <item>AC1 — <c>POST /api/v1/admin/ot/client-procedures/{id}/revoke</c> deja de estar mapeada:
///   de las dos salidas que ofrecía el AC (404/410 o invocación interna) se toma la primera, porque
///   <c>RevokeOtClientProcedureHandler</c> solo se invoca en proceso desde
///   <see cref="DecideRevocationRequestHandler"/> y no necesita superficie HTTP propia.</item>
///   <item>AC2 — la única vía a <c>Revocado</c> queda siendo la decisión sobre una solicitud del
///   gestor: las rutas de aprobar/rechazar de la Feature #12565 siguen registradas. La regresión
///   sobre el resultado de la transición (placa liberada, adjuntos marcados) vive en
///   <c>OtClientProcedureHandlerTests</c> y <c>DecideRevocationRequestHandlerTests</c>; aquí solo se
///   fija la superficie HTTP.</item>
/// </list>
/// El inventario se lee del host real (<see cref="EndpointDataSource"/>), no del código fuente: si
/// alguien volviera a mapear la ruta libre, esta prueba falla.
/// </summary>
public sealed class OtRevocationRouteRetirementTests
    : IClassFixture<OtRevocationRouteRetirementTests.RouteInventoryFactory>
{
    private readonly RouteInventoryFactory _factory;

    public OtRevocationRouteRetirementTests(RouteInventoryFactory factory) => _factory = factory;

    [Fact]
    public void AC1_LaRutaLibreDeRevocacionYaNoEstaMapeada()
    {
        Routes().Should().NotContain(
            "POST /api/v1/admin/ot/client-procedures/{id:guid}/revoke",
            "HU #12580 AC1 — la revocación deja de ser una acción libre del OT; solo se dispara al "
            + "aprobar una solicitud del gestor (Feature #12565)");
    }

    [Fact]
    public void AC2_LasRutasDeDecisionSobreLaSolicitudSiguenSiendoLaUnicaViaARevocado()
    {
        var routes = Routes();

        routes.Should().Contain(
            "POST /api/v1/admin/ot/client-procedures/{id:guid}/revocation-requests/approve",
            "aprobar la solicitud es la ÚNICA vía a Revocado tras el retiro del disparador libre");
        routes.Should().Contain(
            "POST /api/v1/admin/ot/client-procedures/{id:guid}/revocation-requests/reject");
    }

    /// <summary>
    /// Toda ruta de revocación bajo el prefijo OT: sirve de red para variantes futuras
    /// (<c>/revoke/</c>, <c>:revoke</c>, otro verbo) que no coincidan literalmente con el AC1.
    /// </summary>
    [Fact]
    public void AC1_NingunaRutaOtTerminaEnRevoke()
    {
        Routes()
            .Where(r => r.EndsWith("/revoke", StringComparison.OrdinalIgnoreCase))
            .Should().BeEmpty("ninguna acción del OT revoca un trámite sin solicitud previa");
    }

    private List<string> Routes() =>
        _factory.Services.GetServices<EndpointDataSource>()
            .SelectMany(ds => ds.Endpoints)
            .OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText is { } raw
                && raw.StartsWith("/api/v1/admin/ot/", StringComparison.OrdinalIgnoreCase))
            .SelectMany(e => (e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? [])
                .Select(m => $"{m} {e.RoutePattern.RawText}"))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>Host real sin sustituciones: solo se enumeran las rutas registradas al arrancar.</summary>
    public sealed class RouteInventoryFactory : WebApplicationFactory<Program>;
}
