using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace Flit.DataMigration.Tests.Api;

/// <summary>
/// HU #12417 AC4/AC6 — el sello de dominio y el CORS dinámico (<c>DomainSealTransform</c>,
/// <c>DynamicCorsOriginSource</c>) no tocan la configuración de <c>ict-host-route</c>: el
/// enrutamiento por dominio del portal de organismos de tránsito sigue exactamente igual. Mismo
/// patrón que <c>GatewayRutaMigracionTests</c>/<c>GatewayRutaPublicaTests</c> — lee
/// <c>appsettings.json</c> tal cual lo carga YARP.
/// </summary>
public sealed class GatewayIctHostRouteTests
{
    private static readonly string[] HostsEsperados =
    [
        "ict.flitsas.com", "ict.dev.flitsas.com", "ict.qa.flitsas.com", "ict.localhost",
    ];

    private static JsonDocument LeerConfigGateway()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Flit.slnx")))
        {
            dir = dir.Parent;
        }

        dir.Should().NotBeNull("las pruebas deben correr dentro del árbol de core-api (Flit.slnx)");
        var ruta = Path.Combine(dir!.FullName, "src", "Flit.Gateway", "appsettings.json");
        File.Exists(ruta).Should().BeTrue($"no se encontró {ruta}");

        return JsonDocument.Parse(
            File.ReadAllText(ruta),
            new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
    }

    [Fact]
    public void IctHostRoute_SigueIgualTrasElSelloDeDominioYElCorsDinamico()
    {
        using var config = LeerConfigGateway();
        var rutas = config.RootElement.GetProperty("ReverseProxy").GetProperty("Routes");

        var ruta = rutas.GetProperty("ict-host-route");
        ruta.GetProperty("ClusterId").GetString().Should().Be("core-ict-cluster");
        ruta.GetProperty("Order").GetInt32().Should().Be(-100);
        ruta.GetProperty("Match").GetProperty("Path").GetString().Should().Be("/{**catch-all}");

        var hosts = ruta.GetProperty("Match").GetProperty("Hosts")
            .EnumerateArray().Select(h => h.GetString()).ToArray();
        hosts.Should().BeEquivalentTo(HostsEsperados,
            "HU #12417 AC4/AC6 — el enrutamiento del portal de organismos de tránsito no cambia");

        // No hay AuthorizationPolicy propia distinta de la que ya tenía (ninguna — la ruta ict no
        // exige JwtRequired hoy); el sello de dominio se aplica igual (AddTransforms es global, no
        // por ruta) sin que esta ruta necesite una entrada propia.
        ruta.TryGetProperty("AuthorizationPolicy", out _).Should().BeFalse();
    }
}
