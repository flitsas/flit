using System.Net;
using System.Net.Sockets;
using System.Text;
using Flit.Consultas.Api.Comparendos;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Flit.Consultas.Tests;

/// <summary>
/// SMARTCLOUD_PROXY_URL: SmartCloud sale por un proxy propio (flit-egress en PDN) sin tocar a los demás proveedores.
/// Sin base de datos.
/// </summary>
public sealed class SmartCloudProxyTests
{
    private static readonly Uri Simit = new("https://www3.simit.org.co/ServiciosSimit/EstadoCuentaPlaca");

    private static SmartCloudOpciones Opciones(string? proxy) => SmartCloudOpciones.Desde(new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> { ["SMARTCLOUD_PROXY_URL"] = proxy })
        .Build());

    [Fact]
    public void SinProxy_NoUsaNingunProxy_NiElDelEntorno()
    {
        var o = Opciones(null);

        o.Proxy.Should().BeNull();
        var h = o.CrearManejador().Should().BeOfType<SocketsHttpHandler>().Subject;
        h.UseProxy.Should().BeFalse("sin SMARTCLOUD_PROXY_URL no debe heredar un HTTPS_PROXY del proceso");
    }

    [Fact]
    public void ConProxy_SeparaLasCredencialesDeLaUrl()
    {
        var h = (SocketsHttpHandler)Opciones("http://svc:t%40k3n@10.0.0.9:19080").CrearManejador();

        h.UseProxy.Should().BeTrue();
        h.Proxy!.GetProxy(Simit).Should().Be(new Uri("http://10.0.0.9:19080/"), "la clave no viaja en la URL del proxy");
        var cred = h.Proxy.Credentials!.GetCredential(new Uri("http://10.0.0.9:19080/"), "Basic")!;
        cred.UserName.Should().Be("svc");
        cred.Password.Should().Be("t@k3n", "la parte de usuario de la URL va codificada");
    }

    [Fact]
    public async Task ConProxy_ElTunelVaAlSimit_ConLasCredencialesTrasEl407()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var puerto = ((IPEndPoint)listener.LocalEndpoint).Port;
        var vistos = new List<string>();
        var servidor = AtenderComoProxy(listener, vistos);

        using var client = new HttpClient(Opciones($"http://svc:tok@127.0.0.1:{puerto}").CrearManejador()) { Timeout = TimeSpan.FromSeconds(10) };
        var act = () => client.GetAsync(Simit);

        await act.Should().ThrowAsync<HttpRequestException>("el proxy de prueba corta el túnel con 502 tras ver las credenciales");
        await servidor;
        vistos.Should().HaveCount(2, "primero sin credenciales (407 Basic), luego con ellas");
        vistos[0].Should().StartWith("CONNECT www3.simit.org.co:443 ").And.NotContain("Proxy-Authorization");
        vistos[1].Should().StartWith("CONNECT www3.simit.org.co:443 ")
            .And.Contain("Proxy-Authorization: Basic " + Convert.ToBase64String(Encoding.ASCII.GetBytes("svc:tok")));
    }

    /// <summary>Proxy mínimo: 407 Basic a la primera petición, 502 a la que trae credenciales (como flit-egress sin destino).</summary>
    private static async Task AtenderComoProxy(TcpListener listener, List<string> vistos)
    {
        while (vistos.Count < 2)
        {
            using var socket = await listener.AcceptTcpClientAsync();
            var stream = socket.GetStream();
            while (vistos.Count < 2)
            {
                var peticion = await LeerCabeceras(stream);
                if (peticion is null)
                    break;
                vistos.Add(peticion);
                var respuesta = peticion.Contains("Proxy-Authorization", StringComparison.Ordinal)
                    ? "HTTP/1.1 502 Bad Gateway\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"
                    : "HTTP/1.1 407 Proxy Authentication Required\r\nProxy-Authenticate: Basic realm=\"flit-egress\"\r\nContent-Length: 0\r\n\r\n";
                await stream.WriteAsync(Encoding.ASCII.GetBytes(respuesta));
            }
        }
    }

    private static async Task<string?> LeerCabeceras(NetworkStream stream)
    {
        var sb = new StringBuilder();
        var buffer = new byte[1];
        while (!sb.ToString().EndsWith("\r\n\r\n", StringComparison.Ordinal))
        {
            if (await stream.ReadAsync(buffer) == 0)
                return null;
            sb.Append((char)buffer[0]);
        }

        return sb.ToString();
    }
}
