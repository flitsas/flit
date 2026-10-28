using System.Net;
using System.Text;
using Flit.PersonaNatural.Api.Adjuntos;
using Flit.PersonaNatural.Api.Persistence;
using Flit.PersonaNatural.Api.Pii;
using Flit.PersonaNatural.Api.Solicitudes;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Flit.PersonaNatural.Tests;

/// <summary>Almacenamiento de prueba: guarda en memoria, recuerda qué se pidió borrar y puede fallar a pedido.</summary>
internal sealed class AlmacenEnMemoria : IAlmacenAdjuntos
{
    public Dictionary<string, byte[]> Archivos { get; } = [];
    public List<string> Eliminados { get; } = [];
    public List<string> Nombres { get; } = [];
    public bool Falla;

    public Task<ArchivoGuardado> GuardarAsync(Guid solicitudId, string tipo, string nombre, byte[] contenido, CancellationToken ct)
    {
        if (Falla)
            throw new InvalidOperationException("almacén caído");
        var referencia = $"ref-{Archivos.Count + 1}";
        Archivos[referencia] = contenido;
        Nombres.Add(nombre);
        return Task.FromResult(new ArchivoGuardado(referencia, Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(contenido)), contenido.LongLength));
    }

    public Task EliminarAsync(string referencia, CancellationToken ct)
    {
        Eliminados.Add(referencia);
        return Task.CompletedTask;
    }
}

/// <summary>Archivos mínimos válidos por firma: lo que importa es lo que dicen los primeros bytes.</summary>
internal static class Archivos
{
    public static byte[] Pdf(int extra = 0) => Concat("%PDF-1.7\n"u8.ToArray(), new byte[extra]);

    public static byte[] Png() => Concat([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A], new byte[16]);

    public static byte[] Jpeg() => Concat([0xFF, 0xD8, 0xFF, 0xE0], new byte[16]);

    public static byte[] Webp() => Concat("RIFF"u8.ToArray(), [0x10, 0, 0, 0], "WEBP"u8.ToArray(), new byte[8]);

    public static byte[] Ejecutable() => Concat("MZ"u8.ToArray(), new byte[64]);

    private static byte[] Concat(params byte[][] partes) => [.. partes.SelectMany(p => p)];
}

/// <summary>
/// HU #14048 (Feature D, Épica #14021) — adjuntos del trámite y comprobante de pago sobre el host real y un almacenamiento
/// falso: se valida el contenido real, el tamaño y el documento contra el snapshot, y en la base solo queda la referencia
/// opaca. Base efímera propia; sin motor se omite en local y falla en CI.
/// </summary>
public sealed class AdjuntosTests : IAsyncLifetime
{
    private const string Base = "/api/v1/persona-natural";

    private readonly AlmacenEnMemoria _almacen = new();
    private PortalHost _host = null!;
    private bool _iniciado;

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        if (_iniciado)
            await _host.DisposeAsync();
    }

    private async Task IniciarAsync(bool conAlmacen = true, params (string Clave, string Valor)[] ajustes)
    {
        _host = await PortalHost.IniciarAsync("pnadj", (servicios, _) =>
        {
            if (conAlmacen)
                servicios.AddSingleton<IAlmacenAdjuntos>(_almacen);
        }, ajustes);
        _iniciado = true;
        _host.Definiciones.PorCodigo["TRASPASO_STANDARD"] = DefinicionesDePrueba.Traspaso();
    }

    // ── Carga ────────────────────────────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("contrato.pdf", "application/pdf")]
    [InlineData("foto.JPG", "image/jpeg")]
    [InlineData("foto.jpeg", "image/jpeg")]
    [InlineData("foto.png", "image/png")]
    [InlineData("foto.webp", "image/webp")]
    public async Task UnArchivoValido_SeAlmacena_YLaRespuestaTraeIdTipoTamanoYHash(string nombre, string mime)
    {
        await IniciarAsync();
        var (id, token) = await Sesion();
        var contenido = mime switch { "application/pdf" => Archivos.Pdf(), "image/png" => Archivos.Png(), "image/webp" => Archivos.Webp(), _ => Archivos.Jpeg() };

        var r = await _host.SubirAsync($"{Base}/solicitudes/{id}/adjuntos", token, contenido, nombre, "compraventa");

        r.StatusCode.Should().Be(HttpStatusCode.Created);
        var json = (await PortalHost.JsonAsync(r)).RootElement;
        json.GetProperty("tipo").GetString().Should().Be("compraventa");
        json.GetProperty("mimeType").GetString().Should().Be(mime);
        json.GetProperty("tamanoBytes").GetInt64().Should().Be(contenido.Length);
        json.GetProperty("sha256").GetString().Should().Be(Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(contenido)));
        await using var db = _host.NuevaDb();
        var fila = await db.Set<Adjunto>().SingleAsync(TestContext.Current.CancellationToken);
        fila.StoragePath.Should().Be("ref-1", "en la base solo queda la referencia opaca");
        fila.Tipo.Should().Be("compraventa");
    }

    [Fact]
    public async Task ElNombreOriginalNoSeConfia_SeNormalizaConLaExtensionDelTipoReal()
    {
        await IniciarAsync();
        var (id, token) = await Sesion();

        await _host.SubirAsync($"{Base}/solicitudes/{id}/adjuntos", token, Archivos.Pdf(), "../../Cédula de Juan Pérez (copia).PDF", "compraventa");

        _almacen.Nombres.Should().ContainSingle().Which.Should().Be("C_dula_de_Juan_P_rez__copia.pdf");
    }

    [Fact]
    public async Task UnEjecutable_UnaExtensionQueNoCoincide_YUnTipoNoPermitido_SeRechazanTrasVerLaFirma()
    {
        await IniciarAsync();
        var (id, token) = await Sesion();

        var exe = await _host.SubirAsync($"{Base}/solicitudes/{id}/adjuntos", token, Archivos.Ejecutable(), "contrato.pdf", "compraventa");
        var exeConSuExtension = await _host.SubirAsync($"{Base}/solicitudes/{id}/adjuntos", token, Archivos.Ejecutable(), "setup.exe", "compraventa");
        var pngComoPdf = await _host.SubirAsync($"{Base}/solicitudes/{id}/adjuntos", token, Archivos.Png(), "contrato.pdf", "compraventa");
        var pdfSinExtension = await _host.SubirAsync($"{Base}/solicitudes/{id}/adjuntos", token, Archivos.Pdf(), "contrato", "compraventa");

        foreach (var r in new[] { exe, exeConSuExtension, pngComoPdf, pdfSinExtension })
        {
            r.StatusCode.Should().Be(HttpStatusCode.UnsupportedMediaType);
            (await PortalHost.CodigoAsync(r)).Should().Be("adjunto_no_permitido");
        }

        _almacen.Archivos.Should().BeEmpty("nada de lo rechazado llega al almacenamiento");
        await using var db = _host.NuevaDb();
        (await db.Set<Adjunto>().CountAsync(TestContext.Current.CancellationToken)).Should().Be(0);
    }

    [Fact]
    public async Task UnArchivoQueSuperaElTope_Responde413_YNoSeAlmacena()
    {
        await IniciarAsync(true, ("Adjuntos:MaxBytes", "2048"));
        var (id, token) = await Sesion();

        var r = await _host.SubirAsync($"{Base}/solicitudes/{id}/adjuntos", token, Archivos.Pdf(extra: 4096), "grande.pdf", "compraventa");

        r.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge);
        (await PortalHost.CodigoAsync(r)).Should().Be("adjunto_no_permitido");
        _almacen.Archivos.Should().BeEmpty();
    }

    [Fact]
    public async Task ElTopePorDefectoEs20MB()
    {
        await IniciarAsync();

        _host.App.Services.GetRequiredService<IOptions<AdjuntosOptions>>().Value.MaxBytes.Should().Be(20L * 1024 * 1024);
    }

    [Fact]
    public async Task UnDocumentoQueNoEsDelTipo_Responde422_YUnArchivoVacioOSinTipoTambien()
    {
        await IniciarAsync();
        var (id, token) = await Sesion();

        var ajeno = await _host.SubirAsync($"{Base}/solicitudes/{id}/adjuntos", token, Archivos.Pdf(), "x.pdf", "certificado_dijin");
        var sinTipo = await _host.SubirAsync($"{Base}/solicitudes/{id}/adjuntos", token, Archivos.Pdf(), "x.pdf");
        var vacio = await _host.SubirAsync($"{Base}/solicitudes/{id}/adjuntos", token, [], "x.pdf", "soat");

        ajeno.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await PortalHost.CodigoAsync(ajeno)).Should().Be("documento_no_pertenece_al_tipo");
        (await PortalHost.CodigoAsync(sinTipo)).Should().Be("datos_invalidos");
        (await PortalHost.CodigoAsync(vacio)).Should().Be("datos_invalidos");
        _almacen.Archivos.Should().BeEmpty();
    }

    [Fact]
    public async Task UnDocumentoCargado_CuentaParaElRequisito_YApareceEnElBorrador()
    {
        await IniciarAsync();
        var (id, token) = await Sesion();
        (await Pendientes(id, token)).Should().Contain("documento_requerido:compraventa");

        await _host.SubirAsync($"{Base}/solicitudes/{id}/adjuntos", token, Archivos.Pdf(), "c.pdf", "compraventa");

        (await Pendientes(id, token)).Should().NotContain("documento_requerido:compraventa").And.Contain("documento_requerido:soat");
        var borrador = (await PortalHost.JsonAsync(await _host.EnviarAsync(HttpMethod.Get, $"{Base}/solicitudes/{id}", token: token))).RootElement;
        borrador.GetProperty("adjuntos").GetArrayLength().Should().Be(1);
        borrador.GetProperty("adjuntos")[0].GetProperty("tipo").GetString().Should().Be("compraventa");
        borrador.GetProperty("adjuntos")[0].TryGetProperty("storagePath", out _).Should().BeFalse("la referencia al almacenamiento no sale del servicio");
    }

    // ── Comprobante de pago ──────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ElComprobante_QuedaLigadoALaSolicitud_ElNuevoReemplazaAlAnteriorYElAnteriorQuedaEnElHistorial()
    {
        await IniciarAsync();
        var (id, token) = await Sesion();
        (await Pendientes(id, token)).Should().NotContain("comprobante_pago_requerido:", "el comprobante ya no es requisito del borrador");

        var primero = await _host.SubirAsync($"{Base}/solicitudes/{id}/pago/comprobante", token, Archivos.Pdf(), "pago1.pdf");
        var segundo = await _host.SubirAsync($"{Base}/solicitudes/{id}/pago/comprobante", token, Archivos.Png(), "pago2.png");

        primero.StatusCode.Should().Be(HttpStatusCode.Created);
        segundo.StatusCode.Should().Be(HttpStatusCode.Created);
        (await Pendientes(id, token)).Should().NotContain("comprobante_pago_requerido:");
        await using var db = _host.NuevaDb();
        var filas = await db.Set<Adjunto>().Where(a => a.Tipo == "comprobante_pago").OrderBy(a => a.CreatedAt).ToListAsync(TestContext.Current.CancellationToken);
        filas.Should().HaveCount(2, "el comprobante anterior se conserva como historial");
        filas[^1].MimeType.Should().Be("image/png", "el vigente es el último");
    }

    // ── Eliminar y cancelar ──────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AlEliminar_SaleDelAlmacenamientoYDeLaSolicitud_ConSuTrazaDeAuditoria()
    {
        await IniciarAsync();
        var (id, token) = await Sesion();
        var adjuntoId = await Subir(id, token, "compraventa");

        var r = await _host.EnviarAsync(HttpMethod.Delete, $"{Base}/solicitudes/{id}/adjuntos/{adjuntoId}", token: token);

        r.StatusCode.Should().Be(HttpStatusCode.NoContent);
        _almacen.Eliminados.Should().Equal("ref-1");
        await using var db = _host.NuevaDb();
        (await db.Set<Adjunto>().CountAsync(TestContext.Current.CancellationToken)).Should().Be(0);
        (await db.Set<AccesoPiiLog>().AnyAsync(a => a.Accion == "adjunto_eliminado" && a.SolicitudId == id, TestContext.Current.CancellationToken)).Should().BeTrue();
        (await Pendientes(id, token)).Should().Contain("documento_requerido:compraventa");
    }

    [Fact]
    public async Task UnArchivoDeOtraSolicitud_Responde403ConAuditoria_YNoSeBorra()
    {
        await IniciarAsync();
        var (idAjeno, tokenAjeno) = await Sesion();
        var ajeno = await Subir(idAjeno, tokenAjeno, "compraventa");
        var (id, token) = await Sesion();

        var borrar = await _host.EnviarAsync(HttpMethod.Delete, $"{Base}/solicitudes/{id}/adjuntos/{ajeno}", token: token);
        var usarOtraRuta = await _host.SubirAsync($"{Base}/solicitudes/{idAjeno}/adjuntos", token, Archivos.Pdf(), "x.pdf", "soat");

        borrar.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await PortalHost.CodigoAsync(borrar)).Should().Be("adjunto_ajeno");
        usarOtraRuta.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        _almacen.Eliminados.Should().BeEmpty();
        await using var db = _host.NuevaDb();
        (await db.Set<Adjunto>().AnyAsync(a => a.Id == ajeno, TestContext.Current.CancellationToken)).Should().BeTrue();
        (await db.Set<AccesoPiiLog>().AnyAsync(a => a.Accion == "sesion_fuera_de_alcance", TestContext.Current.CancellationToken)).Should().BeTrue();
    }

    [Fact]
    public async Task UnaSolicitudEnCola_NoAceptaNiQuitaArchivos()
    {
        await IniciarAsync();
        var (id, token) = await Sesion();
        var adjuntoId = await Subir(id, token, "compraventa");
        await using (var db = _host.NuevaDb())
            await db.Set<SolicitudTramite>().ExecuteUpdateAsync(set => set.SetProperty(s => s.Estado, EstadoSolicitud.EnCola), TestContext.Current.CancellationToken);

        var subir = await _host.SubirAsync($"{Base}/solicitudes/{id}/adjuntos", token, Archivos.Pdf(), "otro.pdf", "soat");
        var borrar = await _host.EnviarAsync(HttpMethod.Delete, $"{Base}/solicitudes/{id}/adjuntos/{adjuntoId}", token: token);

        subir.StatusCode.Should().Be(HttpStatusCode.Conflict);
        borrar.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await PortalHost.CodigoAsync(borrar)).Should().Be("estado_no_permite_cambio");
        _almacen.Archivos.Should().HaveCount(1);
        _almacen.Eliminados.Should().BeEmpty();
    }

    [Fact]
    public async Task AlCancelarElTramite_SusArchivosSalenDelAlmacenamientoYDeLaBase()
    {
        await IniciarAsync();
        var (id, token) = await Sesion();
        await Subir(id, token, "compraventa");
        await Subir(id, token, "soat");

        var r = await _host.EnviarAsync(HttpMethod.Delete, $"{Base}/solicitudes/{id}", token: token);

        r.StatusCode.Should().Be(HttpStatusCode.OK);
        _almacen.Eliminados.Should().BeEquivalentTo("ref-1", "ref-2");
        await using var db = _host.NuevaDb();
        (await db.Set<Adjunto>().CountAsync(TestContext.Current.CancellationToken)).Should().Be(0);
    }

    [Fact]
    public async Task LaRetencionDeUnBorradorAbandonado_TambienBorraSusArchivos()
    {
        await IniciarAsync();
        var (id, token) = await Sesion();
        await Subir(id, token, "compraventa");
        _host.Reloj.Avanzar(TimeSpan.FromDays(31));

        var anonimizados = await _host.EnScopeAsync(sp => sp.GetRequiredService<RetencionService>().AnonimizarAbandonadosAsync(TestContext.Current.CancellationToken));

        anonimizados.Should().Be(1);
        _almacen.Eliminados.Should().Equal("ref-1");
        await using var db = _host.NuevaDb();
        (await db.Set<Adjunto>().CountAsync(TestContext.Current.CancellationToken)).Should().Be(0);
    }

    // ── Almacenamiento ───────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SinAlmacenamientoConfigurado_ElServicioArranca_YCargarResponde503ConCodigo()
    {
        await IniciarAsync(conAlmacen: false);
        var (id, token) = await Sesion();

        var r = await _host.SubirAsync($"{Base}/solicitudes/{id}/adjuntos", token, Archivos.Pdf(), "c.pdf", "compraventa");

        r.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await PortalHost.CodigoAsync(r)).Should().Be("almacenamiento_no_disponible");
    }

    [Fact]
    public async Task SiElAlmacenamientoFalla_Responde502_YNoQuedaNingunRegistro()
    {
        await IniciarAsync();
        var (id, token) = await Sesion();
        _almacen.Falla = true;

        var r = await _host.SubirAsync($"{Base}/solicitudes/{id}/adjuntos", token, Archivos.Pdf(), "c.pdf", "compraventa");

        r.StatusCode.Should().Be(HttpStatusCode.BadGateway);
        (await PortalHost.CodigoAsync(r)).Should().Be("almacenamiento_no_disponible");
        await using var db = _host.NuevaDb();
        (await db.Set<Adjunto>().CountAsync(TestContext.Current.CancellationToken)).Should().Be(0);
    }

    [Fact]
    public async Task ElNombreYElContenidoNuncaLlegaALosLogs()
    {
        await IniciarAsync();
        var (id, token) = await Sesion();

        await _host.SubirAsync($"{Base}/solicitudes/{id}/adjuntos", token, Archivos.Pdf(), "cedula-de-juan-perez.pdf", "compraventa");

        lock (_host.Logs.Mensajes)
            _host.Logs.Mensajes.Should().NotContain(m => m.Contains("juan", StringComparison.OrdinalIgnoreCase) || m.Contains("%PDF"));
    }

    [Fact]
    public async Task ElFileManager_CreaElRegistro_SubeALaUrlPrefirmada_YDevuelveSoloElId()
    {
        var manejador = new ManejadorFileManager();
        using var http = new HttpClient(manejador) { BaseAddress = new Uri("https://files.test/") };
        var almacen = new AlmacenFileManager(http, Options.Create(new FileManagerOptions { Category = "persona-natural" }));
        var contenido = Archivos.Pdf(extra: 10);

        var guardado = await almacen.GuardarAsync(Guid.NewGuid(), "compraventa", "c.pdf", contenido, TestContext.Current.CancellationToken);

        guardado.Referencia.Should().Be("fm-123");
        guardado.TamanoBytes.Should().Be(contenido.Length);
        guardado.Sha256.Should().Be(Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(contenido)));
        manejador.Peticiones.Should().HaveCount(2);
        manejador.Peticiones[0].Should().Contain("POST https://files.test/api/v1/files").And.Contain("\"category\":\"persona-natural\"");
        manejador.Peticiones[1].Should().StartWith("POST https://s3.test/subida");
        manejador.CuerpoSubida.IndexOf("name=key", StringComparison.Ordinal).Should().BeLessThan(manejador.CuerpoSubida.IndexOf("name=file", StringComparison.Ordinal), "los campos firmados van antes del archivo");
        await almacen.EliminarAsync("fm-123", TestContext.Current.CancellationToken);
    }

    [Theory]
    [InlineData("%PDF-1.4", "application/pdf")]
    [InlineData("RIFFxxxxWEBPyy", "image/webp")]
    [InlineData("RIFFxxxxWAVEyy", null)]
    [InlineData("MZ\0\0", null)]
    [InlineData("", null)]
    public void LaFirmaDelArchivoDiceQueEs(string inicio, string? esperado) =>
        FirmaDeArchivo.Detectar(Encoding.Latin1.GetBytes(inicio)).Should().Be(esperado);

    // ── Soporte ──────────────────────────────────────────────────────────────────────────────────────────────────────

    private async Task<(Guid Id, string Token)> Sesion()
    {
        var r = await _host.EnviarAsync(HttpMethod.Post, $"{Base}/sesiones",
            new { tipoCodigo = "TRASPASO_STANDARD", organismoId = PortalHost.Organismo, cedula = PortalHost.Cedula, placa = "ABC123", consentimiento = true }, captcha: true);
        r.StatusCode.Should().Be(HttpStatusCode.Created);
        var json = (await PortalHost.JsonAsync(r)).RootElement;
        return (json.GetProperty("solicitudId").GetGuid(), json.GetProperty("token").GetString()!);
    }

    private async Task<Guid> Subir(Guid id, string token, string tipo)
    {
        var r = await _host.SubirAsync($"{Base}/solicitudes/{id}/adjuntos", token, Archivos.Pdf(), "doc.pdf", tipo);
        r.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await PortalHost.JsonAsync(r)).RootElement.GetProperty("id").GetGuid();
    }

    private async Task<List<string>> Pendientes(Guid id, string token)
    {
        var json = (await PortalHost.JsonAsync(await _host.EnviarAsync(HttpMethod.Get, $"{Base}/solicitudes/{id}", token: token))).RootElement;
        return [.. json.GetProperty("pendientes").EnumerateArray().Select(p => $"{p.GetProperty("codigo").GetString()}:{(p.TryGetProperty("referencia", out var x) ? x.GetString() : null)}")];
    }

    /// <summary>Simula el file-manager (crear el registro) y el almacenamiento prefirmado (recibir el multipart).</summary>
    private sealed class ManejadorFileManager : HttpMessageHandler
    {
        public List<string> Peticiones { get; } = [];
        public string CuerpoSubida { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var cuerpo = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            Peticiones.Add($"{request.Method} {request.RequestUri} {(request.RequestUri!.Host == "files.test" ? cuerpo : string.Empty)}");
            if (request.RequestUri.Host == "files.test")
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""{"id":"fm-123","presignedUrl":{"url":"https://s3.test/subida","method":"POST","fields":{"key":"abc","policy":"p"}}}""", Encoding.UTF8, "application/json"),
                };
            }

            CuerpoSubida = cuerpo;
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        }
    }
}
