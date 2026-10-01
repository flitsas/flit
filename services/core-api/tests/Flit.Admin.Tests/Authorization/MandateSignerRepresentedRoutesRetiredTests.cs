using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Flit.Admin.Tests.Authorization;

/// <summary>
/// HU #13179 (AC6) — las rutas <c>represented-companies</c> de MANDATARIOS (compañía y submódulo de hijas) dejan de
/// existir; la de escrituras (<c>deeds</c>) sigue registrada. Se inspecciona la tabla de rutas, sin tocar datos.
/// <para>Uso de ejemplo: ninguna ruta termina en <c>mandate-signers/represented-companies</c>.</para>
/// </summary>
public sealed class MandateSignerRepresentedRoutesRetiredTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public MandateSignerRepresentedRoutesRetiredTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    private IReadOnlyList<string> Routes() =>
        [.. _factory.Services.GetServices<EndpointDataSource>()
            .SelectMany(d => d.Endpoints)
            .OfType<RouteEndpoint>()
            .Select(e => e.RoutePattern.RawText ?? string.Empty)];

    [Fact]
    public void AC6_LasRutasRepresentedCompaniesDeMandatarios_NoExisten()
    {
        Routes().Should().NotContain(r => r.Contains("mandate-signers/represented-companies", StringComparison.Ordinal));
    }

    [Fact]
    public void AC6_LaRutaDeEscrituras_SigueRegistrada()
    {
        Routes().Should().Contain(r => r.Contains("/companies/{tenantId:guid}/deeds", StringComparison.Ordinal));
    }

    [Fact]
    public void AC6_LasRutasNuevasDeCompaniasAsociables_EstanRegistradas()
    {
        Routes().Should().Contain(r => r.EndsWith("mandate-signers/associable-companies", StringComparison.Ordinal));
    }
}
