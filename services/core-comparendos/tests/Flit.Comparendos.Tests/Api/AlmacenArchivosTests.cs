using System.Net;
using System.Text;
using System.Text.Json;
using Flit.Comparendos.Api.Infraestructura.Archivos;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Flit.Comparendos.Tests.Api;

/// <summary>
/// Dónde quedan los archivos: en el file-manager, categoría «comparendos». Un .env sin URL no puede dejar los documentos
/// en la base sin que nadie lo note: el servicio no arranca salvo con la bandera de las pruebas.
/// </summary>
public sealed class AlmacenArchivosTests
{
    private static IConfiguration Config(params (string Clave, string Valor)[] valores) =>
        new ConfigurationBuilder().AddInMemoryCollection(valores.ToDictionary(v => v.Clave, v => (string?)v.Valor)).Build();

    [Fact]
    public void SinUrlNiBandera_NoArranca()
    {
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("FILE_MANAGER_BASE_URL")))
            return; // la variable de la máquina cuenta como URL
        var accion = () => new ServiceCollection().AddAlmacenArchivos(Config());
        accion.Should().Throw<InvalidOperationException>().WithMessage("*FILE_MANAGER_BASE_URL*");
    }

    [Fact]
    public void ConBandera_GuardaEnLaBase()
    {
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("FILE_MANAGER_BASE_URL")))
            return;
        var services = new ServiceCollection().AddAlmacenArchivos(Config((OpcionesAlmacen.GuardarEnBase, "true")));
        services.Single(d => d.ServiceType == typeof(IAlmacenArchivos)).ImplementationType.Should().Be<AlmacenEnBase>();
    }

    [Fact]
    public void ConUrl_UsaElFileManager_YLeeLoViejoDeLaBase()
    {
        var services = new ServiceCollection().AddAlmacenArchivos(Config(("FileManager:BaseUrl", "http://file-manager.test")));
        services.Single(d => d.ServiceType == typeof(IAlmacenArchivos)).ImplementationType.Should().Be<AlmacenFileManagerConRespaldo>();
    }

    private sealed class FileManagerFalso : HttpMessageHandler
    {
        public List<(HttpMethod Metodo, string Url, string? Cuerpo)> Pedidos { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Pedidos.Add((request.Method, request.RequestUri!.ToString(), request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken)));
            return request.RequestUri!.Host == "file-manager.test"
                ? new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"id\":\"f-1\",\"presignedUrl\":{\"url\":\"http://s3.test/sube\",\"method\":\"PUT\"}}", Encoding.UTF8, "application/json"),
                }
                : new HttpResponseMessage(HttpStatusCode.OK);
        }
    }

    [Fact]
    public async Task Guardar_PideLaCategoriaComparendos_YSubeConLaUrlFirmada()
    {
        var falso = new FileManagerFalso();
        var almacen = new FileManagerAlmacen(new HttpClient(falso) { BaseAddress = new Uri("http://file-manager.test/") }, Options.Create(new FileManagerOptions()));
        var r = await almacen.GuardarAsync(Guid.Parse("11111111-1111-1111-1111-111111111111"), "ocr", "a.pdf", "application/pdf", [1, 2, 3], CancellationToken.None);

        r.Referencia.Should().Be("f-1");
        var creacion = JsonDocument.Parse(falso.Pedidos[0].Cuerpo!).RootElement;
        creacion.GetProperty("category").GetString().Should().Be("comparendos");
        creacion.GetProperty("metadata").GetProperty("empresaId").GetString().Should().Be("11111111-1111-1111-1111-111111111111");
        (falso.Pedidos[1].Metodo, falso.Pedidos[1].Url).Should().Be((HttpMethod.Put, "http://s3.test/sube"));
    }
}
