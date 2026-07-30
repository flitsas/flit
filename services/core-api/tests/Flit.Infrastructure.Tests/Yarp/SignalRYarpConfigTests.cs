using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Flit.Infrastructure.Tests.Yarp;

/// <summary>
/// Contrato YARP SignalR WebSockets + SessionAffinity (HU #11104 / ADR-0039).
/// No referencia Flit.Gateway.dll (conflicto de tipo Program con Flit.Api en este test project).
/// Carpeta Yarp/ (no Gateway/) porque .gitignore ignora el patrón "gateway".
/// </summary>
public sealed class SignalRYarpConfigTests
{
    private const string ClusterId = "core-api-signalr-cluster";
    private const string AffinityCookie = ".Flit.SignalR.Affinity";
    private const string FailurePolicy = "Redistribute";

    private static string FindGatewayFile(string fileName)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "services", "core-api", "src", "Flit.Gateway", fileName);
            if (File.Exists(candidate)) return candidate;
            candidate = Path.Combine(dir.FullName, "src", "Flit.Gateway", fileName);
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }

        var fallback = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "src", "Flit.Gateway", fileName));
        Assert.True(File.Exists(fallback), $"No se encontró Flit.Gateway/{fileName} (tried {fallback})");
        return fallback;
    }

    [Fact]
    public void Appsettings_routes_hubs_to_signalr_cluster()
    {
        var config = new ConfigurationBuilder()
            .AddJsonFile(FindGatewayFile("appsettings.json"), optional: false)
            .Build();

        Assert.Equal(ClusterId, config["ReverseProxy:Routes:signalr-route:ClusterId"]);
        Assert.Equal("/hubs/{**catch-all}", config["ReverseProxy:Routes:signalr-route:Match:Path"]);
    }

    [Fact]
    public void Appsettings_signalr_cluster_has_affinity_cookie_and_redistribute()
    {
        var config = new ConfigurationBuilder()
            .AddJsonFile(FindGatewayFile("appsettings.json"), optional: false)
            .Build();

        var prefix = $"ReverseProxy:Clusters:{ClusterId}:SessionAffinity";
        Assert.Equal("True", config[$"{prefix}:Enabled"], ignoreCase: true);
        Assert.Equal("Cookie", config[$"{prefix}:Policy"]);
        Assert.Equal(AffinityCookie, config[$"{prefix}:AffinityKeyName"]);
        Assert.Equal(FailurePolicy, config[$"{prefix}:FailurePolicy"]);
        Assert.Equal("00:05:00", config[$"ReverseProxy:Clusters:{ClusterId}:HttpRequest:ActivityTimeout"]);
    }

    [Fact]
    public void Appsettings_does_not_use_legacy_yarp_affinity_cookie_name()
    {
        var json = File.ReadAllText(FindGatewayFile("appsettings.json"));
        Assert.DoesNotContain(".Yarp.Affinity.SignalR", json, StringComparison.Ordinal);
        Assert.Contains(AffinityCookie, json, StringComparison.Ordinal);
        Assert.Contains("\"FailurePolicy\": \"Redistribute\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Program_cs_enables_websockets_before_reverse_proxy()
    {
        var source = File.ReadAllText(FindGatewayFile("Program.cs"));
        var ws = source.IndexOf("UseWebSockets()", StringComparison.Ordinal);
        var proxy = source.IndexOf("MapReverseProxy()", StringComparison.Ordinal);
        Assert.True(ws >= 0, "Falta app.UseWebSockets() (AC3)");
        Assert.True(proxy >= 0, "Falta app.MapReverseProxy()");
        Assert.True(ws < proxy, "UseWebSockets() debe ir ANTES de MapReverseProxy()");
    }

    [Fact]
    public void Signalr_cluster_json_shape_is_valid()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(FindGatewayFile("appsettings.json")));
        var cluster = doc.RootElement
            .GetProperty("ReverseProxy")
            .GetProperty("Clusters")
            .GetProperty(ClusterId);
        Assert.True(cluster.GetProperty("SessionAffinity").GetProperty("Enabled").GetBoolean());
        Assert.Equal(
            AffinityCookie,
            cluster.GetProperty("SessionAffinity").GetProperty("AffinityKeyName").GetString());
        Assert.Equal(
            FailurePolicy,
            cluster.GetProperty("SessionAffinity").GetProperty("FailurePolicy").GetString());
    }

    [Fact]
    public void Qa_and_dev_examples_override_signalr_cluster_destination()
    {
        var qa = File.ReadAllText(FindGatewayFile("appsettings.QA.json"));
        var dev = File.ReadAllText(FindGatewayFile("appsettings.Development.json.example"));
        Assert.Contains(ClusterId, qa, StringComparison.Ordinal);
        Assert.Contains(ClusterId, dev, StringComparison.Ordinal);
    }
}
