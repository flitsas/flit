using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Flit.Comparendos.Api.Modulos.Comparendos;
using Flit.Comparendos.Api.Modulos.Ocr;
using Flit.Comparendos.Api.Persistence;
using Flit.Comparendos.Api.Persistence.Modelo;
using Flit.Comparendos.Api.Plataforma;
using Flit.Comparendos.Domain.Identificadores;
using Flit.Comparendos.Domain.Modelo;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static Flit.Comparendos.Tests.Api.ComparendosDePrueba;

namespace Flit.Comparendos.Tests.Api;

/// <summary>
/// GDC-EVID §9/§10: «Asociar los N» del paquete, en segundo plano. Entran los duplicados sin conflicto (sin documento previo y
/// número igual a la vista); los reemplazos y los números distintos quedan para revisar uno por uno. El POST encola (PorAsociar)
/// y el trabajador asocia de a uno con el correo de evidencia en cola. Las pruebas llaman al trabajador de forma directa.
/// </summary>
public sealed partial class OcrAsociarTests
{
    /// <summary>Un paquete con varios archivos; cada contenido se lee como su número. Devuelve el paquete y los documentos por contenido.</summary>
    private async Task<(Guid Paquete, Dictionary<string, Flit.Comparendos.Api.Persistence.Modelo.DocumentoOcr> Docs)> SubirVariosAsync(HttpClient cliente, params (string Numero, string Contenido)[] archivos)
    {
        var ct = TestContext.Current.CancellationToken;
        var form = new MultipartFormDataContent { { new StringContent("Lote varios"), "nombre" }, { new StringContent("true"), "iniciar" } };
        foreach (var (numero, contenido) in archivos)
        {
            api.Ocr.Responder(contenido, () => OcrDePrueba.Campos(numero));
            form.Add(new ByteArrayContent(System.Text.Encoding.UTF8.GetBytes(contenido)), "archivos", $"{contenido}.pdf");
        }

        var r = await cliente.PostAsync($"{Base}/paquetes", form, ct);
        r.IsSuccessStatusCode.Should().BeTrue(await r.Content.ReadAsStringAsync(ct));
        var paquete = (await r.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetGuid();
        while (await api.ProcesarAsync() > 0)
        {
        }

        var docs = await api.EnDbAsync(db => db.DocumentosOcr.AsNoTracking().Where(d => d.PaqueteId == paquete).ToListAsync(ct));
        return (paquete, docs.ToDictionary(d => Path.GetFileNameWithoutExtension(d.NombreArchivo)));
    }

    private async Task<(Guid Empresa, HttpClient Cliente, Dictionary<string, Guid> Ids)> EmpresaConAsync(params string[] numeros)
    {
        api.Reloj.Hoy(new DateOnly(2026, 10, 9));
        var empresa = await EmpresaAsync(api, ocr: true);
        var ids = await IngresarAsync(api, empresa, numeros.Select(n => Item(n)).ToArray());
        return (empresa, api.Cliente(new UsuarioDePrueba(empresa, Operador)), ids);
    }

    private static Task<JsonElement> PreviaAsync(HttpClient cliente, Guid paquete) =>
        cliente.GetFromJsonAsync<JsonElement>($"{Base}/paquetes/{paquete}/asociacion-lote", TestContext.Current.CancellationToken);

    private static async Task<JsonElement> AsociarLoteAsync(HttpClient cliente, Guid paquete)
    {
        var r = await cliente.PostAsJsonAsync($"{Base}/paquetes/{paquete}/asociar-lote", new { }, TestContext.Current.CancellationToken);
        r.StatusCode.Should().Be(HttpStatusCode.OK, await r.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        return await r.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
    }

    private Task<int> EvidenciasDeAsync(IEnumerable<Guid> comparendos)
    {
        var ids = comparendos.ToList();
        return api.EnDbAsync(db => db.Comunicaciones.CountAsync(c => c.ComparendoId != null && ids.Contains(c.ComparendoId.Value) && c.Tipo == TipoComunicacion.Evidencia));
    }

    /// <summary>Corre el trabajador de fondo hasta vaciar la cola (como ProcesarAsync del OCR).</summary>
    private async Task<int> ProcesarLoteAsync()
    {
        var trabajador = api.App.Services.GetRequiredService<ProcesadorAsociacionLote>();
        var n = 0;
        while (await trabajador.ProcesarSiguienteAsync(TestContext.Current.CancellationToken))
            n++;
        return n;
    }

    private Task<List<AsociacionLoteDocumento>> FilasDelLoteAsync(Guid lote) =>
        api.EnDbAsync(db => db.AsociacionLoteDocumentos.AsNoTracking().Where(i => i.LoteId == lote).ToListAsync());

    /// <summary>
    /// Paquete mixto: A (conductor con correo) y B (sin conductor) entran; C (ya tiene documento) va a la cola de reemplazos;
    /// D (mismo comparendo por la llave, pero el número a la vista difiere) va a revisión; E (nuevo) no es duplicado.
    /// </summary>
    [Fact]
    public async Task AsociarLote_PaqueteMixto_Encola_ElTrabajadorAsocia_ResumenYNingunCorreoDeMas()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var (_, cliente, ids) = await EmpresaConAsync("LOTEA1", "LOTEB1", "LOTEC1", "LOTED1");
        await ConConductorAsync(cliente, ids["LOTEA1"]);
        await api.EnDbAsync(db => db.Comparendos.Where(c => c.Id == ids["LOTEC1"]).ExecuteUpdateAsync(u => u.SetProperty(c => c.ArchivoEvidenciaId, Guid.NewGuid())));
        var (paquete, docs) = await SubirVariosAsync(cliente,
            ("LOTEA1", "lote-a"), ("LOTEB1", "lote-b"), ("LOTEC1", "lote-c"), ("LOTE/D1", "lote-d"), ("LOTEE1", "lote-e"));
        docs["lote-d"].Estado.Should().Be(EstadoDocumentoOcr.Duplicado, "«LOTE/D1» y «LOTED1» tienen la misma llave");

        var previa = await PreviaAsync(cliente, paquete);
        previa.GetProperty("asociables").GetInt32().Should().Be(2);
        var correos = previa.GetProperty("correos");
        (correos.GetProperty("saldran").GetInt32(), correos.GetProperty("conductores").GetInt32()).Should().Be((1, 1));
        correos.GetProperty("omitidos").EnumerateArray().Select(o => (o.GetProperty("motivo").GetString(), o.GetProperty("cantidad").GetInt32()))
            .Should().Equal(("No hay un conductor asignado", 1));
        var revisar = previa.GetProperty("porRevisar");
        (revisar.GetProperty("reemplazos").GetInt32(), revisar.GetProperty("numeroDistinto").GetInt32()).Should().Be((1, 1));
        previa.GetProperty("enCurso").ValueKind.Should().Be(JsonValueKind.Null, "nunca hubo un lote");
        var listado = (await cliente.GetFromJsonAsync<JsonElement>($"{Base}/paquetes/{paquete}", ct)).GetProperty("documentos").EnumerateArray()
            .ToDictionary(d => Path.GetFileNameWithoutExtension(d.GetProperty("nombreArchivo").GetString()!), d => d.GetProperty("revision").GetString());
        listado.Should().Equal(new Dictionary<string, string?> { ["lote-a"] = null, ["lote-b"] = null, ["lote-c"] = "reemplazo", ["lote-d"] = "numeroDistinto", ["lote-e"] = null });

        var r = await AsociarLoteAsync(cliente, paquete);

        r.GetProperty("encolados").GetInt32().Should().Be(2);
        var loteId = r.GetProperty("loteId").GetGuid();
        var estados = await api.EnDbAsync(db => db.DocumentosOcr.AsNoTracking().Where(d => d.PaqueteId == paquete).ToDictionaryAsync(d => Path.GetFileNameWithoutExtension(d.NombreArchivo), d => d.Estado, ct));
        (estados["lote-a"], estados["lote-b"], estados["lote-c"], estados["lote-d"], estados["lote-e"]).Should()
            .Be((EstadoDocumentoOcr.PorAsociar, EstadoDocumentoOcr.PorAsociar, EstadoDocumentoOcr.Duplicado, EstadoDocumentoOcr.Duplicado, EstadoDocumentoOcr.Completo));
        (await EvidenciasDeAsync(ids.Values)).Should().Be(0, "el POST no asocia ni envía: encola");
        var antes = (await PreviaAsync(cliente, paquete));
        antes.GetProperty("asociables").GetInt32().Should().Be(0);
        var enCurso = antes.GetProperty("enCurso");
        (enCurso.GetProperty("loteId").GetGuid(), enCurso.GetProperty("porAsociar").GetInt32(), enCurso.GetProperty("total").GetInt32(), enCurso.GetProperty("asociados").GetInt32())
            .Should().Be((loteId, 2, 2, 0));
        (enCurso.GetProperty("ultimoAvanceEn").GetDateTimeOffset(), enCurso.GetProperty("terminadoEn").ValueKind)
            .Should().Be((enCurso.GetProperty("solicitadoEn").GetDateTimeOffset(), JsonValueKind.Null), "sin avance aún, la fecha es la de la solicitud");

        api.Reloj.Hoy(new DateOnly(2026, 10, 10)); // el trabajador procesa al día siguiente: el avance debe moverse
        await ProcesarLoteAsync();

        (await ComparendoDbAsync(ids["LOTEA1"])).ArchivoEvidenciaId.Should().Be(docs["lote-a"].ArchivoId);
        (await ComparendoDbAsync(ids["LOTEB1"])).ArchivoEvidenciaId.Should().Be(docs["lote-b"].ArchivoId);
        (await EvidenciasAsync(ids["LOTEA1"])).Should().ContainSingle().Which.Estado.Should().Be(EstadoComunicacion.Pendiente, "en lote el correo queda en cola");
        await DebeTenerNotaAsync(ids["LOTEB1"], "No hay un conductor asignado");
        var asociacion = await api.EnDbAsync(db => db.Timeline.AsNoTracking().FirstAsync(t => t.ComparendoId == ids["LOTEA1"] && t.Tipo == TipoEventoTimeline.DocumentoAsociadoOcr, ct));
        asociacion.ActorUsuarioId.Should().NotBeNull("el trabajador actúa como quien pidió el lote");
        var fin = (await PreviaAsync(cliente, paquete)).GetProperty("enCurso");
        (fin.GetProperty("porAsociar").GetInt32(), fin.GetProperty("asociados").GetInt32(), fin.GetProperty("correosEnCamino").GetInt32(), fin.GetProperty("noAsociados").GetArrayLength())
            .Should().Be((0, 2, 1, 0));
        fin.GetProperty("sinCorreo").EnumerateArray().Select(x => (x.GetProperty("motivo").GetString(), x.GetProperty("cantidad").GetInt32())).Should().Equal(("No hay un conductor asignado", 1));
        fin.GetProperty("terminadoEn").ValueKind.Should().NotBe(JsonValueKind.Null);
        fin.GetProperty("ultimoAvanceEn").GetDateTimeOffset().Should().Be(api.Reloj.GetUtcNow(), "la fecha del último documento procesado");
        fin.GetProperty("ultimoAvanceEn").GetDateTimeOffset().Should().BeAfter(fin.GetProperty("solicitadoEn").GetDateTimeOffset());
        (await api.EnDbAsync(db => db.PaquetesOcr.AsNoTracking().SingleAsync(x => x.Id == paquete, ct))).CantidadPorAsociar.Should().Be(0);

        // Otra pulsación: nada que encolar, ningún correo de más.
        var otra = await AsociarLoteAsync(cliente, paquete);
        (otra.GetProperty("encolados").GetInt32(), otra.GetProperty("loteId").ValueKind).Should().Be((0, JsonValueKind.Null));
        await ProcesarLoteAsync();
        (await EvidenciasDeAsync(ids.Values)).Should().Be(1);
    }

    /// <summary>Un documento PorAsociar no se toca a mano: asociar, corregir, descartar y reprocesar responden 409 EN_ASOCIACION.</summary>
    [Fact]
    public async Task AsociarLote_UnDocumentoPorAsociar_NoSeEdita_409()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var (_, cliente, _) = await EmpresaConAsync("LOTEN1");
        var (paquete, docs) = await SubirVariosAsync(cliente, ("LOTEN1", "no-editable"));
        (await AsociarLoteAsync(cliente, paquete)).GetProperty("encolados").GetInt32().Should().Be(1);
        var id = docs["no-editable"].Id;

        foreach (var r in new[]
                 {
                     await Asociar(cliente, id, reemplazar: false),
                     await cliente.PutAsJsonAsync($"{Base}/documentos/{id}/campos", new { campos = new Dictionary<string, string?> { ["placa"] = "ZZZ999" } }, ct),
                     await cliente.PostAsJsonAsync($"{Base}/documentos/{id}/descartar", new { motivo = "x" }, ct),
                     await cliente.PostAsync($"{Base}/documentos/{id}/reprocesar", null, ct),
                 })
        {
            r.StatusCode.Should().Be(HttpStatusCode.Conflict, await r.Content.ReadAsStringAsync(ct));
            var cuerpo = await r.Content.ReadFromJsonAsync<JsonElement>(ct);
            (cuerpo.GetProperty("code").GetString(), cuerpo.GetProperty("error").GetString())
                .Should().Be(("EN_ASOCIACION", "Este documento se está asociando en segundo plano; espera a que termine."));
        }

        var detalle = (await cliente.GetFromJsonAsync<JsonElement>($"{Base}/documentos/{id}", ct)).GetProperty("duplicado");
        (detalle.GetProperty("asociable").GetBoolean(), detalle.GetProperty("motivoNoAsociable").GetString())
            .Should().Be((false, "Este documento se está asociando en segundo plano; espera a que termine."));
        await ProcesarLoteAsync(); // no dejar la cola sucia para las demás pruebas de la clase (comparten base)
    }

    /// <summary>Doble clic antes de que el trabajador empiece: la segunda pulsación encola 0 y no crea otro lote.</summary>
    [Fact]
    public async Task AsociarLote_DoblePost_ElSegundoEncolaCero()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var (empresa, cliente, ids) = await EmpresaConAsync("LOTEX1", "LOTEX2", "LOTEX3");
        foreach (var n in ids.Keys)
            await ConConductorAsync(cliente, ids[n]);
        var (paquete, _) = await SubirVariosAsync(cliente, ids.Keys.Select(n => (n, $"doble-{n}")).ToArray());
        var otro = api.Cliente(new UsuarioDePrueba(empresa, Operador));

        var resultados = await Task.WhenAll(AsociarLoteAsync(cliente, paquete), AsociarLoteAsync(otro, paquete));

        resultados.Sum(r => r.GetProperty("encolados").GetInt32()).Should().Be(3, "cada documento en un solo lote");
        (await api.EnDbAsync(db => db.AsociacionesLote.CountAsync(l => l.PaqueteId == paquete))).Should().Be(1);
        await ProcesarLoteAsync();
        (await EvidenciasDeAsync(ids.Values)).Should().Be(3, "un correo por comparendo, ninguno de más");
    }

    /// <summary>Entre encolar y procesar otra persona le pone un documento al comparendo: vuelve a Duplicado con su motivo y sin correo.</summary>
    [Fact]
    public async Task AsociarLote_SiCambioEntreEncolarYProcesar_VuelveADuplicado_ConMotivo_YSinCorreo()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var (_, cliente, ids) = await EmpresaConAsync("LOTEO1", "LOTEO2");
        foreach (var n in ids.Keys)
            await ConConductorAsync(cliente, ids[n]);
        var (paquete, docs) = await SubirVariosAsync(cliente, ("LOTEO1", "otro-1"), ("LOTEO2", "otro-2"));
        var loteId = (await AsociarLoteAsync(cliente, paquete)).GetProperty("loteId").GetGuid();
        var ajeno = Guid.NewGuid();
        await api.EnDbAsync(db => db.Comparendos.Where(c => c.Id == ids["LOTEO1"]).ExecuteUpdateAsync(u => u.SetProperty(c => c.ArchivoEvidenciaId, ajeno)));

        await ProcesarLoteAsync();

        (await ComparendoDbAsync(ids["LOTEO1"])).ArchivoEvidenciaId.Should().Be(ajeno, "no se pisa el documento que puso el otro");
        (await api.EnDbAsync(db => db.DocumentosOcr.AsNoTracking().SingleAsync(d => d.Id == docs["otro-1"].Id, ct))).Estado.Should().Be(EstadoDocumentoOcr.Duplicado);
        (await EvidenciasAsync(ids["LOTEO1"])).Should().BeEmpty();
        (await EvidenciasAsync(ids["LOTEO2"])).Should().ContainSingle();
        var fin = (await PreviaAsync(cliente, paquete)).GetProperty("enCurso");
        (fin.GetProperty("loteId").GetGuid(), fin.GetProperty("asociados").GetInt32()).Should().Be((loteId, 1));
        fin.GetProperty("noAsociados").EnumerateArray().Select(n => (n.GetProperty("documentoId").GetGuid(), n.GetProperty("nombreArchivo").GetString(), n.GetProperty("motivo").GetString()))
            .Should().Equal((docs["otro-1"].Id, docs["otro-1"].NombreArchivo, "Otra persona cambió el documento del comparendo mientras lo revisabas. Vuelve a abrirlo para compararlos."));
        (await PreviaAsync(cliente, paquete)).GetProperty("porRevisar").GetProperty("reemplazos").GetInt32().Should().Be(1, "ahora es un reemplazo");
    }

    /// <summary>Una concesión vigente no se toca (otra réplica lo está procesando); vencida, el trabajador la retoma.</summary>
    [Fact]
    public async Task AsociarLote_ConcesionVigenteNoSeToca_Vencida_SeRetoma()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var (_, cliente, ids) = await EmpresaConAsync("LOTEL1");
        await ConConductorAsync(cliente, ids["LOTEL1"]);
        var (paquete, _) = await SubirVariosAsync(cliente, ("LOTEL1", "concesion"));
        var loteId = (await AsociarLoteAsync(cliente, paquete)).GetProperty("loteId").GetGuid();
        var ahora = api.Reloj.GetUtcNow();
        await api.EnDbAsync(db => db.AsociacionLoteDocumentos.Where(i => i.LoteId == loteId)
            .ExecuteUpdateAsync(u => u.SetProperty(i => i.ProximoIntentoEn, ahora.AddMinutes(5)).SetProperty(i => i.Intentos, (short)1)));

        await ProcesarLoteAsync();
        (await FilasDelLoteAsync(loteId)).Should().ContainSingle().Which.Should().Match<AsociacionLoteDocumento>(i => i.Resultado == ResultadoAsociacionLote.PorAsociar && i.Intentos == 1,
            "otra réplica tiene la concesión: no se toca");

        await api.EnDbAsync(db => db.AsociacionLoteDocumentos.Where(i => i.LoteId == loteId).ExecuteUpdateAsync(u => u.SetProperty(i => i.ProximoIntentoEn, ahora.AddMinutes(-1))));
        await ProcesarLoteAsync();
        (await FilasDelLoteAsync(loteId)).Should().ContainSingle().Which.Resultado.Should().Be(ResultadoAsociacionLote.Asociado);
        (await EvidenciasAsync(ids["LOTEL1"])).Should().ContainSingle();
    }

    /// <summary>
    /// Caída tras confirmar la asociación y antes de registrar el correo: el documento quedó Asociado y la fila sigue pendiente.
    /// La vuelta siguiente solo registra el correo y cierra el lote.
    /// </summary>
    [Fact]
    public async Task AsociarLote_SiCayoTrasAsociar_LaRetomaSoloRegistraElCorreo()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var (_, cliente, ids) = await EmpresaConAsync("LOTER1");
        await ConConductorAsync(cliente, ids["LOTER1"]);
        var (paquete, docs) = await SubirVariosAsync(cliente, ("LOTER1", "retoma"));
        var loteId = (await AsociarLoteAsync(cliente, paquete)).GetProperty("loteId").GetGuid();
        await using (var scope = api.App.Services.CreateAsyncScope())
        {
            // Lo que alcanzó a hacer la vuelta que se cayó: la asociación, sin el correo.
            var db = scope.ServiceProvider.GetRequiredService<ComparendosDb>();
            var doc = await db.DocumentosOcr.FirstAsync(d => d.Id == docs["retoma"].Id, ct);
            await scope.ServiceProvider.GetRequiredService<ServicioOcr>().AsociarAsync(doc, false, null, Actor.Sistema, ct, desdeLote: true);
        }

        await ProcesarLoteAsync();

        (await EvidenciasAsync(ids["LOTER1"])).Should().ContainSingle();
        (await FilasDelLoteAsync(loteId)).Should().ContainSingle().Which.Should().Match<AsociacionLoteDocumento>(i => i.Resultado == ResultadoAsociacionLote.Asociado && i.CorreoEstado == "Pendiente");
        (await api.EnDbAsync(db => db.AsociacionesLote.AsNoTracking().SingleAsync(l => l.Id == loteId, ct))).TerminadoEn.Should().NotBeNull();
    }

    /// <summary>
    /// Caída aún más tarde: la vuelta alcanzó a asociar Y a registrar el correo, pero no a cerrar su fila. La retoma reconoce el
    /// correo ya registrado y solo cierra la fila: el conductor no recibe el documento dos veces.
    /// </summary>
    [Fact]
    public async Task AsociarLote_SiCayoTrasRegistrarElCorreo_LaRetomaNoLoReenvia()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var (_, cliente, ids) = await EmpresaConAsync("LOTEW1");
        await ConConductorAsync(cliente, ids["LOTEW1"]);
        var (paquete, docs) = await SubirVariosAsync(cliente, ("LOTEW1", "retoma-tarde"));
        var loteId = (await AsociarLoteAsync(cliente, paquete)).GetProperty("loteId").GetGuid();
        await using (var scope = api.App.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ComparendosDb>();
            var doc = await db.DocumentosOcr.FirstAsync(d => d.Id == docs["retoma-tarde"].Id, ct);
            await scope.ServiceProvider.GetRequiredService<AsociacionDocumentos>().UnoAsync(doc, false, null, Actor.Sistema, despachar: false, ct, desdeLote: true);
        }
        (await EvidenciasAsync(ids["LOTEW1"])).Should().ContainSingle("la vuelta que se cayó ya lo registró");

        await ProcesarLoteAsync();

        (await EvidenciasAsync(ids["LOTEW1"])).Should().ContainSingle("la retoma no lo vuelve a mandar");
        (await FilasDelLoteAsync(loteId)).Should().ContainSingle().Which.Should().Match<AsociacionLoteDocumento>(i => i.Resultado == ResultadoAsociacionLote.Asociado && i.CorreoEstado == "Pendiente");
    }

    /// <summary>Si el cierre del lote falló al final, el trabajador lo repara cuando la cola está vacía (la franja no se queda «asociando»).</summary>
    [Fact]
    public async Task AsociarLote_LoteSinFechaDeTermino_SeReparaConLaColaVacia()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var (_, cliente, _) = await EmpresaConAsync("LOTEF1");
        var (paquete, _) = await SubirVariosAsync(cliente, ("LOTEF1", "fin"));
        var loteId = (await AsociarLoteAsync(cliente, paquete)).GetProperty("loteId").GetGuid();
        await ProcesarLoteAsync();
        await api.EnDbAsync(db => db.AsociacionesLote.Where(l => l.Id == loteId).ExecuteUpdateAsync(u => u.SetProperty(l => l.TerminadoEn, (DateTimeOffset?)null)));

        await ProcesarLoteAsync();

        (await api.EnDbAsync(db => db.AsociacionesLote.AsNoTracking().SingleAsync(l => l.Id == loteId, ct))).TerminadoEn.Should().NotBeNull();
    }

    /// <summary>Encola un documento y deja su fila con <paramref name="intentos"/> previos (el reclamo suma uno).</summary>
    private async Task<(Guid Lote, Guid Documento, Guid Comparendo)> UnoEnColaAsync(string numero, short intentos)
    {
        var (_, cliente, ids) = await EmpresaConAsync(numero);
        await ConConductorAsync(cliente, ids[numero]);
        var (paquete, docs) = await SubirVariosAsync(cliente, (numero, $"tec-{numero}"));
        var lote = (await AsociarLoteAsync(cliente, paquete)).GetProperty("loteId").GetGuid();
        await api.EnDbAsync(db => db.AsociacionLoteDocumentos.Where(i => i.LoteId == lote).ExecuteUpdateAsync(u => u.SetProperty(i => i.Intentos, intentos)));
        return (lote, docs[$"tec-{numero}"].Id, ids[numero]);
    }

    private async Task UnaVueltaConErrorTecnicoAsync()
    {
        var trabajador = api.App.Services.GetRequiredService<ProcesadorAsociacionLote>();
        trabajador.AntesDeProcesar = _ => throw new InvalidOperationException("falla técnica simulada");
        try
        {
            await trabajador.ProcesarSiguienteAsync(TestContext.Current.CancellationToken);
        }
        finally
        {
            trabajador.AntesDeProcesar = null;
        }
    }

    /// <summary>Error técnico con intentos de sobra: la fila sigue pendiente y se reintenta pronto (30 s), no a los 10 minutos.</summary>
    [Fact]
    public async Task AsociarLote_ErrorTecnico_ReintentaEn30Segundos_YLuegoAsocia()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        await ProcesarLoteAsync();
        var (lote, documento, comparendo) = await UnoEnColaAsync("LOTEG1", intentos: 0);

        await UnaVueltaConErrorTecnicoAsync();

        var fila = (await FilasDelLoteAsync(lote)).Single();
        (fila.Resultado, fila.Intentos, fila.ProximoIntentoEn).Should().Be((ResultadoAsociacionLote.PorAsociar, (short)1, api.Reloj.GetUtcNow().AddSeconds(30)));
        await api.EnDbAsync(db => db.AsociacionLoteDocumentos.Where(i => i.LoteId == lote).ExecuteUpdateAsync(u => u.SetProperty(i => i.ProximoIntentoEn, api.Reloj.GetUtcNow().AddSeconds(-1))));
        await ProcesarLoteAsync();
        (await FilasDelLoteAsync(lote)).Single().Resultado.Should().Be(ResultadoAsociacionLote.Asociado);
        (await EvidenciasAsync(comparendo)).Should().ContainSingle();
        _ = documento;
    }

    /// <summary>Tercer error técnico con el documento sin asociar: vuelve a Duplicado y el resumen lo dice con un motivo de negocio.</summary>
    [Fact]
    public async Task AsociarLote_TercerErrorTecnico_SinAsociar_VuelveADuplicado_ConMotivo()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        await ProcesarLoteAsync();
        var (lote, documento, comparendo) = await UnoEnColaAsync("LOTEH1", intentos: 2);

        await UnaVueltaConErrorTecnicoAsync();

        var fila = (await FilasDelLoteAsync(lote)).Single();
        (fila.Resultado, fila.Motivo).Should().Be((ResultadoAsociacionLote.NoAsociado, ProcesadorAsociacionLote.MotivoErrorTecnico));
        (await api.EnDbAsync(db => db.DocumentosOcr.AsNoTracking().SingleAsync(d => d.Id == documento, ct))).Estado.Should().Be(EstadoDocumentoOcr.Duplicado);
        (await EvidenciasAsync(comparendo)).Should().BeEmpty();
    }

    /// <summary>Tercer error técnico cuando el documento SÍ quedó asociado (lo que fallaba era el correo): no se dice «no se asoció».</summary>
    [Fact]
    public async Task AsociarLote_TercerErrorTecnico_YaAsociado_QuedaAsociado_ConCorreoOmitido()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        await ProcesarLoteAsync();
        var (lote, documento, _) = await UnoEnColaAsync("LOTEI1", intentos: 2);
        await using (var scope = api.App.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ComparendosDb>();
            var doc = await db.DocumentosOcr.FirstAsync(d => d.Id == documento, ct);
            await scope.ServiceProvider.GetRequiredService<ServicioOcr>().AsociarAsync(doc, false, null, Actor.Sistema, ct, desdeLote: true);
        }

        await UnaVueltaConErrorTecnicoAsync();

        var fila = (await FilasDelLoteAsync(lote)).Single();
        (fila.Resultado, fila.CorreoEstado, fila.CorreoMotivo).Should().Be((ResultadoAsociacionLote.Asociado, "Omitido", "No se pudo preparar el correo al conductor; el documento quedó asociado"));
    }

    /// <summary>El resumen nunca muestra un motivo nulo ni vacío (la pantalla lo pinta tal cual).</summary>
    [Fact]
    public async Task AsociarLote_Resumen_MotivosNuncaNulosNiVacios()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var (_, cliente, ids) = await EmpresaConAsync("LOTEK7", "LOTEK8");
        await ConConductorAsync(cliente, ids["LOTEK7"]);
        await ConConductorAsync(cliente, ids["LOTEK8"]);
        var (paquete, docs) = await SubirVariosAsync(cliente, ("LOTEK7", "nulo-1"), ("LOTEK8", "nulo-2"));
        var lote = (await AsociarLoteAsync(cliente, paquete)).GetProperty("loteId").GetGuid();
        await ProcesarLoteAsync();
        var (d1, d2) = (docs["nulo-1"].Id, docs["nulo-2"].Id);
        await api.EnDbAsync(db => db.AsociacionLoteDocumentos.Where(i => i.LoteId == lote && i.DocumentoId == d1)
            .ExecuteUpdateAsync(u => u.SetProperty(i => i.Resultado, ResultadoAsociacionLote.NoAsociado).SetProperty(i => i.Motivo, (string?)null)));
        await api.EnDbAsync(db => db.AsociacionLoteDocumentos.Where(i => i.LoteId == lote && i.DocumentoId == d2)
            .ExecuteUpdateAsync(u => u.SetProperty(i => i.CorreoEstado, "Omitido").SetProperty(i => i.CorreoMotivo, "  ")));

        var fin = (await PreviaAsync(cliente, paquete)).GetProperty("enCurso");

        fin.GetProperty("noAsociados").EnumerateArray().Select(n => n.GetProperty("motivo").GetString()).Should().Equal(ProcesadorAsociacionLote.MotivoErrorTecnico);
        fin.GetProperty("sinCorreo").EnumerateArray().Select(n => n.GetProperty("motivo").GetString()).Should().Equal(AsociacionEnSegundoPlano.MotivoCorreoSinDetalle);
    }

    [Fact]
    public async Task AsociarLote_DosEscaneadosDelMismoComparendo_ElSegundoVaALaColaDeReemplazos()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var (_, cliente, _) = await EmpresaConAsync("LOTEM1");
        var (paquete, docs) = await SubirVariosAsync(cliente, ("LOTEM1", "mismo-1"), ("LOTEM1", "mismo-2"));
        var primero = docs.Values.MinBy(d => d.Id)!;
        var segundo = docs.Values.MaxBy(d => d.Id)!;

        var previa = await PreviaAsync(cliente, paquete);
        (previa.GetProperty("asociables").GetInt32(), previa.GetProperty("porRevisar").GetProperty("reemplazos").GetInt32()).Should().Be((1, 1));
        var listado = (await cliente.GetFromJsonAsync<JsonElement>($"{Base}/paquetes/{paquete}", ct)).GetProperty("documentos").EnumerateArray()
            .ToDictionary(d => d.GetProperty("id").GetGuid(), d => d.GetProperty("revision").GetString());
        (listado[primero.Id], listado[segundo.Id]).Should().Be(((string?)null, "reemplazo"));
        (await AsociarLoteAsync(cliente, paquete)).GetProperty("encolados").GetInt32().Should().Be(1);
        // Mientras el primero está en curso, el segundo sigue siendo reemplazo y no se puede encolar en otra pulsación.
        (await AsociarLoteAsync(cliente, paquete)).GetProperty("encolados").GetInt32().Should().Be(0);
        await ProcesarLoteAsync();
    }

    /// <summary>K = conductores distintos por identidad (tipo y número de documento), no por correo: un mismo buzón compartido no los junta.</summary>
    [Fact]
    public async Task AsociarLote_VistaPrevia_CuentaConductoresPorDocumento_NoPorCorreo()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var (_, cliente, ids) = await EmpresaConAsync("LOTEJ1", "LOTEJ2", "LOTEJ3", "LOTEJ4");
        async Task Conductor(string numero, string documento, string correo) =>
            (await cliente.PutAsJsonAsync($"/api/v1/comparendos/comparendos/{ids[numero]}/contraventor", new
            {
                tipoDocumento = "CC", numeroDocumento = documento, nombres = "Juan Pérez", correo, declaracionTratamientoDatos = true,
            }, ct)).EnsureSuccessStatusCode();
        // Tres conductores distintos que comparten el buzón (como en la prueba con el correo de Samuel) y uno repetido con otro buzón.
        await Conductor("LOTEJ1", "1020304050", "buzon.compartido@example.test");
        await Conductor("LOTEJ2", "2030405060", "buzon.compartido@example.test");
        await Conductor("LOTEJ3", "3040506070", "buzon.compartido@example.test");
        await Conductor("LOTEJ4", "1020304050", "otro.buzon@example.test");
        var (paquete, _) = await SubirVariosAsync(cliente, ("LOTEJ1", "cond-1"), ("LOTEJ2", "cond-2"), ("LOTEJ3", "cond-3"), ("LOTEJ4", "cond-4"));

        var correos = (await PreviaAsync(cliente, paquete)).GetProperty("correos");

        (correos.GetProperty("saldran").GetInt32(), correos.GetProperty("conductores").GetInt32()).Should().Be((4, 3), "4 correos (uno por comparendo) a 3 conductores; por buzón serían 2");
    }

    [Fact]
    public async Task AsociarLote_SinNadaQueAsociar_Responde200EnCeros_YSinLote()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var (_, cliente, _) = await EmpresaConAsync("LOTEV1");
        var (paquete, _) = await SubirVariosAsync(cliente, ("LOTEV9", "solo-nuevo"));

        var previa = await PreviaAsync(cliente, paquete);

        previa.GetProperty("asociables").GetInt32().Should().Be(0);
        (previa.GetProperty("correos").GetProperty("saldran").GetInt32(), previa.GetProperty("correos").GetProperty("conductores").GetInt32(),
            previa.GetProperty("correos").GetProperty("omitidos").GetArrayLength()).Should().Be((0, 0, 0));
        (previa.GetProperty("porRevisar").GetProperty("reemplazos").GetInt32(), previa.GetProperty("porRevisar").GetProperty("numeroDistinto").GetInt32()).Should().Be((0, 0));
        previa.GetProperty("enCurso").ValueKind.Should().Be(JsonValueKind.Null);
        var r = await AsociarLoteAsync(cliente, paquete);
        (r.GetProperty("encolados").GetInt32(), r.GetProperty("loteId").ValueKind).Should().Be((0, JsonValueKind.Null));
    }

    [Fact]
    public async Task AsociarLote_DeOtraEmpresa_NoSeEncuentra_YNoEncolaNada()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var (_, cliente, _) = await EmpresaConAsync("LOTEZ1");
        var (paquete, docs) = await SubirVariosAsync(cliente, ("LOTEZ1", "lote-z"));
        var intruso = api.Cliente(new UsuarioDePrueba(await EmpresaAsync(api, ocr: true), Operador));

        (await intruso.GetAsync($"{Base}/paquetes/{paquete}/asociacion-lote", ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await intruso.PostAsJsonAsync($"{Base}/paquetes/{paquete}/asociar-lote", new { }, ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await api.EnDbAsync(db => db.DocumentosOcr.AsNoTracking().SingleAsync(d => d.Id == docs["lote-z"].Id, ct))).Estado.Should().Be(EstadoDocumentoOcr.Duplicado);
    }

    [Fact]
    public async Task AsociarLote_ConSoloVer_LaVistaPreviaSi_EncolarNo()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var (empresa, cliente, _) = await EmpresaConAsync("LOTEP1");
        var (paquete, _) = await SubirVariosAsync(cliente, ("LOTEP1", "lote-p"));
        var lector = api.Cliente(new UsuarioDePrueba(empresa, [Permisos.OcrVer]));

        (await lector.GetAsync($"{Base}/paquetes/{paquete}/asociacion-lote", TestContext.Current.CancellationToken)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await lector.PostAsJsonAsync($"{Base}/paquetes/{paquete}/asociar-lote", new { }, TestContext.Current.CancellationToken)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    /// <summary>La misma ventana en la pantalla individual: cancelada la petición al empezar a asociar, la asociación y su correo salen igual.</summary>
    [Fact(Timeout = 60_000)]
    public async Task AsociarUno_ConLaPeticionCanceladaAlEmpezar_AsociaYMandaElCorreo()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var (_, cliente, ids) = await EmpresaConAsync("LOTEU1");
        await ConConductorAsync(cliente, ids["LOTEU1"]);
        var (_, docs) = await SubirVariosAsync(cliente, ("LOTEU1", "uno-cancela"));
        await using var scope = api.App.Services.CreateAsyncScope();
        var asociacion = scope.ServiceProvider.GetRequiredService<AsociacionDocumentos>();
        using var peticion = new CancellationTokenSource();
        asociacion.AntesDeAsociar = _ => peticion.Cancel();
        var doc = await scope.ServiceProvider.GetRequiredService<ComparendosDb>().DocumentosOcr.FirstAsync(d => d.Id == docs["uno-cancela"].Id, ct);

        var (r, correo) = await asociacion.UnoAsync(doc, reemplazar: false, evidenciaVista: null, Actor.Sistema, despachar: true, peticion.Token);

        r.YaAsociado.Should().BeFalse();
        correo.Estado.Should().BeOneOf("Enviado", "Pendiente");
        (await ComparendoDbAsync(ids["LOTEU1"])).ArchivoEvidenciaId.Should().Be(docs["uno-cancela"].ArchivoId);
        (await EvidenciasAsync(ids["LOTEU1"])).Should().ContainSingle();
    }

    /// <summary>
    /// LA tabla compartida con el front (lib/ocr.ts numeroBase/numerosDifieren): mismos valores en las dos pruebas para que
    /// las dos copias de la regla no se separen.
    /// </summary>
    [Theory]
    [InlineData("D-11001000000000012345", "11001000000000012345", false)] // letra larga
    [InlineData("A-98765", "98765", false)] // letra corta
    [InlineData("A98765", "98765", false)] // letra corta sin guion
    [InlineData("11001 0000 0000 0012345", "11001000000000012345", false)] // espacios
    [InlineData("11001.000000000.012345", "11001000000000012345", false)] // puntos
    [InlineData("11001-000000000-012345", "11001000000000012345", false)] // guiones
    [InlineData("  d - 11001000000000012345", "11001000000000012345", false)] // espacios y minúscula al inicio
    [InlineData("11001/000000000012345", "11001000000000012345", true)] // barra
    [InlineData("11001_000000000012345", "11001000000000012345", true)] // guion bajo
    [InlineData("11001,000000000012345", "11001000000000012345", true)] // coma
    [InlineData("11001:000000000012345", "11001000000000012345", true)] // dos puntos
    [InlineData("#11001000000000012345", "11001000000000012345", true)] // numeral
    [InlineData("11001/00000999/0308", "11001000009990308", true)] // barras dentro (caso del E2E)
    [InlineData("11001000000000012346", "11001000000000012345", true)] // otro dígito
    [InlineData("AB12C34", "AB12C35", true)] // mixto distinto
    [InlineData("11001a00000000012345", "11001A00000000012345", false)] // minúscula en medio (se compara en mayúscula)
    [InlineData("   ", "11001000000000012345", false)] // solo espacios = sin número
    [InlineData(null, null, false)] // ninguno
    [InlineData(null, "11001000000000012345", false)] // sin número leído
    [InlineData("11001000000000012345", "", false)] // sin número en el sistema
    public void DifiereALaVista_EsLaMismaReglaQueElFront(string? leido, string? sistema, bool esperado) =>
        NumeroComparendo.DifiereALaVista(leido, sistema).Should().Be(esperado);
}
