using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace Flit.DataMigration.Tests.Api;

/// <summary>
/// Fija el contrato entre el gateway y <c>migracion-api</c>. Son tres invariantes que, si alguien
/// las rompe "ordenando" la configuración, fallan en producción y no en el build.
/// <list type="number">
/// <item><b>Cluster propio.</b> Si la ruta se mueve a <c>core-api-cluster</c> hereda su
/// <c>ActivityTimeout</c> de 30 s y el gateway devuelve 504 sobre migraciones que SÍ se
/// completaron — invitando a reintentar algo ya hecho. Una migración de instancia 3 (snapshot de
/// PDFs de V1 + subidas de 9-12 MB) tarda bastante más de 30 s.</item>
/// <item><b>Puerto interno 4030.</b> <c>migracion-api</c> no publica puerto en el host: el gateway
/// lo alcanza por la red interna del compose. Si el destino y el <c>ASPNETCORE_URLS</c> del compose
/// dejan de coincidir, el síntoma es un 502 sin más pista.</item>
/// <item><b>Prefijo intacto.</b> El host sirve <c>/api/v1/migracion</c> tal cual; la ruta no lleva
/// transformación. Si se le añadiera un <c>PathPattern</c>, todo daría 404.</item>
/// </list>
/// </summary>
public sealed class GatewayRutaMigracionTests
{
    private const string RutaEsperada = "/api/v1/migracion/{**catch-all}";
    private const string DestinoEsperado = "http://migracion-api:4030/";

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

        // Skip/AllowTrailingCommas es exactamente lo que hace el proveedor de configuración JSON de
        // .NET, así que este test también comprueba que los comentarios del archivo son legibles.
        return JsonDocument.Parse(
            File.ReadAllText(ruta),
            new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
    }

    [Fact]
    public void LaRutaDeMigracionApuntaASuPropioCluster()
    {
        using var config = LeerConfigGateway();
        var proxy = config.RootElement.GetProperty("ReverseProxy");

        var ruta = proxy.GetProperty("Routes").GetProperty("migracion-route");
        ruta.GetProperty("ClusterId").GetString().Should().Be("migracion-cluster");
        ruta.GetProperty("Match").GetProperty("Path").GetString().Should().Be(RutaEsperada);
        ruta.TryGetProperty("Transforms", out _).Should().BeFalse(
            "el host sirve /api/v1/migracion tal cual: una transformación de ruta lo dejaría en 404");
    }

    [Fact]
    public void ElClusterDeMigracionTieneTimeoutHolgadoYPuertoFijo()
    {
        using var config = LeerConfigGateway();
        var clusters = config.RootElement.GetProperty("ReverseProxy").GetProperty("Clusters");

        var destino = clusters.GetProperty("migracion-cluster")
            .GetProperty("Destinations").GetProperty("migracion-api-1")
            .GetProperty("Address").GetString();
        destino.Should().Be(DestinoEsperado);

        var timeout = TimeSpan.Parse(
            clusters.GetProperty("migracion-cluster").GetProperty("HttpRequest")
                .GetProperty("ActivityTimeout").GetString()!,
            System.Globalization.CultureInfo.InvariantCulture);
        timeout.Should().BeGreaterThanOrEqualTo(
            TimeSpan.FromMinutes(10),
            "una migración de instancia 3 puede tardar minutos; con un timeout corto el gateway "
            + "corta con 504 migraciones que sí se completaron");

        var timeoutCoreApi = TimeSpan.Parse(
            clusters.GetProperty("core-api-cluster").GetProperty("HttpRequest")
                .GetProperty("ActivityTimeout").GetString()!,
            System.Globalization.CultureInfo.InvariantCulture);
        timeout.Should().BeGreaterThan(
            timeoutCoreApi,
            "es la razón de existir del cluster aparte: si acaba igualándose a core-api, "
            + "la ruta ya no necesita estar separada y alguien la fusionará");
    }
}
