using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace Flit.DataMigration.Tests.Api;

/// <summary>
/// HU #13078 (Feature #13066, Épica #12737) — fija la ruta del Gateway para la API de clientes de
/// integración externos (<c>/api/v1/external/*</c>, creada en la HU #13087). El pase externo no es de la
/// plataforma (emisor, audiencia y llave propios): si la ruta cayera en el catch-all
/// <c>core-api-route</c> (<c>JwtRequired</c>), el Gateway respondería 401 a Flito antes de llegar a
/// core-api, que es quien lo valida con el esquema <c>ExternalClient</c>.
/// </summary>
public sealed class GatewayRutaExternaTests
{
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
    public void AC1_ElPrefijoExternoLlegaACoreApiSinExigirElPaseDeLaPlataforma()
    {
        using var config = LeerConfigGateway();
        var ruta = config.RootElement.GetProperty("ReverseProxy").GetProperty("Routes").GetProperty("external-route");

        ruta.GetProperty("ClusterId").GetString().Should().Be("core-api-cluster");
        ruta.GetProperty("Match").GetProperty("Path").GetString().Should().Be("/api/v1/external/{**catch-all}");
        ruta.TryGetProperty("AuthorizationPolicy", out _).Should().BeFalse(
            "el pase externo lo valida core-api; JwtRequired del Gateway lo rechazaría con 401");
        ruta.TryGetProperty("Transforms", out _).Should().BeFalse(
            "core-api sirve /api/v1/external tal cual: una transformación de ruta lo dejaría en 404");
        ruta.GetProperty("Match").TryGetProperty("Hosts", out _).Should().BeFalse(
            "la ruta debe responder en el host de la API de cada ambiente (DEV, QA, PDN)");
    }

    [Fact]
    public void AC1_ElTiempoDeEsperaDelClusterAlcanzaParaUnaPaginaCompleta()
    {
        using var config = LeerConfigGateway();
        var cluster = config.RootElement.GetProperty("ReverseProxy").GetProperty("Clusters").GetProperty("core-api-cluster");

        var espera = TimeSpan.Parse(
            cluster.GetProperty("HttpRequest").GetProperty("ActivityTimeout").GetString()!,
            System.Globalization.CultureInfo.InvariantCulture);

        espera.Should().BeGreaterThanOrEqualTo(TimeSpan.FromSeconds(30),
            "una página de 1000 trámites con su ítem completo es una sola consulta; 30 s es el tope acordado");
    }

    [Fact]
    public void AC2_LaRutaExternaNoAbreOtrosPrefijos()
    {
        using var config = LeerConfigGateway();
        var rutas = config.RootElement.GetProperty("ReverseProxy").GetProperty("Routes");

        rutas.GetProperty("core-api-route").GetProperty("AuthorizationPolicy").GetString().Should().Be("JwtRequired",
            "el resto de /api sigue exigiendo el pase de la plataforma");
        rutas.GetProperty("core-api-route").GetProperty("Match").GetProperty("Path").GetString().Should().Be("/api/{**catch-all}");

        var sinPase = rutas.EnumerateObject()
            .Where(r => !r.Value.TryGetProperty("AuthorizationPolicy", out _))
            .Where(r => !r.Value.GetProperty("Match").TryGetProperty("Hosts", out _))
            .Select(r => r.Value.GetProperty("Match").GetProperty("Path").GetString())
            .ToList();

        sinPase.Should().Contain("/api/v1/external/{**catch-all}");
        sinPase.Should().NotContain(["/api/{**catch-all}", "/{**catch-all}", "/api/v1/{**catch-all}"],
            "ninguna ruta sin pase puede ser un comodín que abra la API entera");
        sinPase.Where(p => p!.StartsWith("/api/v1/external", StringComparison.Ordinal)).Should().ContainSingle(
            "el prefijo externo tiene una sola ruta, específica");
    }
}
