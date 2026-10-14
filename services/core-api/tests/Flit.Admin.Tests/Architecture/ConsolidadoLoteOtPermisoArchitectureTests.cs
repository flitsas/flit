using Flit.Api.Authorization;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Flit.Admin.Tests.Architecture;

/// <summary>
/// Épica #13216 (HU #13391, AC10) — el permiso <c>consolidado-masivo.download</c> protege SOLO la ruta nueva de
/// <c>AdminOtEndpoints</c> (<c>POST /api/v1/admin/ot/consolidados/lotes</c>), en AND con la
/// <c>OtModulePolicy</c> del grupo. Las rutas individuales del consolidado OT conservan sus policies. Recorre las
/// rutas REGISTRADAS por el host real (<see cref="EndpointDataSource"/>) y falla nombrando la ruta infractora.
/// <para>Uso de ejemplo: no se invoca; corre en CI.</para>
/// </summary>
public sealed class ConsolidadoLoteOtPermisoArchitectureTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string Prefijo = "/api/v1/admin/ot";
    private const string RutaLote = "/api/v1/admin/ot/consolidados/lotes";
    private const string Slug = "consolidado-masivo.download";

    private readonly WebApplicationFactory<Program> _factory;

    public ConsolidadoLoteOtPermisoArchitectureTests(WebApplicationFactory<Program> factory) => _factory = factory;

    [Fact]
    public void AC10_SoloLaRutaDelLoteOt_TieneElPermisoDeDescargaMasiva()
    {
        var rutasOt = RutasOt();
        rutasOt.Should().HaveCountGreaterThan(30, "el barrido debe ver las rutas de AdminOtEndpoints (no en verde por vacuidad)");

        var conPermiso = rutasOt
            .Where(e => Requisitos(e).OfType<PermissionRequirement>().Any(r => r.Slug == Slug))
            .Select(Firma)
            .ToList();

        conPermiso.Should().Equal($"POST {RutaLote}");
    }

    [Fact]
    public void AC10_LaRutaDelLote_ExigeElPermisoEnAndConOtModulePolicy()
    {
        var lote = RutasOt().Single(e => Firma(e) == $"POST {RutaLote}");

        Requisitos(lote).OfType<PermissionRequirement>().Select(r => r.Slug).Should().Equal(Slug);
        Policies(lote).Should().Contain(AdminAuthorization.OtModulePolicy, "el permiso se suma a la policy del grupo, no la sustituye");
    }

    [Theory]
    [InlineData("POST", "/api/v1/admin/ot/client-procedures/{id:guid}/consolidado-maestro")]
    [InlineData("GET", "/api/v1/admin/ot/client-procedures/{id:guid}/consolidado/entrega")]
    public void AC10_LasRutasIndividualesDelConsolidadoOt_ConservanSoloOtModulePolicy(string verbo, string patron)
    {
        var ruta = RutasOt().Single(e => Firma(e) == $"{verbo} {patron}");

        Policies(ruta).Should().Equal(AdminAuthorization.OtModulePolicy);
        Requisitos(ruta).OfType<PermissionRequirement>().Should().BeEmpty();
    }

    private List<RouteEndpoint> RutasOt() =>
        _factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText?.StartsWith(Prefijo, StringComparison.Ordinal) == true)
            .ToList();

    private static string Firma(RouteEndpoint e) =>
        $"{string.Join(",", e.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods ?? [])} {e.RoutePattern.RawText}";

    private static IEnumerable<IAuthorizationRequirement> Requisitos(RouteEndpoint e) =>
        e.Metadata.GetOrderedMetadata<AuthorizationPolicy>().SelectMany(p => p.Requirements);

    private static List<string> Policies(RouteEndpoint e) =>
        e.Metadata.GetOrderedMetadata<IAuthorizeData>()
            .Select(a => a.Policy)
            .OfType<string>()
            .Distinct(StringComparer.Ordinal)
            .ToList();
}
