using Flit.Identity.Api;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Flit.Admin.Tests.Identity;

/// <summary>
/// HU #13233 (Epic #13217) — durante la transición, core-api es el respaldo del gateway para el login: toda ruta de
/// core-identity tiene que existir igual en core-api (mismo patrón y mismos métodos). Se retira en el corte (HU #13235).
/// </summary>
public sealed class IdentityRouteParityTests
{
    [Fact]
    public void CadaRutaDeCoreIdentity_ExisteIgualEnCoreApi()
    {
        using var api = OidcServerTests.WithOidc(new WebApplicationFactory<Program>());
        using var identity = new WebApplicationFactory<IdentityApiEntryPoint>().WithWebHostBuilder(b =>
        {
            b.UseSetting("Suite:Oidc:Enabled", "true");
            b.UseSetting("Suite:Hosts:Environment", "dev");
        });

        var identityRoutes = Routes(identity.Services).Where(r => !r.Pattern.StartsWith("/health", StringComparison.Ordinal)).ToList();

        identityRoutes.Should().NotBeEmpty();
        Routes(api.Services).Should().Contain(identityRoutes);
    }

    private static IEnumerable<(string Pattern, string Methods)> Routes(IServiceProvider services) =>
        services.GetServices<EndpointDataSource>()
            .SelectMany(s => s.Endpoints)
            .OfType<RouteEndpoint>()
            .Select(e => (
                Pattern: "/" + (e.RoutePattern.RawText ?? string.Empty).TrimStart('/'),
                Methods: string.Join(",", e.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods.Order() ?? Enumerable.Empty<string>())))
            .Distinct();
}
