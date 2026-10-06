using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Flit.Admin.Tests.Authorization;

/// <summary>
/// HU #13158 — limpieza de rutas de mandatos sin consumidor. El inventario se lee del host real
/// (<see cref="EndpointDataSource"/>): si alguien vuelve a mapear la ruta retirada, o retira una de las
/// exclusiones fijas, esta prueba falla.
/// </summary>
public sealed class MandateRoutesCleanupTests : IClassFixture<MandateRoutesCleanupTests.RouteInventoryFactory>
{
    private readonly RouteInventoryFactory _factory;

    public MandateRoutesCleanupTests(RouteInventoryFactory factory) => _factory = factory;

    [Fact]
    public void AC1_ElExtractDeLaConfiguracionDeMandatoYaNoEstaMapeado()
    {
        Routes().Should().NotContain("POST /api/v1/admin/plataforma/mandatos/extract");
    }

    [Fact]
    public void AC2_LasExclusionesFijasSiguenExpuestas()
    {
        var routes = Routes();

        routes.Should().Contain("POST /api/v1/admin/transit-offices/{transitOfficeId:guid}/mandate-signers/",
            "el alta del OT la consume F1");
        routes.Should().Contain(
            "PUT /api/v1/admin/plataforma/mandatos/ot/{officeId:guid}/company-rules/{companyTenantId:guid}",
            "upsertCompanyOtMandateRule lo consume F5");
    }

    [Fact]
    public void AC3_LosCandidatosConConsumidorSeConservan()
    {
        var routes = Routes();

        // OtMandatosSection (F3) consume editar, inactivar y reactivar; MandatoOtConfigForm consume el DELETE
        // de la regla de la compañía y OtMandatosSection el GET de la configuración del OT.
        routes.Should().Contain("PUT /api/v1/admin/transit-offices/{transitOfficeId:guid}/mandate-signers/{mandateSignerId:guid}");
        routes.Should().Contain("POST /api/v1/admin/transit-offices/{transitOfficeId:guid}/mandate-signers/{mandateSignerId:guid}/inactivate");
        routes.Should().Contain("POST /api/v1/admin/transit-offices/{transitOfficeId:guid}/mandate-signers/{mandateSignerId:guid}/reactivate");
        routes.Should().Contain("DELETE /api/v1/admin/plataforma/mandatos/ot/{officeId:guid}/company-rules/{companyTenantId:guid}");
        routes.Should().Contain("DELETE /api/v1/admin/ot/offices/{officeId:guid}/mandatos/company-rules/{companyTenantId:guid}");
        routes.Should().Contain("GET /api/v1/admin/plataforma/mandatos/ot/{officeId:guid}");
    }

    [Fact]
    public void AC5_LosInterruptoresDePlantillaPropiaYEnvioPorCorreoSiguenMapeados()
    {
        var routes = Routes();

        routes.Should().Contain("POST /api/v1/admin/plataforma/mandatos/ot/{officeId:guid}/template");
        routes.Should().Contain("PUT /api/v1/admin/plataforma/mandatos/ot/{officeId:guid}/template/editor");
        routes.Should().Contain("POST /api/v1/admin/plataforma/mandatos/simulador/enviar");
    }

    private List<string> Routes() =>
        _factory.Services.GetServices<EndpointDataSource>()
            .SelectMany(ds => ds.Endpoints)
            .OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText is { } raw
                && raw.StartsWith("/api/v1/admin/", StringComparison.OrdinalIgnoreCase)
                && (raw.Contains("mandate-signers", StringComparison.OrdinalIgnoreCase)
                    || raw.Contains("mandatos", StringComparison.OrdinalIgnoreCase)))
            .SelectMany(e => (e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? [])
                .Select(m => $"{m} {e.RoutePattern.RawText}"))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>Host real sin sustituciones: solo se enumeran las rutas registradas al arrancar.</summary>
    public sealed class RouteInventoryFactory : WebApplicationFactory<Program>;
}
