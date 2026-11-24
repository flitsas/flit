using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Flit.Comparendos.Api.Modulos.Avisos;
using Flit.Comparendos.Api.Modulos.Ocr;
using Flit.Comparendos.Api.Modulos.Trabajos;
using Flit.Comparendos.Api.Persistence.Modelo;
using Flit.Comparendos.Api.Plataforma;
using Flit.Comparendos.Domain.Modelo;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static Flit.Comparendos.Tests.Api.ComparendosDePrueba;

namespace Flit.Comparendos.Tests.Api;

/// <summary>GDC-ASOC: asociar el documento escaneado de un comparendo que ya existe (antes quedaba en Duplicado, estado final).</summary>
public sealed class OcrAsociarTests(ApiConOcr api) : IClassFixture<ApiConOcr>
{
    private const string Base = "/api/v1/comparendos/ocr";
    private static readonly string[] Operador = [Permisos.OcrVer, Permisos.OcrGestionar, Permisos.BandejaVer, Permisos.ContraventorAsignar, Permisos.AvisosEnviar];

    private async Task<(Guid Empresa, HttpClient Cliente, Guid ComparendoId)> ComparendoAsync(string numero, bool fotomulta = false, int dia = 9)
    {
        api.Reloj.Hoy(new DateOnly(2026, 10, dia));
        var empresa = await EmpresaAsync(api, ocr: true);
        var ids = await IngresarAsync(api, empresa, Item(numero, fotomulta: fotomulta));
        return (empresa, api.Cliente(new UsuarioDePrueba(empresa, Operador)), ids.Single().Value);
    }

    /// <summary>Sube un archivo con ese contenido que el proveedor lee como <paramref name="numero"/> y devuelve su documento.</summary>
    private async Task<(Guid Paquete, DocumentoOcr Doc)> SubirAsync(HttpClient cliente, string numero, string contenido)
    {
        var ct = TestContext.Current.CancellationToken;
        api.Ocr.Responder(contenido, () => OcrDePrueba.Campos(numero));
        var form = new MultipartFormDataContent { { new StringContent($"Lote {contenido}"), "nombre" }, { new StringContent("true"), "iniciar" } };
        form.Add(new ByteArrayContent(System.Text.Encoding.UTF8.GetBytes(contenido)), "archivos", $"{contenido}.pdf");
        var r = await cliente.PostAsync($"{Base}/paquetes", form, ct);
        r.IsSuccessStatusCode.Should().BeTrue(await r.Content.ReadAsStringAsync(ct));
        var paquete = (await r.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetGuid();
        while (await api.ProcesarAsync() > 0)
        {
        }

        return (paquete, await api.EnDbAsync(db => db.DocumentosOcr.AsNoTracking().SingleAsync(d => d.PaqueteId == paquete, ct)));
    }

    private static Task<HttpResponseMessage> Asociar(HttpClient cliente, Guid doc, bool reemplazar, Guid? vista = null) =>
        cliente.PostAsJsonAsync($"{Base}/documentos/{doc}/asociar", new { reemplazar, evidenciaActualId = vista }, TestContext.Current.CancellationToken);

    private static async Task<string?> CodigoAsync(HttpResponseMessage r) =>
        (await r.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken)).GetProperty("code").GetString();

    private Task<Comparendo> ComparendoDbAsync(Guid id) => api.EnDbAsync(db => db.Comparendos.AsNoTracking().SingleAsync(c => c.Id == id));

    [Fact]
    public async Task Asociar_ElDuplicadoDeUnComparendoDeLaFuente_DejaElDocumentoEnElComparendo_YCierraElPaquete()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var (_, cliente, comparendo) = await ComparendoAsync("ASO-1");
        var (paquete, doc) = await SubirAsync(cliente, "ASO-1", "asoc-feliz");
        doc.Estado.Should().Be(EstadoDocumentoOcr.Duplicado);

        var antes = (await cliente.GetFromJsonAsync<JsonElement>($"{Base}/documentos/{doc.Id}", ct)).GetProperty("duplicado");
        (antes.GetProperty("asociable").GetBoolean(), antes.GetProperty("motivoNoAsociable").ValueKind, antes.GetProperty("evidenciaActual").ValueKind)
            .Should().Be((true, JsonValueKind.Null, JsonValueKind.Null));

        var r = await Asociar(cliente, doc.Id, reemplazar: false);
        r.StatusCode.Should().Be(HttpStatusCode.OK, await r.Content.ReadAsStringAsync(ct));
        var cuerpo = await r.Content.ReadFromJsonAsync<JsonElement>(ct);
        (cuerpo.GetProperty("comparendoId").GetGuid(), cuerpo.GetProperty("numero").GetString(), cuerpo.GetProperty("estado").GetString(), cuerpo.GetProperty("reemplazo").GetBoolean())
            .Should().Be((comparendo, "ASO-1", "Asociado", false));

        (await ComparendoDbAsync(comparendo)).ArchivoEvidenciaId.Should().Be(doc.ArchivoId);
        var guardado = await api.EnDbAsync(db => db.DocumentosOcr.AsNoTracking().SingleAsync(d => d.Id == doc.Id, ct));
        (guardado.Estado, guardado.ComparendoId, guardado.RevisadoPor).Should().Be((EstadoDocumentoOcr.Asociado, comparendo, guardado.RevisadoPor));
        guardado.RevisadoPor.Should().NotBeNull();
        var p = await api.EnDbAsync(db => db.PaquetesOcr.AsNoTracking().SingleAsync(x => x.Id == paquete, ct));
        (p.Estado, p.CantidadAsociados, p.CantidadDuplicados, p.CerradoEn is not null).Should().Be((EstadoPaqueteOcr.Cerrado, 1, 0, true));
        var evento = await api.EnDbAsync(db => db.Timeline.AsNoTracking().SingleAsync(t => t.ComparendoId == comparendo && t.Tipo == TimelineTipo, ct));
        (evento.RelacionadoTipo, evento.RelacionadoId).Should().Be(("documento_ocr", doc.Id));
        evento.Descripcion.Should().StartWith("Se asoció el documento escaneado").And.NotContainAny("OCR", "API", "LLM");
        var detalle = await cliente.GetFromJsonAsync<JsonElement>($"{Base}/paquetes/{paquete}", ct);
        detalle.GetProperty("paquete").GetProperty("cantidadAsociados").GetInt32().Should().Be(1);
    }

    private const TipoEventoTimeline TimelineTipo = TipoEventoTimeline.DocumentoAsociadoOcr;

    [Fact]
    public async Task Asociar_ConDocumentoPrevio_ExigeDecidir_Reemplaza_YElAnteriorQuedaEnElTimeline()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var (_, cliente, comparendo) = await ComparendoAsync("ASO-2");
        var (_, primero) = await SubirAsync(cliente, "ASO-2", "asoc-previo-1");
        (await Asociar(cliente, primero.Id, reemplazar: false)).EnsureSuccessStatusCode();
        var (paquete, segundo) = await SubirAsync(cliente, "ASO-2", "asoc-previo-2");

        var lectura = (await cliente.GetFromJsonAsync<JsonElement>($"{Base}/documentos/{segundo.Id}", ct)).GetProperty("duplicado").GetProperty("evidenciaActual");
        lectura.GetProperty("archivoId").GetGuid().Should().Be(primero.ArchivoId);
        lectura.GetProperty("nombre").GetString().Should().Be("asoc-previo-1.pdf");
        (lectura.TryGetProperty("contentType", out _), lectura.TryGetProperty("tamanoBytes", out _), lectura.TryGetProperty("cargadaEn", out _)).Should().Be((true, true, true));

        var sinDecidir = await Asociar(cliente, segundo.Id, reemplazar: false, vista: primero.ArchivoId);
        sinDecidir.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await CodigoAsync(sinDecidir)).Should().Be("EVIDENCIA_EXISTENTE");
        var sinVer = await Asociar(cliente, segundo.Id, reemplazar: true, vista: null);
        sinVer.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await CodigoAsync(sinVer)).Should().Be("EVIDENCIA_CAMBIO");
        (await ComparendoDbAsync(comparendo)).ArchivoEvidenciaId.Should().Be(primero.ArchivoId, "ningún rechazo escribe");

        var r = await Asociar(cliente, segundo.Id, reemplazar: true, vista: primero.ArchivoId);
        r.StatusCode.Should().Be(HttpStatusCode.OK, await r.Content.ReadAsStringAsync(ct));
        (await r.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("reemplazo").GetBoolean().Should().BeTrue();
        (await ComparendoDbAsync(comparendo)).ArchivoEvidenciaId.Should().Be(segundo.ArchivoId);
        var eventos = await api.EnDbAsync(db => db.Timeline.AsNoTracking().Where(t => t.ComparendoId == comparendo && t.Tipo == TimelineTipo).OrderBy(t => t.Id).ToListAsync(ct));
        eventos.Should().HaveCount(2);
        eventos[1].Descripcion.Should().StartWith("Se reemplazó el documento");
        using var datos = JsonDocument.Parse(eventos[1].Datos!);
        (datos.RootElement.GetProperty("anterior").GetGuid(), datos.RootElement.GetProperty("archivo").GetGuid()).Should().Be((primero.ArchivoId, segundo.ArchivoId));
        (await api.EnDbAsync(db => db.Archivos.AnyAsync(a => a.Id == primero.ArchivoId, ct))).Should().BeTrue("el documento anterior no se borra");
        (await api.EnDbAsync(db => db.PaquetesOcr.AsNoTracking().SingleAsync(x => x.Id == paquete, ct))).CantidadAsociados.Should().Be(1);
    }

    [Fact]
    public async Task Asociar_SiOtroUsuarioCambioElDocumento_RespondeEvidenciaCambio_SinEscribir()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var (_, cliente, comparendo) = await ComparendoAsync("ASO-3");
        var (_, doc) = await SubirAsync(cliente, "ASO-3", "asoc-carrera");
        var otro = Guid.NewGuid();
        await api.EnDbAsync(db => db.Comparendos.Where(c => c.Id == comparendo).ExecuteUpdateAsync(u => u.SetProperty(c => c.ArchivoEvidenciaId, otro)));

        var r = await Asociar(cliente, doc.Id, reemplazar: true, vista: null);

        r.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await CodigoAsync(r)).Should().Be("EVIDENCIA_CAMBIO");
        (await ComparendoDbAsync(comparendo)).ArchivoEvidenciaId.Should().Be(otro);
        (await api.EnDbAsync(db => db.DocumentosOcr.AsNoTracking().SingleAsync(d => d.Id == doc.Id))).Estado.Should().Be(EstadoDocumentoOcr.Duplicado);
    }

    [Fact]
    public async Task Asociar_NoAplicaAlMismoArchivoNiAlChoqueContraOtroDocumento_YElDuplicadoSigueDescartable()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        api.Reloj.Hoy(new DateOnly(2026, 10, 9));
        var empresa = await EmpresaAsync(api, ocr: true);
        var cliente = api.Cliente(new UsuarioDePrueba(empresa, Operador));
        var (_, original) = await SubirAsync(cliente, "ASO-4", "asoc-original");
        original.Estado.Should().Be(EstadoDocumentoOcr.Completo);
        api.Ocr.Responder("asoc-otro", () => OcrDePrueba.Campos("ASO-4"));
        var (_, mismoNumero) = await SubirAsync(cliente, "ASO-4", "asoc-otro");
        var (_, mismoArchivo) = await SubirAsync(cliente, "ASO-4", "asoc-original");
        (mismoNumero.FuenteDuplicado, mismoArchivo.FuenteDuplicado).Should().Be(("OCR", "ARCHIVO"));

        foreach (var doc in new[] { mismoNumero, mismoArchivo })
        {
            var r = await Asociar(cliente, doc.Id, reemplazar: true);
            r.StatusCode.Should().Be(HttpStatusCode.Conflict, doc.FuenteDuplicado);
            (await CodigoAsync(r)).Should().Be("NO_ASOCIABLE");
            var duplicado = (await cliente.GetFromJsonAsync<JsonElement>($"{Base}/documentos/{doc.Id}", ct)).GetProperty("duplicado");
            duplicado.GetProperty("asociable").GetBoolean().Should().BeFalse();
            duplicado.GetProperty("motivoNoAsociable").GetString().Should().NotBeNullOrWhiteSpace();
        }

        (await Asociar(cliente, original.Id, reemplazar: true)).StatusCode.Should().Be(HttpStatusCode.Conflict, "un documento completo no es duplicado de ningún comparendo");
    }

    [Fact]
    public async Task Asociar_OtraEmpresa_Responde404_YSinPermisoDeGestionar_403()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var (_, cliente, comparendo) = await ComparendoAsync("ASO-5");
        var (_, doc) = await SubirAsync(cliente, "ASO-5", "asoc-tenant");
        var otraEmpresa = await EmpresaAsync(api, ocr: true);

        (await Asociar(api.Cliente(new UsuarioDePrueba(otraEmpresa, Operador)), doc.Id, reemplazar: true)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        var soloVer = api.Cliente(new UsuarioDePrueba(doc.TenantId, [Permisos.OcrVer]));
        (await Asociar(soloVer, doc.Id, reemplazar: true)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ComparendoDbAsync(comparendo)).ArchivoEvidenciaId.Should().BeNull();
        (await api.EnDbAsync(db => db.DocumentosOcr.AsNoTracking().SingleAsync(d => d.Id == doc.Id))).Estado.Should().Be(EstadoDocumentoOcr.Duplicado);
    }

    [Fact]
    public async Task Asociar_DosVeces_EsIdempotente()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var (_, cliente, comparendo) = await ComparendoAsync("ASO-6");
        var (paquete, doc) = await SubirAsync(cliente, "ASO-6", "asoc-idem");

        (await Asociar(cliente, doc.Id, reemplazar: false)).EnsureSuccessStatusCode();
        var otraVez = await Asociar(cliente, doc.Id, reemplazar: false);

        otraVez.StatusCode.Should().Be(HttpStatusCode.OK);
        (await api.EnDbAsync(db => db.Timeline.CountAsync(t => t.ComparendoId == comparendo && t.Tipo == TimelineTipo))).Should().Be(1);
        (await api.EnDbAsync(db => db.PaquetesOcr.AsNoTracking().SingleAsync(x => x.Id == paquete))).CantidadAsociados.Should().Be(1);
    }

    [Fact]
    public async Task Asociar_FuncionaConUnComparendoPagadoOInactivo_YUnAsociadoNoSeDescarta()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var (_, cliente, comparendo) = await ComparendoAsync("ASO-7");
        await api.EnDbAsync(db => db.Comparendos.Where(c => c.Id == comparendo).ExecuteUpdateAsync(u => u
            .SetProperty(c => c.Pagado, true).SetProperty(c => c.Activo, false).SetProperty(c => c.MotivoInactivacion, MotivoInactivacion.InactivadoOperador)));
        var (_, doc) = await SubirAsync(cliente, "ASO-7", "asoc-inactivo");
        var duplicado = (await cliente.GetFromJsonAsync<JsonElement>($"{Base}/documentos/{doc.Id}", TestContext.Current.CancellationToken)).GetProperty("duplicado");
        (duplicado.GetProperty("activo").GetBoolean(), duplicado.GetProperty("pagado").GetBoolean(), duplicado.GetProperty("asociable").GetBoolean()).Should().Be((false, true, true));

        (await Asociar(cliente, doc.Id, reemplazar: false)).StatusCode.Should().Be(HttpStatusCode.OK);

        (await ComparendoDbAsync(comparendo)).ArchivoEvidenciaId.Should().Be(doc.ArchivoId);
        var (_, repetido) = await SubirAsync(cliente, "ASO-7", "asoc-inactivo");
        repetido.FuenteDuplicado.Should().Be("ARCHIVO", "el mismo archivo ya asociado no se vuelve a cargar");
        (await cliente.PostAsJsonAsync($"{Base}/documentos/{doc.Id}/descartar", new { motivo = "x" }, TestContext.Current.CancellationToken)).StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    /// <summary>La razón de la HU: un comparendo físico que llegó por la fuente y recibió el Aviso 1 (50 %, hasta el 5-oct) sin soporte lleva el documento en el Aviso 2 (25 %).</summary>
    [Fact]
    public async Task ElAviso2_DeUnComparendoFisico_LlevaElDocumentoAsociado()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var (empresa, cliente, comparendo) = await ComparendoAsync("ASO-8", fotomulta: false, dia: 2);
        (await cliente.PutAsJsonAsync($"/api/v1/comparendos/comparendos/{comparendo}/contraventor", new
        {
            tipoDocumento = "CC", numeroDocumento = "1020304050", nombres = "Juan Pérez", correo = "juan@example.test", declaracionTratamientoDatos = true,
        }, ct)).EnsureSuccessStatusCode();
        await SchedulerAsync(empresa);
        await SchedulerAsync(empresa);
        async Task<List<string>> Adjuntos(TipoComunicacion tipo) => await api.EnDbAsync(db =>
            (from c in db.Comunicaciones where c.ComparendoId == comparendo && c.Tipo == tipo join a in db.ComunicacionAdjuntos on c.Id equals a.ComunicacionId select a.Nombre).ToListAsync(ct));
        (await Adjuntos(TipoComunicacion.Aviso1)).Should().NotContain(n => n.StartsWith("comparendo-", StringComparison.Ordinal));
        (await api.EnDbAsync(db => db.Timeline.Where(t => t.ComparendoId == comparendo && t.Tipo == TipoEventoTimeline.AvisoEnviado).Select(t => t.Descripcion).SingleAsync(ct)))
            .Should().Contain("el documento del comparendo");

        var (_, doc) = await SubirAsync(cliente, "ASO-8", "asoc-aviso");
        (await Asociar(cliente, doc.Id, reemplazar: false)).EnsureSuccessStatusCode();
        api.Reloj.Hoy(new DateOnly(2026, 10, 6));
        await SchedulerAsync(empresa);
        await SchedulerAsync(empresa);

        (await api.EnDbAsync(db => db.AvisosConductor.Where(a => a.ComparendoId == comparendo).Select(a => a.TipoAviso).ToListAsync(ct))).Should().Contain(TipoAviso.Aviso2);
        (await Adjuntos(TipoComunicacion.Aviso2)).Should().Contain("comparendo-ASO-8.pdf");
    }

    [Fact]
    public async Task ElPaquete_SeCierra_CuandoElUltimoPendienteSeRegistra_AunConUnDocumentoAsociado()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var (_, cliente, _) = await ComparendoAsync("ASO-9");
        api.Ocr.Responder("asoc-lote-dup", () => OcrDePrueba.Campos("ASO-9"));
        api.Ocr.Responder("asoc-lote-nuevo", () => OcrDePrueba.Campos("ASO-9B", valor: null));
        var form = new MultipartFormDataContent { { new StringContent("Lote mixto"), "nombre" }, { new StringContent("true"), "iniciar" } };
        form.Add(new ByteArrayContent(System.Text.Encoding.UTF8.GetBytes("asoc-lote-dup")), "archivos", "dup.pdf");
        form.Add(new ByteArrayContent(System.Text.Encoding.UTF8.GetBytes("asoc-lote-nuevo")), "archivos", "nuevo.pdf");
        var paquete = (await (await cliente.PostAsync($"{Base}/paquetes", form, ct)).Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetGuid();
        while (await api.ProcesarAsync() > 0)
        {
        }

        var docs = await api.EnDbAsync(db => db.DocumentosOcr.AsNoTracking().Where(d => d.PaqueteId == paquete).ToDictionaryAsync(d => d.NombreArchivo, ct));
        (await Asociar(cliente, docs["dup.pdf"].Id, reemplazar: false)).EnsureSuccessStatusCode();
        (await api.EnDbAsync(db => db.PaquetesOcr.AsNoTracking().SingleAsync(p => p.Id == paquete, ct))).Estado.Should().NotBe(EstadoPaqueteOcr.Cerrado, "falta registrar un documento");

        (await cliente.PutAsJsonAsync($"{Base}/documentos/{docs["nuevo.pdf"].Id}/campos", new { campos = new Dictionary<string, string?> { [CamposOcr.Valor] = "302.050" } }, ct)).EnsureSuccessStatusCode();
        (await cliente.PostAsync($"{Base}/documentos/{docs["nuevo.pdf"].Id}/registrar", null, ct)).EnsureSuccessStatusCode();

        var final = await api.EnDbAsync(db => db.PaquetesOcr.AsNoTracking().SingleAsync(p => p.Id == paquete, ct));
        (final.Estado, final.CantidadAsociados, final.CantidadRegistrados).Should().Be((EstadoPaqueteOcr.Cerrado, 1, 1));
    }

    [Fact]
    public async Task Asociar_SiElComparendoYaNoExiste_ReEvaluaElDocumento_Responde409_YGuardaElNuevoEstado()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var (_, cliente, comparendo) = await ComparendoAsync("ASO-10");
        var (_, doc) = await SubirAsync(cliente, "ASO-10", "asoc-reevaluar");
        doc.Estado.Should().Be(EstadoDocumentoOcr.Duplicado);
        await api.EnDbAsync(db => db.Comparendos.Where(c => c.Id == comparendo).ExecuteUpdateAsync(u => u.SetProperty(c => c.NumeroNormalizado, "OTRO")));

        var r = await Asociar(cliente, doc.Id, reemplazar: false);

        r.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await CodigoAsync(r)).Should().Be("NO_ASOCIABLE");
        var guardado = await api.EnDbAsync(db => db.DocumentosOcr.AsNoTracking().SingleAsync(d => d.Id == doc.Id));
        (guardado.Estado, guardado.DuplicadoDeComparendoId).Should().Be((EstadoDocumentoOcr.Completo, (Guid?)null));
        (await ComparendoDbAsync(comparendo)).ArchivoEvidenciaId.Should().BeNull();
    }

    private async Task SchedulerAsync(Guid empresa)
    {
        await using var scope = api.App.Services.CreateAsyncScope();
        var ejecutor = scope.ServiceProvider.GetServices<IEjecutorTrabajo>().OfType<SchedulerAvisos>().Single();
        await ejecutor.EjecutarAsync(new SolicitudTrabajo(TipoTrabajo.AvisosConductor, Disparo.Manual, empresa), CancellationToken.None);
    }
}
