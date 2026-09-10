using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace Flit.DataMigration.Tests.Api;

/// <summary>
/// Fija que <c>/api/v1/public/*</c> nunca vuelva a caer en el catch-all del gateway
/// (<c>core-api-route</c>, <c>AuthorizationPolicy: JwtRequired</c>). Sin una ruta propia, el
/// gateway rechazaba con 401 peticiones que no pueden llevar un JWT — p. ej. un <c>&lt;img src&gt;</c>
/// del banner promocional — antes de llegar al backend, donde el endpoint sí es
/// <c>AllowAnonymous()</c>. El bug no se notaba en local porque el proxy de <c>next.config.ts</c>
/// pega directo a <c>core-api</c>, sin pasar por el gateway. Mismo alcance que
/// <c>TenantEnforcementMiddleware</c>, que ya excluye <c>/api/v1/public/*</c> por diseño.
/// </summary>
public sealed class GatewayRutaPublicaTests
{
    private const string RutaEsperada = "/api/v1/public/{**catch-all}";

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
    public void LaRutaPublicaApuntaAlClusterDeCoreApiSinAuth()
    {
        using var config = LeerConfigGateway();
        var rutas = config.RootElement.GetProperty("ReverseProxy").GetProperty("Routes");

        var ruta = rutas.GetProperty("public-route");
        ruta.GetProperty("ClusterId").GetString().Should().Be("core-api-cluster");
        ruta.GetProperty("Match").GetProperty("Path").GetString().Should().Be(RutaEsperada);
        ruta.TryGetProperty("AuthorizationPolicy", out _).Should().BeFalse(
            "/api/v1/public/* es anonimo por diseno: si se le exige JwtRequired, cualquier <img src> "
            + "u otro consumo sin Authorization header vuelve a recibir 401 del gateway");
        ruta.TryGetProperty("Transforms", out _).Should().BeFalse(
            "el host sirve /api/v1/public tal cual: una transformacion de ruta lo dejaria en 404");
    }
}
