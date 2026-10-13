using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Flit.Comparendos.Api.Modulos.Ocr;
using Flit.Comparendos.Api.Persistence.Modelo;
using Flit.Comparendos.Api.Plataforma;
using Flit.Comparendos.Domain.Modelo;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static Flit.Comparendos.Tests.Api.ComparendosDePrueba;

namespace Flit.Comparendos.Tests.Api;

/// <summary>Proveedor OCR guionado: la respuesta depende del contenido del archivo (texto) y se puede encadenar por intento.</summary>
public sealed class OcrDePrueba : IProveedorOcr
{
    public ConcurrentDictionary<string, Queue<Func<ResultadoOcr>>> Guion { get; } = new();

    private int _llamadas;

    public int Llamadas => _llamadas;

    public ConcurrentBag<(string ContentType, byte[] Contenido)> Recibidos { get; } = [];

    public Task<ResultadoOcr> ExtraerAsync(byte[] contenido, string contentType, string prompt, string modelo, CancellationToken ct)
    {
        Interlocked.Increment(ref _llamadas);
        Recibidos.Add((contentType, contenido));
        var clave = Encoding.UTF8.GetString(contenido);
        if (!Guion.TryGetValue(clave, out var cola) || cola.Count == 0)
            return Task.FromResult(ResultadoOcr.Fallo(modelo, TipoFalloOcr.Ilegible, "sin guion"));
        var siguiente = cola.Count > 1 ? cola.Dequeue() : cola.Peek();
        return Task.FromResult(siguiente());
    }

    public void Responder(string clave, params Func<ResultadoOcr>[] respuestas) => Guion[clave] = new Queue<Func<ResultadoOcr>>(respuestas);

    public static ResultadoOcr Campos(string numero, string placa = "ABC124", string? valor = "604100", string? nombre = "TITULAR DE PRUEBA S.A.S.") =>
        new(new Dictionary<string, CampoExtraido>
        {
            [CamposOcr.Numero] = new(numero, 0.98m),
            [CamposOcr.TipoDocumento] = new("NIT", 0.9m),
            [CamposOcr.NumeroDocumento] = new("900123456", 0.9m),
            [CamposOcr.Nombre] = new(nombre, nombre is null ? null : 0.9m),
            [CamposOcr.CodigoInfraccion] = new("c29", 0.95m),
            [CamposOcr.FechaInfraccion] = new("2026-09-28", 0.9m),
            [CamposOcr.Placa] = new(placa, 0.97m),
            [CamposOcr.Secretaria] = new("SECRETARIA DISTRITAL DE MOVILIDAD DE BOGOTA", 0.9m),
            [CamposOcr.Valor] = new(valor, valor is null ? null : 0.9m),
            [CamposOcr.EsFotomulta] = new("false", 0.8m),
        }, "{\"ok\":true}", "modelo-prueba");
}

public sealed class ApiConOcr : ApiConFuente
{
    public OcrDePrueba Ocr { get; } = new();

    protected override void Reemplazar(IServiceCollection services)
    {
        base.Reemplazar(services);
        services.AddSingleton<IProveedorOcr>(Ocr);
    }

    /// <summary>Corre la cola de OCR hasta que no quede nada listo para procesar a la hora del reloj de prueba.</summary>
    public async Task<int> ProcesarAsync()
    {
        var procesador = App.Services.GetRequiredService<ProcesadorOcr>();
        var n = 0;
        while (await procesador.ProcesarSiguienteAsync(CancellationToken.None))
            n++;
        return n;
    }
}

/// <summary>HUs #13469–#13471 — carga por OCR (§6.18, RN-050 a RN-064).</summary>
public sealed class OcrTests(ApiConOcr api) : IClassFixture<ApiConOcr>
{
    private static readonly string[] Operador = [Permisos.OcrVer, Permisos.OcrGestionar, Permisos.BandejaVer];

    private static MultipartFormDataContent Paquete(string nombre, params (string Archivo, string Contenido)[] archivos)
    {
        var form = new MultipartFormDataContent { { new StringContent(nombre), "nombre" }, { new StringContent("true"), "iniciar" } };
        foreach (var (archivo, contenido) in archivos)
            form.Add(new ByteArrayContent(Encoding.UTF8.GetBytes(contenido)), "archivos", archivo);
        return form;
    }

    private async Task<(Guid Empresa, HttpClient Cliente)> EmpresaOcrAsync(bool ocr = true)
    {
        api.Reloj.Hoy(new DateOnly(2026, 10, 9));
        var empresa = await EmpresaAsync(api, ocr: ocr);
        return (empresa, api.Cliente(new UsuarioDePrueba(empresa, Operador)));
    }

    private static async Task<JsonElement> Json(HttpResponseMessage r, CancellationToken ct)
    {
        r.IsSuccessStatusCode.Should().BeTrue(await r.Content.ReadAsStringAsync(ct));
        return await r.Content.ReadFromJsonAsync<JsonElement>(ct);
    }

    private Task<Dictionary<string, DocumentoOcr>> DocumentosAsync(Guid paquete) =>
        api.EnDbAsync(db => db.DocumentosOcr.AsNoTracking().Where(d => d.PaqueteId == paquete).ToDictionaryAsync(d => d.NombreArchivo));

    [Fact]
    public async Task AC1_FlujoCompleto_Extrae_DetectaDuplicados_Complementa_Registra_YCierra()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var (empresa, cliente) = await EmpresaOcrAsync();
        await IngresarAsync(api, empresa, Item("OCR-EXISTE"));
        (await api.Cliente(new UsuarioDePrueba(Guid.NewGuid(), [], SuperAdmin: true)).PutAsJsonAsync("/api/v1/comparendos/admin/infracciones",
            new[] { new { codigo = "C29", descripcion = "Conducir a velocidad superior a la máxima permitida", vigente = true } }, ct)).EnsureSuccessStatusCode();
        api.Ocr.Responder("a", () => OcrDePrueba.Campos("OCR-A", placa: "XYZ-987"));
        api.Ocr.Responder("b", () => OcrDePrueba.Campos("OCR-B", valor: null));
        api.Ocr.Responder("c", () => OcrDePrueba.Campos("ocr existe"));
        api.Ocr.Responder("d", () => OcrDePrueba.Campos("OCR-A"));

        var creado = await Json(await cliente.PostAsync("/api/v1/comparendos/ocr/paquetes", Paquete("Lote octubre", ("a.jpg", "a"), ("b.png", "b"), ("c.pdf", "c"), ("d.jpg", "d"), ("e.jpg", "a")), ct), ct);
        var paquete = creado.GetProperty("id").GetGuid();
        creado.GetProperty("estado").GetString().Should().Be("EnProcesamiento");
        (await api.ProcesarAsync()).Should().Be(5);

        var docs = await DocumentosAsync(paquete);
        docs["a.jpg"].Estado.Should().Be(EstadoDocumentoOcr.Completo);
        docs["b.png"].Estado.Should().Be(EstadoDocumentoOcr.Incompleto);
        docs["b.png"].CamposFaltantes.Should().Equal(CamposOcr.Valor);
        docs["c.pdf"].Estado.Should().Be(EstadoDocumentoOcr.Duplicado);
        docs["c.pdf"].FuenteDuplicado.Should().Be("SIMIT");
        docs["d.jpg"].Estado.Should().Be(EstadoDocumentoOcr.Duplicado);
        docs["d.jpg"].DuplicadoDeDocumentoId.Should().Be(docs["a.jpg"].Id);
        docs["e.jpg"].Estado.Should().Be(EstadoDocumentoOcr.Duplicado, "mismo archivo (sha256) que a.jpg");
        docs["e.jpg"].FuenteDuplicado.Should().Be("ARCHIVO");

        var detalle = await Json(await cliente.GetAsync($"/api/v1/comparendos/ocr/paquetes/{paquete}", ct), ct);
        detalle.GetProperty("paquete").GetProperty("estado").GetString().Should().Be("Procesado");
        detalle.GetProperty("paquete").GetProperty("cantidadDuplicados").GetInt32().Should().Be(3);

        // La descripción de la infracción salió del catálogo (RN-061) y el duplicado muestra el comparendo original.
        var docA = await Json(await cliente.GetAsync($"/api/v1/comparendos/ocr/documentos/{docs["a.jpg"].Id}", ct), ct);
        docA.GetProperty("campos").EnumerateArray().Single(c => c.GetProperty("nombreCampo").GetString() == CamposOcr.DescripcionInfraccion)
            .GetProperty("valorFinal").GetString().Should().Be("Conducir a velocidad superior a la máxima permitida");
        docA.GetProperty("proveedorModelo").ValueKind.Should().Be(JsonValueKind.Null, "la gestora no ve el proveedor ni el modelo");
        var superA = await Json(await api.Cliente(new UsuarioDePrueba(Guid.NewGuid(), [], SuperAdmin: true)).GetAsync($"/api/v1/comparendos/ocr/documentos/{docs["a.jpg"].Id}", ct), ct);
        superA.GetProperty("proveedorModelo").GetString().Should().Contain("modelo-prueba");
        var docC = await Json(await cliente.GetAsync($"/api/v1/comparendos/ocr/documentos/{docs["c.pdf"].Id}", ct), ct);
        docC.GetProperty("duplicado").GetProperty("numero").GetString().Should().Be("OCR-EXISTE");
        (await cliente.PostAsync($"/api/v1/comparendos/ocr/documentos/{docs["c.pdf"].Id}/registrar", null, ct)).StatusCode.Should().Be(HttpStatusCode.Conflict);

        // El operador completa el valor que faltaba (RN-057): queda en el log y el documento pasa a COMPLETO.
        var corregido = await Json(await cliente.PutAsJsonAsync($"/api/v1/comparendos/ocr/documentos/{docs["b.png"].Id}/campos",
            new { campos = new Dictionary<string, string?> { [CamposOcr.Valor] = "302.050" }, motivo = "Leído del original" }, ct), ct);
        corregido.GetProperty("estado").GetString().Should().Be("Completo");

        var registro = await Json(await cliente.PostAsync($"/api/v1/comparendos/ocr/paquetes/{paquete}/registrar-completos", null, ct), ct);
        registro.GetProperty("registrados").GetArrayLength().Should().Be(2);
        registro.GetProperty("placasPorMonitorear").EnumerateArray().Select(p => p.GetString()).Should().BeEquivalentTo("XYZ987", "ABC124");

        var a = await api.EnDbAsync(db => db.Comparendos.AsNoTracking().SingleAsync(c => c.TenantId == empresa && c.NumeroNormalizado == "OCRA", ct));
        (a.TipoIngreso, a.FuentePrincipal, a.ArchivoEvidenciaId).Should().Be((TipoIngreso.Ocr, (CanalIngesta?)null, docs["a.jpg"].ArchivoId));
        a.FechaNotificacion.Should().Be(new DateOnly(2026, 9, 28), "físico sin fecha de notificación: se notificó en la vía (S-40)");
        a.FechaLimiteGestion.Should().NotBeNull();
        var b = await api.EnDbAsync(db => db.Comparendos.AsNoTracking().SingleAsync(c => c.TenantId == empresa && c.NumeroNormalizado == "OCRB", ct));
        (b.TipoIngreso, b.ValorTotal).Should().Be((TipoIngreso.OcrComplemento, 302050m));
        (await api.EnDbAsync(db => db.Timeline.CountAsync(t => t.ComparendoId == b.Id && t.Tipo == TipoEventoTimeline.Ingreso, ct))).Should().Be(1);
        var edicion = await api.EnDbAsync(db => db.DocumentoOcrEdiciones.SingleAsync(e => e.DocumentoOcrId == docs["b.png"].Id, ct));
        (edicion.Motivo, edicion.UsuarioNombre).Should().Be(("Leído del original", "operador@empresa.test"));
        (await api.EnDbAsync(db => db.PaquetesOcr.SingleAsync(p => p.Id == paquete, ct))).CreadoPorNombre.Should().Be("operador@empresa.test");

        var final = await api.EnDbAsync(db => db.PaquetesOcr.AsNoTracking().SingleAsync(p => p.Id == paquete, ct));
        (final.Estado, final.CantidadRegistrados, final.CerradoEn is not null).Should().Be((EstadoPaqueteOcr.Cerrado, 2, true));

        // RN-058: registrado ya no se edita desde OCR.
        (await cliente.PutAsJsonAsync($"/api/v1/comparendos/ocr/documentos/{docs["a.jpg"].Id}/campos",
            new { campos = new Dictionary<string, string?> { [CamposOcr.Placa] = "AAA111" } }, ct)).StatusCode.Should().Be(HttpStatusCode.Conflict);

        var stats = await Json(await cliente.GetAsync("/api/v1/comparendos/ocr/estadisticas", ct), ct);
        var mes = stats.EnumerateArray().Single();
        (mes.GetProperty("procesados").GetInt32(), mes.GetProperty("completos").GetInt32(), mes.GetProperty("incompletos").GetInt32(), mes.GetProperty("duplicados").GetInt32())
            .Should().Be((5, 1, 1, 3));
    }

    [Fact]
    public async Task AC2_FalloTecnico_ReintentaConBackoff_TresIntentos_Alerta_YReintentoManual()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var (_, cliente) = await EmpresaOcrAsync();
        await api.EnDbAsync(async db =>
        {
            db.AlertaDestinatarios.Add(new AlertaDestinatario { Id = Guid.NewGuid(), Correo = "infra@flit.test", Nombre = "Infra", TipoAlerta = TipoAlertaDestinatario.SoloOcr, CreadoEn = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync(ct);
        });
        var caido = () => ResultadoOcr.Fallo("m", TipoFalloOcr.Http5xx, "503");
        api.Ocr.Responder("t", caido, caido, caido, () => OcrDePrueba.Campos("OCR-T"));
        var paquete = (await Json(await cliente.PostAsync("/api/v1/comparendos/ocr/paquetes", Paquete("Reintentos", ("t.jpg", "t")), ct), ct)).GetProperty("id").GetGuid();

        (await api.ProcesarAsync()).Should().Be(1);
        var doc = (await DocumentosAsync(paquete))["t.jpg"];
        (doc.Estado, doc.Intentos, doc.ProximoIntentoEn).Should().Be((EstadoDocumentoOcr.Pendiente, (short)1, api.Reloj.Ahora.AddSeconds(5)));
        (await api.ProcesarAsync()).Should().Be(0, "el backoff de 5 s aún no vence");

        api.Reloj.Ahora = api.Reloj.Ahora.AddSeconds(5);
        await api.ProcesarAsync();
        doc = (await DocumentosAsync(paquete))["t.jpg"];
        (doc.Intentos, doc.ProximoIntentoEn).Should().Be(((short)2, api.Reloj.Ahora.AddSeconds(15)));

        api.Reloj.Ahora = api.Reloj.Ahora.AddSeconds(15);
        await api.ProcesarAsync();
        doc = (await DocumentosAsync(paquete))["t.jpg"];
        (doc.Estado, doc.Intentos, doc.TipoFallo).Should().Be((EstadoDocumentoOcr.Fallido, (short)3, TipoFalloOcr.Http5xx));
        (await api.EnDbAsync(db => db.PaquetesOcr.SingleAsync(p => p.Id == paquete, ct))).Estado.Should().Be(EstadoPaqueteOcr.ProcesadoConErrores);
        (await api.EnDbAsync(db => db.DocumentoOcrIntentos.CountAsync(i => i.DocumentoOcrId == doc.Id, ct))).Should().Be(3);
        var alerta = await api.EnDbAsync(db => db.Comunicaciones.SingleAsync(c => c.Tipo == TipoComunicacion.AlertaFalloOcr, ct));
        (alerta.PlantillaClave, alerta.Destinatarios).Should().Match<(string, string)>(x => x.Item1 == "comparendos.alerta_fallo_ocr" && x.Item2.Contains("infra@flit.test"));

        (await cliente.PostAsync($"/api/v1/comparendos/ocr/documentos/{doc.Id}/reintentar", null, ct)).StatusCode.Should().Be(HttpStatusCode.Accepted);
        await api.ProcesarAsync();
        (await DocumentosAsync(paquete))["t.jpg"].Estado.Should().Be(EstadoDocumentoOcr.Completo);
        (await api.EnDbAsync(db => db.PaquetesOcr.SingleAsync(p => p.Id == paquete, ct))).Estado.Should().Be(EstadoPaqueteOcr.Procesado);
    }

    [Fact]
    public async Task AC3_Ilegible_NoSeReintenta_YSeDescarta()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var (_, cliente) = await EmpresaOcrAsync();
        api.Ocr.Responder("vacio", () => new ResultadoOcr(new Dictionary<string, CampoExtraido> { [CamposOcr.Numero] = new(null, null) }, "{}", "m"));
        var paquete = (await Json(await cliente.PostAsync("/api/v1/comparendos/ocr/paquetes", Paquete("Ilegibles", ("x.png", "vacio")), ct), ct)).GetProperty("id").GetGuid();
        var antes = api.Ocr.Llamadas;
        await api.ProcesarAsync();
        api.Ocr.Llamadas.Should().Be(antes + 1);
        var doc = (await DocumentosAsync(paquete))["x.png"];
        (doc.Estado, doc.TipoFallo).Should().Be((EstadoDocumentoOcr.Fallido, TipoFalloOcr.Ilegible));
        (await cliente.PostAsync($"/api/v1/comparendos/ocr/documentos/{doc.Id}/reintentar", null, ct)).StatusCode.Should().Be(HttpStatusCode.Conflict);

        (await Json(await cliente.PostAsJsonAsync($"/api/v1/comparendos/ocr/paquetes/{paquete}/descartar-fallidos", new { motivo = "Ilegible" }, ct), ct))
            .GetProperty("descartados").GetInt32().Should().Be(1);
        (await api.EnDbAsync(db => db.PaquetesOcr.SingleAsync(p => p.Id == paquete, ct))).Estado.Should().Be(EstadoPaqueteOcr.Cerrado);
    }

    [Fact]
    public async Task AC4_Validaciones_Capacidad_Formato_YAislamiento()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var (_, sinOcr) = await EmpresaOcrAsync(ocr: false);
        (await sinOcr.PostAsync("/api/v1/comparendos/ocr/paquetes", Paquete("x", ("a.jpg", "a")), ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var (_, cliente) = await EmpresaOcrAsync();
        var malo = await cliente.PostAsync("/api/v1/comparendos/ocr/paquetes", Paquete("x", ("hoja.xlsx", "a")), ct);
        malo.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await malo.Content.ReadAsStringAsync(ct)).Should().Contain("ARCHIVOS_INVALIDOS").And.Contain("hoja.xlsx");
        (await cliente.PostAsync("/api/v1/comparendos/ocr/paquetes", Paquete(" ", ("a.jpg", "a")), ct)).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        api.Ocr.Responder("iso", () => OcrDePrueba.Campos("OCR-ISO"));
        var paquete = (await Json(await cliente.PostAsync("/api/v1/comparendos/ocr/paquetes", Paquete("Propio", ("iso.jpg", "iso")), ct), ct)).GetProperty("id").GetGuid();
        var (_, otra) = await EmpresaOcrAsync();
        (await otra.GetAsync($"/api/v1/comparendos/ocr/paquetes/{paquete}", ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await otra.PostAsync($"/api/v1/comparendos/ocr/paquetes/{paquete}/registrar-completos", null, ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        var super = api.Cliente(new UsuarioDePrueba(Guid.NewGuid(), [], SuperAdmin: true));
        (await super.GetAsync($"/api/v1/comparendos/ocr/paquetes/{paquete}", ct)).StatusCode.Should().Be(HttpStatusCode.OK, "el SuperAdmin lee");
        (await super.PostAsync($"/api/v1/comparendos/ocr/paquetes/{paquete}/registrar-completos", null, ct)).IsSuccessStatusCode.Should().BeFalse("el SuperAdmin no opera");
    }

    [Fact]
    public async Task AC5_PromptVersionado_CrearProbarPublicar_EInmutable()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var super = api.Cliente(new UsuarioDePrueba(Guid.NewGuid(), [], SuperAdmin: true));
        var lista = await Json(await super.GetAsync("/api/v1/comparendos/admin/ocr/prompts", ct), ct);
        var v1 = lista.EnumerateArray().Single(p => p.GetProperty("numeroVersion").GetInt32() == 1);
        v1.GetProperty("publicada").GetBoolean().Should().BeTrue();

        (await super.PutAsJsonAsync($"/api/v1/comparendos/admin/ocr/prompts/{v1.GetProperty("id").GetGuid()}", new { texto = "x", proveedor = "ANTHROPIC", modelo = "m" }, ct))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await super.PostAsJsonAsync("/api/v1/comparendos/admin/ocr/prompts", new { texto = "x", proveedor = "GEMINI", modelo = "g" }, ct))
            .StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        var nueva = (await Json(await super.PostAsJsonAsync("/api/v1/comparendos/admin/ocr/prompts", new { texto = "Extrae el comparendo v2", proveedor = "anthropic", modelo = "claude-sonnet-5-5" }, ct), ct)).GetProperty("id").GetGuid();
        api.Ocr.Responder("muestra", () => OcrDePrueba.Campos("MUESTRA-1"));
        var prueba = new MultipartFormDataContent { { new ByteArrayContent(Encoding.UTF8.GetBytes("muestra")), "archivo", "muestra.jpg" } };
        var r = await Json(await super.PostAsync($"/api/v1/comparendos/admin/ocr/prompts/{nueva}/probar", prueba, ct), ct);
        r.GetProperty("campos").GetProperty(CamposOcr.Numero).GetProperty("valor").GetString().Should().Be("MUESTRA-1");

        (await super.PostAsync($"/api/v1/comparendos/admin/ocr/prompts/{nueva}/publicar", null, ct)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        var publicadas = await api.EnDbAsync(db => db.PromptsOcr.Where(p => p.Publicada).Select(p => p.Id).ToListAsync(ct));
        publicadas.Should().Equal(nueva);
        (await api.Cliente(new UsuarioDePrueba(Guid.NewGuid(), Operador)).GetAsync("/api/v1/comparendos/admin/ocr/prompts", ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // Se devuelve la v1 publicada para no afectar a las demás pruebas de la clase.
        (await super.PostAsync($"/api/v1/comparendos/admin/ocr/prompts/{v1.GetProperty("id").GetGuid()}/publicar", null, ct)).EnsureSuccessStatusCode();
    }

    /// <summary>TIFF de prueba en memoria (RGB, una franja por fila).</summary>
    private static byte[] Tiff(int paginas)
    {
        var ms = new MemoryStream();
        using (var t = BitMiracle.LibTiff.Classic.Tiff.ClientOpen("x.tif", "w", ms, new BitMiracle.LibTiff.Classic.TiffStream()))
        {
            for (var p = 0; p < paginas; p++)
            {
                t.SetField(BitMiracle.LibTiff.Classic.TiffTag.IMAGEWIDTH, 4);
                t.SetField(BitMiracle.LibTiff.Classic.TiffTag.IMAGELENGTH, 3);
                t.SetField(BitMiracle.LibTiff.Classic.TiffTag.SAMPLESPERPIXEL, 3);
                t.SetField(BitMiracle.LibTiff.Classic.TiffTag.BITSPERSAMPLE, 8);
                t.SetField(BitMiracle.LibTiff.Classic.TiffTag.PHOTOMETRIC, BitMiracle.LibTiff.Classic.Photometric.RGB);
                t.SetField(BitMiracle.LibTiff.Classic.TiffTag.PLANARCONFIG, BitMiracle.LibTiff.Classic.PlanarConfig.CONTIG);
                t.SetField(BitMiracle.LibTiff.Classic.TiffTag.ROWSPERSTRIP, 3);
                if (paginas > 1)
                    t.SetField(BitMiracle.LibTiff.Classic.TiffTag.SUBFILETYPE, BitMiracle.LibTiff.Classic.FileType.PAGE);
                for (var y = 0; y < 3; y++)
                    t.WriteScanline(Enumerable.Repeat((byte)(60 * (y + p)), 12).ToArray(), y);
                t.WriteDirectory();
            }
        }

        return ms.ToArray();
    }

    [Fact]
    public async Task Tiff_SeConvierte_YElOriginalQuedaComoEvidencia()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var (_, cliente) = await EmpresaOcrAsync();
        var form = new MultipartFormDataContent { { new StringContent("TIFF"), "nombre" }, { new StringContent("true"), "iniciar" } };
        form.Add(new ByteArrayContent(Tiff(1)), "archivos", "una.tif");
        form.Add(new ByteArrayContent(Tiff(2)), "archivos", "dos.tiff");
        var paquete = (await Json(await cliente.PostAsync("/api/v1/comparendos/ocr/paquetes", form, ct), ct)).GetProperty("id").GetGuid();
        api.Ocr.Recibidos.Clear();
        await api.ProcesarAsync();

        var docs = await DocumentosAsync(paquete);
        (docs["una.tif"].TipoArchivo, docs["dos.tiff"].TipoArchivo).Should().Be(("IMAGE", "PDF"));
        docs.Values.Should().OnlyContain(d => d.ArchivoOriginalId != null && d.ArchivoOriginalId != d.ArchivoId);
        var tipos = await api.EnDbAsync(db => db.Archivos.Where(a => docs.Values.Select(d => d.ArchivoId).Contains(a.Id)).Select(a => a.ContentType).ToListAsync(ct));
        tipos.Should().BeEquivalentTo("image/png", "application/pdf");
        var originales = await api.EnDbAsync(db => db.Archivos.Where(a => docs.Values.Select(d => d.ArchivoOriginalId).Contains(a.Id)).Select(a => a.ContentType).ToListAsync(ct));
        originales.Should().OnlyContain(t => t == "image/tiff");
        api.Ocr.Recibidos.Select(r => r.ContentType).Should().BeEquivalentTo("image/png", "application/pdf");
        api.Ocr.Recibidos.Single(r => r.ContentType == "image/png").Contenido.Take(4).Should().Equal(0x89, 0x50, 0x4E, 0x47);

        var malo = new MultipartFormDataContent { { new StringContent("Roto"), "nombre" } };
        malo.Add(new ByteArrayContent(Encoding.UTF8.GetBytes("no es un tiff")), "archivos", "roto.tif");
        var r = await cliente.PostAsync("/api/v1/comparendos/ocr/paquetes", malo, ct);
        r.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await r.Content.ReadAsStringAsync(ct)).Should().Contain("roto.tif");
    }
}
