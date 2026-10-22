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
        if (Guion.TryGetValue($"{prompt}::{clave}", out var porPrompt) && porPrompt.Count > 0)
            clave = $"{prompt}::{clave}";
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

    /// <summary>Corre las evaluaciones de prompt pendientes.</summary>
    public async Task<int> EvaluarAsync()
    {
        var evaluador = App.Services.GetRequiredService<EvaluadorPromptOcr>();
        var n = 0;
        while (await evaluador.EvaluarSiguienteAsync(CancellationToken.None))
            n++;
        return n;
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
        // Solo las llamadas de este documento: la cola es compartida y puede traer reintentos vencidos de otras pruebas.
        int Llamadas() => api.Ocr.Recibidos.Count(r => Encoding.UTF8.GetString(r.Contenido) == "vacio");
        var antes = Llamadas();
        await api.ProcesarAsync();
        Llamadas().Should().Be(antes + 1);
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

        // Sin evaluarla contra la publicada no se publica (AC6 cubre la evaluación).
        var sinEvaluar = await super.PostAsync($"/api/v1/comparendos/admin/ocr/prompts/{nueva}/publicar", null, ct);
        sinEvaluar.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await sinEvaluar.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("code").GetString().Should().Be("EVALUACION_REQUERIDA");
        (await api.EnDbAsync(db => db.PromptsOcr.Where(p => p.Publicada).Select(p => p.Id).ToListAsync(ct))).Should().Equal(v1.GetProperty("id").GetGuid());
        (await api.Cliente(new UsuarioDePrueba(Guid.NewGuid(), Operador)).GetAsync("/api/v1/comparendos/admin/ocr/prompts", ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task AC6_PublicarExigeEvaluacionAprobada_YSePuedeVolverALaAnterior()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var (_, cliente) = await EmpresaOcrAsync();
        var super = api.Cliente(new UsuarioDePrueba(Guid.NewGuid(), [], SuperAdmin: true));
        const string Base = "/api/v1/comparendos/admin/ocr/prompts";
        var v1 = (await Json(await super.GetAsync(Base, ct), ct)).EnumerateArray().Single(p => p.GetProperty("publicada").GetBoolean()).GetProperty("id").GetGuid();

        async Task<Guid> CrearAsync(string texto) =>
            (await Json(await super.PostAsJsonAsync(Base, new { texto, proveedor = "ANTHROPIC", modelo = "m" }, ct), ct)).GetProperty("id").GetGuid();
        async Task<JsonElement> VersionAsync(Guid id) =>
            (await Json(await super.GetAsync(Base, ct), ct)).EnumerateArray().Single(p => p.GetProperty("id").GetGuid() == id);
        async Task<string?> PublicarAsync(Guid id)
        {
            var r = await super.PostAsync($"{Base}/{id}/publicar", null, ct);
            return r.StatusCode == HttpStatusCode.NoContent ? null : (await r.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("code").GetString();
        }

        // Con menos de 5 documentos revisados no hay con qué comparar (las otras pruebas de la clase pueden haber registrado algunos).
        if (await api.EnDbAsync(db => db.DocumentosOcr.CountAsync(d => d.Estado == EstadoDocumentoOcr.Registrado && d.RevisadoEn != null, ct)) < 5)
        {
            var pronto = await CrearAsync("pronto");
            (await super.PostAsync($"{Base}/{pronto}/evaluar", null, ct)).StatusCode.Should().Be(HttpStatusCode.Accepted);
            await api.EvaluarAsync();
            var sinConjunto = (await VersionAsync(pronto)).GetProperty("evaluacion");
            (sinConjunto.GetProperty("estado").GetString(), sinConjunto.GetProperty("aprobada").GetBoolean()).Should().Be(("Fallida", false));
            sinConjunto.GetProperty("mensaje").GetString().Should().Contain("al menos 5");
        }

        // Cinco documentos que una persona revisó y registró: la verdad de la evaluación.
        var archivos = Enumerable.Range(1, 5).Select(i => ($"ev{i}.pdf", $"ev{i}")).ToArray();
        foreach (var (_, clave) in archivos)
        {
            api.Ocr.Responder(clave, () => OcrDePrueba.Campos($"EV-{clave}", placa: $"EVA{clave[^1]}0{clave[^1]}"));
            api.Ocr.Responder($"malo::{clave}", () => OcrDePrueba.Campos("OTRO-NUMERO", placa: "ZZZ999", valor: "1", nombre: "NADIE"));
        }

        var paquete = (await Json(await cliente.PostAsync("/api/v1/comparendos/ocr/paquetes", Paquete("Para evaluar", archivos), ct), ct)).GetProperty("id").GetGuid();
        await api.ProcesarAsync();
        (await Json(await cliente.PostAsync($"/api/v1/comparendos/ocr/paquetes/{paquete}/registrar-completos", null, ct), ct))
            .GetProperty("registrados").GetArrayLength().Should().Be(5);

        // Una versión que lee peor que la publicada no aprueba y no se publica.
        var malo = await CrearAsync("malo");
        (await super.PostAsync($"{Base}/{malo}/evaluar", null, ct)).StatusCode.Should().Be(HttpStatusCode.Accepted);
        (await super.PostAsync($"{Base}/{malo}/evaluar", null, ct)).StatusCode.Should().Be(HttpStatusCode.Conflict, "ya hay una en curso");
        (await PublicarAsync(malo)).Should().Be("EVALUACION_REQUERIDA", "la evaluación sigue en curso");
        (await api.EvaluarAsync()).Should().Be(1);
        var evMalo = (await VersionAsync(malo)).GetProperty("evaluacion");
        (evMalo.GetProperty("estado").GetString(), evMalo.GetProperty("aprobada").GetBoolean()).Should().Be(("Terminada", false), evMalo.GetProperty("mensaje").GetString());
        evMalo.GetProperty("porcentajeCandidata").GetDecimal().Should().BeLessThan(evMalo.GetProperty("porcentajeVigente").GetDecimal());
        (await PublicarAsync(malo)).Should().Be("EVALUACION_REQUERIDA");

        var detalle = await Json(await super.GetAsync($"{Base}/evaluaciones/{evMalo.GetProperty("id").GetGuid()}", ct), ct);
        var ev1 = detalle.GetProperty("detalle").EnumerateArray().Single(d => d.GetProperty("archivo").GetString() == "ev1.pdf");
        var numero = ev1.GetProperty("campos").EnumerateArray().Single(c => c.GetProperty("campo").GetString() == CamposOcr.Numero);
        (numero.GetProperty("esperado").GetString(), numero.GetProperty("candidata").GetString(), numero.GetProperty("vigente").GetString())
            .Should().Be(("EV-ev1", "OTRO-NUMERO", "EV-ev1"));

        // Una que lee igual aprueba y se publica; la anterior queda retirada.
        var bueno = await CrearAsync("bueno");
        (await super.PostAsync($"{Base}/{bueno}/evaluar", null, ct)).StatusCode.Should().Be(HttpStatusCode.Accepted);
        await api.EvaluarAsync();
        (await VersionAsync(bueno)).GetProperty("puedePublicar").GetBoolean().Should().BeTrue();
        (await PublicarAsync(bueno)).Should().BeNull();
        (await VersionAsync(v1)).GetProperty("retiradaEn").ValueKind.Should().NotBe(JsonValueKind.Null);

        // Si se edita después de evaluarla, hay que evaluarla de nuevo.
        var tercera = await CrearAsync("tercera");
        (await super.PostAsync($"{Base}/{tercera}/evaluar", null, ct)).StatusCode.Should().Be(HttpStatusCode.Accepted);
        await api.EvaluarAsync();
        (await VersionAsync(tercera)).GetProperty("puedePublicar").GetBoolean().Should().BeTrue();
        api.Reloj.Ahora += TimeSpan.FromMinutes(1);
        (await super.PutAsJsonAsync($"{Base}/{tercera}", new { texto = "tercera editada", proveedor = "ANTHROPIC", modelo = "m" }, ct)).EnsureSuccessStatusCode();
        (await VersionAsync(tercera)).GetProperty("puedePublicar").GetBoolean().Should().BeFalse();
        (await PublicarAsync(tercera)).Should().Be("EVALUACION_REQUERIDA");

        // Volver a la anterior no exige evaluación, y la que ya estuvo publicada no se edita.
        (await PublicarAsync(v1)).Should().BeNull();
        (await api.EnDbAsync(db => db.AuditoriasConfiguracion.AnyAsync(a => a.EntidadId == v1.ToString() && a.Accion == "VOLVER_A_PUBLICAR", ct))).Should().BeTrue();
        (await super.PutAsJsonAsync($"{Base}/{bueno}", new { texto = "x", proveedor = "ANTHROPIC", modelo = "m" }, ct)).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await api.EnDbAsync(db => db.PromptsOcr.Where(p => p.Publicada).Select(p => p.Id).ToListAsync(ct))).Should().Equal(v1);
    }

    [Fact]
    public async Task AC7_AlRegistrar_QuedaAsociadoAlNitYALaPlacaMonitoreados()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var (empresa, cliente) = await EmpresaOcrAsync();
        var nit = Guid.NewGuid();
        var placa = Guid.NewGuid();
        await api.EnDbAsync(async db =>
        {
            db.IdentificacionesMonitoreadas.Add(new IdentificacionMonitoreada { Id = nit, TenantId = empresa, NumeroBase = "900123456", NombreTitular = "Titular" });
            db.PlacasMonitoreadas.Add(new PlacaMonitoreada { Id = placa, TenantId = empresa, Placa = "NIT777" });
            await db.SaveChangesAsync(ct);
        });

        // Uno con el NIT tal cual, otro con el dígito de verificación pegado, otro con un NIT que la empresa no monitorea.
        var dv = Flit.Comparendos.Domain.Identificadores.Nit.DigitoVerificacion("900123456");
        ResultadoOcr Con(string numero, string documento, string placaLeida)
        {
            var r = OcrDePrueba.Campos(numero, placa: placaLeida);
            var campos = r.Campos.ToDictionary(kv => kv.Key, kv => kv.Value);
            campos[CamposOcr.NumeroDocumento] = new(documento, 0.9m);
            return r with { Campos = campos };
        }

        api.Ocr.Responder("nit-tal-cual", () => Con("OCR-NIT-1", "900123456", "NIT777"));
        api.Ocr.Responder("nit-con-dv", () => Con("OCR-NIT-2", $"900123456{dv}", "ABC124"));
        api.Ocr.Responder("nit-ajeno", () => Con("OCR-NIT-3", "800999111", "ABC125"));
        var paquete = (await Json(await cliente.PostAsync("/api/v1/comparendos/ocr/paquetes",
            Paquete("NIT", ("a.pdf", "nit-tal-cual"), ("b.pdf", "nit-con-dv"), ("c.pdf", "nit-ajeno")), ct), ct)).GetProperty("id").GetGuid();
        await api.ProcesarAsync();
        (await Json(await cliente.PostAsync($"/api/v1/comparendos/ocr/paquetes/{paquete}/registrar-completos", null, ct), ct)).GetProperty("registrados").GetArrayLength().Should().Be(3);

        var c = await api.EnDbAsync(db => db.Comparendos.AsNoTracking().Where(x => x.TenantId == empresa && x.NumeroNormalizado.StartsWith("OCRNIT"))
            .ToDictionaryAsync(x => x.NumeroNormalizado, x => new { x.IdentificacionMonitoreadaId, x.NitConsultado, x.PlacaMonitoreadaId }, ct));
        c["OCRNIT1"].Should().Be(new { IdentificacionMonitoreadaId = (Guid?)nit, NitConsultado = (string?)"900123456", PlacaMonitoreadaId = (Guid?)placa });
        (c["OCRNIT2"].IdentificacionMonitoreadaId, c["OCRNIT2"].PlacaMonitoreadaId).Should().Be(((Guid?)nit, (Guid?)null));
        (c["OCRNIT3"].IdentificacionMonitoreadaId, c["OCRNIT3"].NitConsultado).Should().Be(((Guid?)null, (string?)null), "un NIT que la empresa no monitorea no se asocia");
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

    [Fact]
    public async Task HU13828_Reprocesar_RespetaLoCorregido_ConTope_YEnLote()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var (_, cliente) = await EmpresaOcrAsync();
        // Primera lectura sin valor ni nombre; la segunda los trae, pero con otra placa (que el operador ya había corregido).
        api.Ocr.Responder("rp", () => OcrDePrueba.Campos("OCR-RP", placa: "AAA111", valor: null, nombre: null), () => OcrDePrueba.Campos("OCR-RP", placa: "ZZZ999"));
        api.Ocr.Responder("rq", () => OcrDePrueba.Campos("OCR-RQ", valor: null));
        api.Ocr.Responder("rv", () => new ResultadoOcr(new Dictionary<string, CampoExtraido> { [CamposOcr.Numero] = new(null, null) }, "{}", "m"));
        var paquete = (await Json(await cliente.PostAsync("/api/v1/comparendos/ocr/paquetes", Paquete("Reprocesos", ("rp.jpg", "rp"), ("rq.jpg", "rq"), ("rv.jpg", "rv")), ct), ct)).GetProperty("id").GetGuid();
        await api.ProcesarAsync();
        var docs = await DocumentosAsync(paquete);
        var rp = docs["rp.jpg"];
        rp.Estado.Should().Be(EstadoDocumentoOcr.Incompleto);
        (await cliente.PutAsJsonAsync($"/api/v1/comparendos/ocr/documentos/{rp.Id}/campos",
            new { campos = new Dictionary<string, string?> { [CamposOcr.Placa] = "BBB222" }, motivo = "Leída del original" }, ct)).EnsureSuccessStatusCode();

        // AC1 + AC3: vuelve a la cola, se lee con la versión publicada y la placa corregida a mano se queda.
        (await cliente.PostAsync($"/api/v1/comparendos/ocr/documentos/{rp.Id}/reprocesar", null, ct)).StatusCode.Should().Be(HttpStatusCode.Accepted);
        (await api.EnDbAsync(db => db.PaquetesOcr.SingleAsync(p => p.Id == paquete, ct))).Estado.Should().Be(EstadoPaqueteOcr.EnProcesamiento);
        await api.ProcesarAsync();
        rp = (await DocumentosAsync(paquete))["rp.jpg"];
        var publicada = await api.EnDbAsync(db => db.PromptsOcr.SingleAsync(p => p.Publicada, ct));
        (rp.Estado, rp.Reprocesos, rp.PromptVersionId).Should().Be((EstadoDocumentoOcr.Completo, (short)1, publicada.Id));
        var campos = await api.EnDbAsync(db => db.DocumentoOcrCampos.AsNoTracking().Where(c => c.DocumentoOcrId == rp.Id).ToDictionaryAsync(c => c.NombreCampo, ct));
        (campos[CamposOcr.Placa].ValorExtraido, campos[CamposOcr.Placa].ValorFinal).Should().Be(("ZZZ999", "BBB222"), "lo corregido a mano no se pisa");
        campos[CamposOcr.Valor].ValorFinal.Should().Be("604100", "lo que nadie tocó toma la nueva lectura");

        // AC5: el detalle dice con qué versión se leyó y cuántas veces se reprocesó; el historial registra el pedido.
        // La versión del prompt solo la ve el SuperAdmin: para la gestora la lectura es transparente.
        var detalle = await Json(await cliente.GetAsync($"/api/v1/comparendos/ocr/documentos/{rp.Id}", ct), ct);
        (detalle.GetProperty("versionPrompt").ValueKind, detalle.GetProperty("reprocesos").GetInt32()).Should().Be((JsonValueKind.Null, 1));
        detalle.GetProperty("historialIntentos").EnumerateArray().Should().Contain(i => i.GetProperty("resultado").GetString() == "REPROCESO"
            && i.GetProperty("mensajeError").GetString()!.StartsWith("Reproceso 1 de 3", StringComparison.Ordinal));
        var flit = await Json(await api.Cliente(new UsuarioDePrueba(Guid.NewGuid(), [], SuperAdmin: true)).GetAsync($"/api/v1/comparendos/ocr/documentos/{rp.Id}", ct), ct);
        flit.GetProperty("versionPrompt").GetInt32().Should().Be(publicada.NumeroVersion);

        // AC4: un completo no se reprocesa, y después del tercer reproceso tampoco.
        (await cliente.PostAsync($"/api/v1/comparendos/ocr/documentos/{rp.Id}/reprocesar", null, ct)).StatusCode.Should().Be(HttpStatusCode.Conflict);
        var rv = docs["rv.jpg"];
        rv.Estado.Should().Be(EstadoDocumentoOcr.Fallido, "ilegible");
        for (var i = 1; i <= ServicioOcr.MaximoReprocesos; i++)
        {
            (await cliente.PostAsync($"/api/v1/comparendos/ocr/documentos/{rv.Id}/reprocesar", null, ct)).StatusCode.Should().Be(HttpStatusCode.Accepted);
            await api.ProcesarAsync();
        }

        var tope = await cliente.PostAsync($"/api/v1/comparendos/ocr/documentos/{rv.Id}/reprocesar", null, ct);
        tope.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await tope.Content.ReadAsStringAsync(ct)).Should().Contain("Ya se reprocesó 3 veces");

        // AC2: en lote se encolan los incompletos que no agotaron sus reprocesos.
        var lote = await Json(await cliente.PostAsync($"/api/v1/comparendos/ocr/paquetes/{paquete}/reprocesar-incompletos", null, ct), ct);
        (lote.GetProperty("encolados").GetInt32(), lote.GetProperty("omitidos").GetInt32()).Should().Be((1, 0));
        (await DocumentosAsync(paquete))["rq.jpg"].Estado.Should().Be(EstadoDocumentoOcr.Pendiente);
        await api.ProcesarAsync();
    }

    [Fact]
    public async Task HU13829_NumeroDe19o21Digitos_SeMarcaParaVerificar_SinBloquear()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var (_, cliente) = await EmpresaOcrAsync();
        api.Ocr.Responder("n19", () => OcrDePrueba.Campos("7600100000005652501"));
        api.Ocr.Responder("n20", () => OcrDePrueba.Campos("76001000000056525017"));
        var paquete = (await Json(await cliente.PostAsync("/api/v1/comparendos/ocr/paquetes", Paquete("Números", ("n19.jpg", "n19"), ("n20.jpg", "n20")), ct), ct)).GetProperty("id").GetGuid();
        await api.ProcesarAsync();

        var tabla = await Json(await cliente.GetAsync($"/api/v1/comparendos/ocr/paquetes/{paquete}", ct), ct);
        var avisos = tabla.GetProperty("documentos").EnumerateArray().ToDictionary(d => d.GetProperty("nombreArchivo").GetString()!, d => d.GetProperty("avisoNumero"));
        avisos["n19.jpg"].GetString().Should().Contain("19 dígitos");
        avisos["n20.jpg"].ValueKind.Should().Be(JsonValueKind.Null);

        var n19 = (await DocumentosAsync(paquete))["n19.jpg"];
        (await Json(await cliente.GetAsync($"/api/v1/comparendos/ocr/documentos/{n19.Id}", ct), ct)).GetProperty("avisoNumero").GetString().Should().Contain("20");
        (await cliente.PostAsync($"/api/v1/comparendos/ocr/documentos/{n19.Id}/registrar", null, ct)).EnsureSuccessStatusCode();
    }
}

/// <summary>La evaluación del prompt no castiga diferencias de formato que el registro ignora.</summary>
public sealed class CalificacionOcrTests
{
    [Theory]
    [InlineData(CamposOcr.Numero, "D05001000000054280231", "D 0500-1000.000054280231", true)]
    [InlineData(CamposOcr.Placa, "ABC123", "abc-123", true)]
    [InlineData(CamposOcr.NumeroDocumento, "900123456", "900.123.456", true)]
    [InlineData(CamposOcr.Valor, "604100", "$604.100", true)]
    [InlineData(CamposOcr.FechaInfraccion, "2026-09-28", "28/09/2026", true)]
    [InlineData(CamposOcr.TipoDocumento, "NIT", "nit", true)]
    [InlineData(CamposOcr.Nombre, "JOSÉ PÉREZ GÓMEZ", "Jose Perez Gomes", true)]
    [InlineData(CamposOcr.Secretaria, "SECRETARIA DE MOVILIDAD DE MEDELLIN", "SECRETARIA DE TRANSITO DE CALI", false)]
    [InlineData(CamposOcr.Valor, "604100", "302050", false)]
    [InlineData(CamposOcr.Placa, null, "ABC123", false)]
    [InlineData(CamposOcr.Placa, "ABC123", null, false)]
    [InlineData(CamposOcr.Placa, null, "null", true)]
    public void Acierta(string campo, string? esperado, string? obtenido, bool acierta) =>
        CalificacionOcr.Acierta(campo, esperado, obtenido).Should().Be(acierta);

    [Fact]
    public void Aprueba_ConMargenDeDosPuntos()
    {
        CalificacionOcr.Aprueba(89, 90, 100).Should().BeTrue();
        CalificacionOcr.Aprueba(88, 90, 100).Should().BeTrue();
        CalificacionOcr.Aprueba(87, 90, 100).Should().BeFalse();
    }
}
