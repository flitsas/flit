using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Flit.Infrastructure.Storage;
using Flit.Tramites.Application.Storage;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Flit.Infrastructure.Tests.ConsolidadoLotes;

/// <summary>Serializa las pruebas de subida grande: miden bytes asignados por el proceso y no deben competir.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ConsolidadoLoteParteStorageSerial
{
    public const string Name = "HU13372-subida-streaming";
}

/// <summary>
/// HU #13372 (épica #13216) — AC4: la parte cifrada se sube al file-manager en streaming (<see cref="StreamContent"/>,
/// PUT o POST según el <c>method</c> de ADR-0057 D1), sin <c>IAttachmentStorage.SaveAsync</c>, y la relectura por
/// <c>OpenReadAsync</c> devuelve el mismo tamaño y SHA-256. El file-manager y el storage son un stub HTTP en proceso que
/// guarda el objeto en un archivo temporal.
/// <para>Uso de ejemplo:</para>
/// <code>
/// var storage = new ConsolidadoLoteParteStorage(httpClient, Options.Create(fileManagerOptions));
/// var stored = await storage.SubirAsync(loteId, 1, rutaCifrada, ct);
/// await using var leido = await storage.OpenReadAsync(stored.StoragePath, ct);
/// </code>
/// </summary>
[Collection(ConsolidadoLoteParteStorageSerial.Name)]
public sealed class ConsolidadoLoteParteStorageTests : IDisposable
{
    private const string Base = "https://fm.test/pdn/";
    private const int MiB = 1024 * 1024;
    private static readonly Guid Lote = Guid.Parse("2d6a1c55-7e3b-4f0a-8c19-5b7e4d3a2f10");

    private readonly string _dir = Directory.CreateDirectory(
        Path.Combine(Path.GetTempPath(), "flit-hu13372-" + Guid.NewGuid().ToString("N"))).FullName;

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { /* best-effort */ }
    }

    private static ConsolidadoLoteParteStorage Storage(StubFileManager stub) =>
        new(
            new HttpClient(stub) { BaseAddress = new Uri(Base) },
            Options.Create(new FileManagerOptions { BaseUrl = Base, FilesPath = "api/v1/files", Category = "tramites" }));

    private string ArchivoGrande(int mib)
    {
        var ruta = Path.Combine(_dir, $"parte-{mib}.flz");
        var bloque = new byte[MiB];
        Random.Shared.NextBytes(bloque);
        using var fs = new FileStream(ruta, FileMode.CreateNew, FileAccess.Write, FileShare.None, MiB);
        for (var i = 0; i < mib; i++)
        {
            BitConverter.TryWriteBytes(bloque, i); // ningún MiB igual a otro
            fs.Write(bloque);
        }

        return ruta;
    }

    private static async Task<(long Tamano, string Sha)> MedirAsync(Stream s, CancellationToken ct)
    {
        var sha = Convert.ToHexStringLower(await SHA256.HashDataAsync(s, ct));
        return (s.Position, sha);
    }

    [Theory]
    [InlineData("PUT", 250)]
    [InlineData("POST", 32)]
    public async Task AC4_SubeEnStreamingConStreamContentYLaRelecturaTieneElMismoTamanoYSha256(string metodo, int mib)
    {
        var ct = TestContext.Current.CancellationToken;
        var ruta = ArchivoGrande(mib);
        string shaOrigen;
        await using (var f = File.OpenRead(ruta))
            shaOrigen = Convert.ToHexStringLower(await SHA256.HashDataAsync(f, ct));
        var stub = new StubFileManager(_dir, metodo);
        var storage = Storage(stub);

        var antes = GC.GetTotalAllocatedBytes(precise: true);
        var stored = await storage.SubirAsync(Lote, 2, ruta, ct);
        var asignado = GC.GetTotalAllocatedBytes(precise: true) - antes;

        // Subida: método pedido por el file-manager, StreamContent con Content-Length (sin chunked) y sin bufferizar.
        var subida = stub.Subidas.Should().ContainSingle().Subject;
        subida.Metodo.Should().Be(metodo);
        subida.ContenidoArchivo.Should().Be(nameof(StreamContent));
        subida.ContentLength.Should().Be((long)mib * MiB);
        asignado.Should().BeLessThan(16L * MiB, "el archivo de {0} MiB no se carga en memoria al subir", mib);

        stored.SizeBytes.Should().Be((long)mib * MiB);
        stored.Sha256.Should().Be(shaOrigen);

        // Relectura por OpenReadAsync (FileManagerDownloader): mismo tamaño y sha256.
        await using var leido = await storage.OpenReadAsync(stored.StoragePath, ct);
        leido.Should().NotBeNull();
        var (tamano, sha) = await MedirAsync(leido!, ct);
        tamano.Should().Be((long)mib * MiB);
        sha.Should().Be(shaOrigen);
    }

    [Fact]
    public async Task AC4_Contrato_RegistroSinPiiConLoteParteYShaYMultipartConCamposFirmadosAntesDelFile()
    {
        var ct = TestContext.Current.CancellationToken;
        var ruta = Path.Combine(_dir, "chica.flz");
        await File.WriteAllBytesAsync(ruta, Encoding.ASCII.GetBytes("FLZ1-contenido-cifrado"), ct);
        var stub = new StubFileManager(_dir, metodo: null); // sin method ⇒ POST

        var stored = await Storage(stub).SubirAsync(Lote, 7, ruta, ct);

        stored.StoragePath.Should().Be("parte_1");
        var body = stub.CuerposCreacion.Should().ContainSingle().Subject;
        body.Should().Contain("\"category\":\"tramites\"")
            .And.Contain("\"filename\":\"lote-2d6a1c557e3b4f0a8c195b7e4d3a2f10-parte-7.flz\"")
            .And.Contain("consolidado_lote_parte")
            .And.Contain("2d6a1c55-7e3b-4f0a-8c19-5b7e4d3a2f10")
            .And.Contain("\"partNumber\":\"7\"")
            .And.Contain(stored.Sha256);
        var subida = stub.Subidas.Should().ContainSingle().Subject;
        subida.Metodo.Should().Be("POST");
        subida.NombresMultipart.Should().Equal("key", "policy", "file");
    }

    [Fact]
    public async Task AC4_Borde_ReintentoTrasFalloTransitorio_ReabreElArchivoYSubeCompleto()
    {
        var ct = TestContext.Current.CancellationToken;
        var ruta = ArchivoGrande(3);
        var stub = new StubFileManager(_dir, "PUT") { FallosAntesDeAceptar = 1 };
        var storage = Storage(stub);

        var stored = await storage.SubirAsync(Lote, 1, ruta, ct);

        stub.Subidas.Should().HaveCount(2);
        await using var leido = await storage.OpenReadAsync(stored.StoragePath, ct);
        (await MedirAsync(leido!, ct)).Should().Be((3L * MiB, stored.Sha256));
    }

    [Fact]
    public async Task AC4_Borde_DosFallosSeguidos_LanzaErrorSinFiltrarLaUrlFirmada()
    {
        var ct = TestContext.Current.CancellationToken;
        var ruta = ArchivoGrande(1);
        var stub = new StubFileManager(_dir, "PUT") { FallosAntesDeAceptar = 5 };

        var act = () => Storage(stub).SubirAsync(Lote, 1, ruta, ct);

        var ex = (await act.Should().ThrowAsync<InvalidOperationException>()).Which;
        ex.Message.Should().Contain("PUT").And.NotContain("s3.test");
        stub.Subidas.Should().HaveCount(2);
    }

    [Fact]
    public async Task AC4_Contrato_NoImplementaIAttachmentStorageYOpenReadDeObjetoInexistenteEsNull()
    {
        typeof(ConsolidadoLoteParteStorage).GetInterfaces().Should().NotContain(typeof(IAttachmentStorage));

        var leido = await Storage(new StubFileManager(_dir, "PUT")).OpenReadAsync("no_existe", TestContext.Current.CancellationToken);

        leido.Should().BeNull();
    }

    [Theory]
    [InlineData(1, 31)]
    [InlineData(250, 280)]
    public void AC4_Contrato_ElTiempoDeSubidaCreceUnSegundoPorMiB(int mib, int segundos)
    {
        var storage = Storage(new StubFileManager(_dir, "PUT"));

        storage.TiempoSubida((long)mib * MiB).Should().Be(TimeSpan.FromSeconds(segundos));
    }

    /// <summary>
    /// HU #13379 (M2/L1) — la lectura de la parte para la descarga va en streaming de punta a punta: una parte de
    /// 250 MiB no se carga entera en memoria (antes <c>FileManagerDownloader.OpenReadAsync</c> la bajaba a un
    /// <see cref="MemoryStream"/>), y el contenido leído es idéntico.
    /// </summary>
    [Fact]
    public async Task HU13379_M2_OpenReadAsync_LeeLaParteEnStreaming_SinCargarlaEnMemoria()
    {
        var ct = TestContext.Current.CancellationToken;
        const int mib = 250;
        var ruta = ArchivoGrande(mib);
        var storage = Storage(new StubFileManager(_dir, "PUT"));
        var stored = await storage.SubirAsync(Lote, 1, ruta, ct);

        var antes = GC.GetTotalAllocatedBytes(precise: true);
        await using var leido = await storage.OpenReadAsync(stored.StoragePath, ct);
        var (tamano, sha) = await MedirAsync(leido!, ct);
        var asignado = GC.GetTotalAllocatedBytes(precise: true) - antes;

        leido!.CanSeek.Should().BeFalse("el cuerpo de S3 se reenvía tal cual llega, sin búfer intermedio");
        tamano.Should().Be((long)mib * MiB);
        sha.Should().Be(stored.Sha256);
        asignado.Should().BeLessThan(16L * MiB, "la parte de {0} MiB no se carga en memoria al leerla", mib);
    }

    [Fact]
    public async Task HU13379_M2_Borde_DisponerElStream_LiberaLaRespuesta_YElDescargadorBufferizadoNoCambia()
    {
        var ct = TestContext.Current.CancellationToken;
        var ruta = ArchivoGrande(1);
        var stub = new StubFileManager(_dir, "PUT");
        var storage = Storage(stub);
        var stored = await storage.SubirAsync(Lote, 1, ruta, ct);

        var streaming = await storage.OpenReadAsync(stored.StoragePath, ct);
        await streaming!.DisposeAsync();
        var act = () => streaming.ReadAsync(new byte[1], ct).AsTask();
        await act.Should().ThrowAsync<ObjectDisposedException>();

        // Consumidores actuales (adjuntos, logos): siguen recibiendo un stream seekable en memoria.
        using var http = new HttpClient(stub) { BaseAddress = new Uri(Base) };
        await using var buferizado = await FileManagerDownloader.OpenReadAsync(
            http, new FileManagerOptions { BaseUrl = Base, FilesPath = "api/v1/files" }, stored.StoragePath, ct);
        buferizado!.CanSeek.Should().BeTrue();
        buferizado.Length.Should().Be(MiB);
    }

    /// <summary>File-manager + storage en proceso: guarda cada objeto subido en un archivo y lo sirve en streaming.</summary>
    private sealed class StubFileManager(string dir, string? metodo) : HttpMessageHandler
    {
        private readonly ConcurrentDictionary<string, string> _objetos = new();
        private int _ids;

        public int FallosAntesDeAceptar { get; init; }
        public ConcurrentQueue<string> CuerposCreacion { get; } = new();
        public ConcurrentQueue<Subida> Subidas { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
        {
            var uri = req.RequestUri!;
            if (req.Method == HttpMethod.Post && uri.AbsolutePath == "/pdn/api/v1/files")
            {
                CuerposCreacion.Enqueue(await req.Content!.ReadAsStringAsync(ct));
                var id = $"parte_{Interlocked.Increment(ref _ids)}";
                var method = metodo is null ? string.Empty : $",\"method\":\"{metodo}\"";
                return Json("{\"id\":\"" + id + "\",\"presignedUrl\":{\"url\":\"https://s3.test/upload/" + id
                    + "?X-Amz-Signature=abc\"" + method + ",\"fields\":{\"key\":\"tramites/" + id + "\",\"policy\":\"pol\"}}}");
            }

            if (uri.Host == "s3.test" && uri.AbsolutePath.StartsWith("/upload/", StringComparison.Ordinal))
                return await RecibirAsync(req, uri.AbsolutePath["/upload/".Length..], ct);

            if (req.Method == HttpMethod.Get && uri.AbsolutePath.EndsWith("/presigned-url", StringComparison.Ordinal))
            {
                var id = uri.AbsolutePath.Split('/')[^2];
                return _objetos.ContainsKey(id)
                    ? Json($$$"""{"presignedUrl":{"url":"https://s3.test/download/{{{id}}}"}}""")
                    : new HttpResponseMessage(HttpStatusCode.NotFound);
            }

            if (req.Method == HttpMethod.Get && uri.Host == "s3.test" && _objetos.TryGetValue(uri.AbsolutePath.Split('/')[^1], out var ruta))
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(File.OpenRead(ruta)) };

            return new HttpResponseMessage(HttpStatusCode.InternalServerError);
        }

        private async Task<HttpResponseMessage> RecibirAsync(HttpRequestMessage req, string id, CancellationToken ct)
        {
            HttpContent archivo;
            List<string> nombres = [];
            if (req.Content is MultipartFormDataContent form)
            {
                nombres = [.. form.Select(p => p.Headers.ContentDisposition!.Name!.Trim('"'))];
                archivo = form.Single(p => p.Headers.ContentDisposition!.Name!.Trim('"') == "file");
            }
            else
            {
                archivo = req.Content!;
            }

            Subidas.Enqueue(new Subida(req.Method.Method, archivo.GetType().Name, archivo.Headers.ContentLength, nombres));
            if (Subidas.Count <= FallosAntesDeAceptar)
                return new HttpResponseMessage(HttpStatusCode.BadGateway);

            // Copia en streaming (búfer de 80 KiB), como haría el storage.
            var ruta = Path.Combine(dir, $"objeto-{id}.bin");
            await using (var origen = await archivo.ReadAsStreamAsync(ct))
            await using (var destino = File.Create(ruta))
                await origen.CopyToAsync(destino, ct);
            _objetos[id] = ruta;
            return new HttpResponseMessage(HttpStatusCode.OK);
        }

        private static HttpResponseMessage Json(string json) =>
            new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    }

    private sealed record Subida(string Metodo, string ContenidoArchivo, long? ContentLength, IReadOnlyList<string> NombresMultipart);
}
