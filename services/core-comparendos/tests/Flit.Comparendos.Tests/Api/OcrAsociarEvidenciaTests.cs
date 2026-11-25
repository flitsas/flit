using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Flit.Comparendos.Api.Modulos.Avisos;
using Flit.Comparendos.Api.Modulos.Calendario;
using Flit.Comparendos.Api.Modulos.Comparendos;
using Flit.Comparendos.Api.Modulos.Correo;
using Flit.Comparendos.Api.Modulos.Plantillas;
using Flit.Comparendos.Api.Modulos.Portal;
using Flit.Comparendos.Api.Plataforma;
using Flit.Comparendos.Api.Persistence;
using Flit.Comparendos.Api.Persistence.Modelo;
using Flit.Comparendos.Domain.Modelo;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;
using static Flit.Comparendos.Tests.Api.ComparendosDePrueba;

namespace Flit.Comparendos.Tests.Api;

/// <summary>
/// GDC-EVID: al asociar (o reemplazar) el documento escaneado de un comparendo existente sale de inmediato un correo al
/// conductor con ese documento, independiente de los avisos 1 a 3 (no es un aviso: no toca su secuencia ni su estado).
/// </summary>
public sealed partial class OcrAsociarTests
{
    private static async Task ConConductorAsync(HttpClient cliente, Guid comparendo, string? correo = "juan@example.test")
    {
        (await cliente.PutAsJsonAsync($"/api/v1/comparendos/comparendos/{comparendo}/contraventor", new
        {
            tipoDocumento = "CC", numeroDocumento = "1020304050", nombres = "Juan Pérez", correo, declaracionTratamientoDatos = true,
        }, TestContext.Current.CancellationToken)).EnsureSuccessStatusCode();
    }

    private Task<List<ComunicacionSaliente>> EvidenciasAsync(Guid comparendo) =>
        api.EnDbAsync(db => db.Comunicaciones.AsNoTracking().Where(c => c.ComparendoId == comparendo && c.Tipo == TipoComunicacion.Evidencia).OrderBy(c => c.CreadoEn).ToListAsync());

    private Task<List<TimelineEvento>> OmitidasAsync(Guid comparendo) =>
        api.EnDbAsync(db => db.Timeline.AsNoTracking().Where(t => t.ComparendoId == comparendo && t.Tipo == TipoEventoTimeline.EvidenciaOmitida).ToListAsync());

    private async Task DebeTenerNotaAsync(Guid comparendo, string motivo, bool reemplazo = false)
    {
        var nota = (await OmitidasAsync(comparendo)).Should().ContainSingle().Subject;
        nota.Descripcion.Should().Be($"Documento asociado: no se envió correo al conductor. Motivo: {motivo}.");
        using var datos = JsonDocument.Parse(nota.Datos!);
        (datos.RootElement.GetProperty("motivo").GetString(), datos.RootElement.GetProperty("reemplazo").GetBoolean()).Should().Be((motivo, reemplazo));
    }

    private static async Task<(string Estado, string? Motivo)> CorreoDeAsync(HttpResponseMessage r)
    {
        var correo = (await r.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken)).GetProperty("correo");
        return (correo.GetProperty("estado").GetString()!, correo.GetProperty("motivo").GetString());
    }

    private static async Task<(bool SeEnviara, string? Motivo)> PrevistaAsync(HttpClient cliente, Guid doc)
    {
        var p = (await cliente.GetFromJsonAsync<JsonElement>($"{Base}/documentos/{doc}", TestContext.Current.CancellationToken)).GetProperty("duplicado").GetProperty("correoAlAsociar");
        return (p.GetProperty("seEnviara").GetBoolean(), p.GetProperty("motivoNo").GetString());
    }

    /// <summary>El escenario de Samuel: con el aviso 1 y el 2 enviados, asociar manda ya el documento; el estado sigue en aviso 2 hasta el aviso 3, que también lo lleva.</summary>
    [Fact]
    public async Task Asociar_ConAviso1Y2Enviados_EnviaYaLaEvidencia_SinTocarLosAvisos_YElAviso3LlevaElDocumento()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var (empresa, cliente, comparendo) = await ComparendoAsync("EVI-1", dia: 2);
        await ConConductorAsync(cliente, comparendo);
        await SchedulerAsync(empresa);
        await SchedulerAsync(empresa);
        api.Reloj.Hoy(new DateOnly(2026, 10, 6));
        await SchedulerAsync(empresa);
        await SchedulerAsync(empresa);
        (await api.EnDbAsync(db => db.AvisosConductor.Where(a => a.ComparendoId == comparendo).Select(a => a.TipoAviso).OrderBy(t => t).ToListAsync(ct))).Should().Equal(TipoAviso.Aviso1, TipoAviso.Aviso2);
        var (_, doc) = await SubirAsync(cliente, "EVI-1", "evi-escenario");
        (await PrevistaAsync(cliente, doc.Id)).Should().Be((true, null));

        var r = await Asociar(cliente, doc.Id, reemplazar: false);

        (await CorreoDeAsync(r)).Should().Be(("Enviado", null));
        var evidencias = await EvidenciasAsync(comparendo);
        evidencias.Should().ContainSingle().Which.Estado.Should().Be(EstadoComunicacion.Enviado);
        var adjuntos = await api.EnDbAsync(db => db.ComunicacionAdjuntos.AsNoTracking().Where(a => a.ComunicacionId == evidencias[0].Id).ToListAsync(ct));
        adjuntos.Should().ContainSingle().Which.Should().Match<ComunicacionAdjunto>(a => a.Nombre == "comparendo-EVI-1.pdf" && a.ArchivoId == doc.ArchivoId, "solo el documento: sin autorización ni poder");
        (await api.EnDbAsync(db => db.Timeline.AsNoTracking().SingleAsync(t => t.ComparendoId == comparendo && t.Tipo == TipoEventoTimeline.EvidenciaEnviada, ct)))
            .Descripcion.Should().Be("Correo con el documento del comparendo registrado para envío a juan@example.test.");
        // No es un aviso: la secuencia y el estado siguen donde estaban.
        (await api.EnDbAsync(db => db.AvisosConductor.CountAsync(a => a.ComparendoId == comparendo, ct))).Should().Be(2);
        (await ComparendoDbAsync(comparendo)).EstadoNotificacionConductor.Should().Be(EstadoNotificacionConductor.Aviso2);

        api.Reloj.Hoy(new DateOnly(2026, 11, 10));
        await SchedulerAsync(empresa);
        await SchedulerAsync(empresa);
        (await api.EnDbAsync(db => db.AvisosConductor.Where(a => a.ComparendoId == comparendo).Select(a => a.TipoAviso).ToListAsync(ct))).Should().Contain(TipoAviso.Aviso3);
        (await api.EnDbAsync(db => (from c in db.Comunicaciones where c.ComparendoId == comparendo && c.Tipo == TipoComunicacion.Aviso3
                                    join a in db.ComunicacionAdjuntos on c.Id equals a.ComunicacionId select a.Nombre).ToListAsync(ct))).Should().Contain("comparendo-EVI-1.pdf");
        (await EvidenciasAsync(comparendo)).Should().HaveCount(1, "el aviso 3 no vuelve a mandar la evidencia");
    }

    /// <summary>Si la evidencia se omite (documento muy pesado), el estado no cambia por ella y el aviso 3 sí lleva el documento.</summary>
    [Fact]
    public async Task Asociar_ConEvidenciaOmitida_ElEstadoNoCambia_YElAviso3SiLlevaElDocumento()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var (empresa, cliente, comparendo) = await ComparendoAsync("EVI-8", dia: 2);
        await ConConductorAsync(cliente, comparendo);
        await SchedulerAsync(empresa);
        await SchedulerAsync(empresa);
        api.Reloj.Hoy(new DateOnly(2026, 10, 6));
        await SchedulerAsync(empresa);
        await SchedulerAsync(empresa);
        (await ComparendoDbAsync(comparendo)).EstadoNotificacionConductor.Should().Be(EstadoNotificacionConductor.Aviso2);
        var (_, doc) = await SubirAsync(cliente, "EVI-8", "evi-omitida-aviso3");
        await api.EnDbAsync(db => db.Archivos.Where(a => a.Id == doc.ArchivoId).ExecuteUpdateAsync(u => u.SetProperty(a => a.TamanoBytes, 3L * 1024 * 1024 + 1)));

        (await CorreoDeAsync(await Asociar(cliente, doc.Id, reemplazar: false))).Should().Be(("Omitido", "El documento pesa demasiado para enviarlo por correo"));

        (await EvidenciasAsync(comparendo)).Should().BeEmpty();
        (await ComparendoDbAsync(comparendo)).EstadoNotificacionConductor.Should().Be(EstadoNotificacionConductor.Aviso2, "la evidencia omitida no mueve el estado");
        api.Reloj.Hoy(new DateOnly(2026, 11, 10));
        await SchedulerAsync(empresa);
        await SchedulerAsync(empresa);
        (await ComparendoDbAsync(comparendo)).EstadoNotificacionConductor.Should().Be(EstadoNotificacionConductor.Aviso3);
        (await api.EnDbAsync(db => (from c in db.Comunicaciones where c.ComparendoId == comparendo && c.Tipo == TipoComunicacion.Aviso3
                                    join a in db.ComunicacionAdjuntos on c.Id equals a.ComunicacionId select a.Nombre).ToListAsync(ct))).Should().Contain("comparendo-EVI-8.pdf");
    }

    /// <summary>
    /// El comparendo que dejó AsociarAsync en el contexto está viejo (ExecuteUpdate no lo actualiza). Si el servicio lo usara tal cual,
    /// decidiría con datos de antes: aquí el comparendo se paga «por fuera» y el servicio debe verlo.
    /// </summary>
    [Fact]
    public async Task RegistrarEvidencia_NoDecideConUnComparendoViejoDelContexto()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var (empresa, cliente, comparendo) = await ComparendoAsync("EVI-9");
        await ConConductorAsync(cliente, comparendo);
        var (_, doc) = await SubirAsync(cliente, "EVI-9", "evi-viejo");
        await using var scope = api.App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ComparendosDb>();
        (await db.Comparendos.SingleAsync(c => c.Id == comparendo, ct)).Pagado.Should().BeFalse("queda rastreado, como tras AsociarAsync");
        await api.EnDbAsync(d => d.Comparendos.Where(c => c.Id == comparendo).ExecuteUpdateAsync(u => u.SetProperty(c => c.Pagado, true)));

        var (comunicacion, decision) = await scope.ServiceProvider.GetRequiredService<ServicioEvidencia>()
            .RegistrarAsync(comparendo, empresa, doc.ArchivoId, reemplazo: false, Actor.Sistema, ct);

        (comunicacion, decision.Motivo).Should().Be(((Guid?)null, "El comparendo ya está pagado"));
    }

    /// <summary>
    /// Ya confirmada la asociación, que se cierre la pestaña o venza la petición no debe perder el correo: al reintentar el
    /// documento ya figura asociado y no se reenvía. El trabajo posterior corre con su propio plazo.
    /// </summary>
    [Fact]
    public async Task EnviarAlAsociar_ConLaPeticionYaCancelada_IgualRegistraElCorreo()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var (empresa, cliente, comparendo) = await ComparendoAsync("EVI-11");
        await ConConductorAsync(cliente, comparendo);
        var (_, doc) = await SubirAsync(cliente, "EVI-11", "evi-cancelada");
        await using var scope = api.App.Services.CreateAsyncScope();
        using var cancelada = new CancellationTokenSource();
        await cancelada.CancelAsync();

        var r = await scope.ServiceProvider.GetRequiredService<ServicioEvidencia>()
            .EnviarAlAsociarAsync(comparendo, empresa, doc.ArchivoId, reemplazo: false, Actor.Sistema, cancelada.Token);

        r.Estado.Should().BeOneOf("Enviado", "Pendiente");
        (await EvidenciasAsync(comparendo)).Should().ContainSingle();
    }

    // ── Vencimiento del plazo propio del trabajo posterior a la asociación ───────────────────────────────────────────────
    private sealed class PlantillasLentas(ComparendosDb db, IConfiguration configuracion) : ServicioPlantillas(db, configuracion)
    {
        public override Task<Armado> ArmarAsync(ConjuntoPlantillas conjunto, TipoPlantilla tipo, Guid? tipologiaId, IReadOnlyDictionary<string, string?> valores, CancellationToken ct) =>
            Task.Delay(Timeout.Infinite, ct).ContinueWith<Armado>(_ => throw new InvalidOperationException("inalcanzable"), TaskScheduler.Default);
    }

    private sealed class DespachadorLento(IServiceScopeFactory scopes, TimeProvider tiempo, ILogger<DespachadorCorreos> logger) : DespachadorCorreos(scopes, tiempo, logger)
    {
        public override async Task<EstadoComunicacion> DespacharAsync(Guid id, CancellationToken ct)
        {
            await Task.Delay(Timeout.Infinite, ct);
            return EstadoComunicacion.Pendiente;
        }
    }

    private static ServicioEvidencia ServicioConPlazo(IServiceProvider sp, bool plantillasLentas, bool despachoLento, int segundos) => new(
        sp.GetRequiredService<ComparendosDb>(), sp.GetRequiredService<Capacidades>(),
        plantillasLentas ? new PlantillasLentas(sp.GetRequiredService<ComparendosDb>(), sp.GetRequiredService<IConfiguration>()) : sp.GetRequiredService<ServicioPlantillas>(),
        sp.GetRequiredService<ContextoPlantilla>(), sp.GetRequiredService<ServicioPortal>(), sp.GetRequiredService<ServicioCorreo>(), sp.GetRequiredService<Timeline>(),
        sp.GetRequiredService<IProductosHabilitados>(),
        despachoLento ? new DespachadorLento(sp.GetRequiredService<IServiceScopeFactory>(), TimeProvider.System, sp.GetRequiredService<ILogger<DespachadorCorreos>>()) : sp.GetRequiredService<DespachadorCorreos>(),
        sp.GetRequiredService<ILogger<ServicioEvidencia>>())
    {
        Plazo = TimeSpan.FromSeconds(segundos),
    };

    /// <summary>El plazo vence mientras se arma el correo: no lanza, dice que no se pudo preparar, y la asociación queda intacta.</summary>
    [Fact(Timeout = 60_000)]
    public async Task EnviarAlAsociar_SiElPlazoVenceAlRegistrar_NoLanza_YLaAsociacionQuedaIntacta()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var (empresa, cliente, comparendo) = await ComparendoAsync("EVI-12");
        await ConConductorAsync(cliente, comparendo);
        var (_, doc) = await SubirAsync(cliente, "EVI-12", "evi-plazo-registro");
        // Se asocia con el canal apagado (no sale correo) para que la asociación quede hecha antes de ejercer el vencimiento.
        await api.EnDbAsync(db => db.ConfiguracionesEmpresa.Where(c => c.TenantId == empresa).ExecuteUpdateAsync(u => u.SetProperty(c => c.NotificationChannel, CanalComunicacion.Whatsapp)));
        (await Asociar(cliente, doc.Id, reemplazar: false)).EnsureSuccessStatusCode();
        await api.EnDbAsync(db => db.ConfiguracionesEmpresa.Where(c => c.TenantId == empresa).ExecuteUpdateAsync(u => u.SetProperty(c => c.NotificationChannel, CanalComunicacion.Email)));
        await using var scope = api.App.Services.CreateAsyncScope();

        var r = await ServicioConPlazo(scope.ServiceProvider, plantillasLentas: true, despachoLento: false, segundos: 1)
            .EnviarAlAsociarAsync(comparendo, empresa, doc.ArchivoId, reemplazo: false, Actor.Sistema, ct);

        r.Should().Be(new ResultadoEvidencia(ResultadoEvidencia.Omitido, "No se pudo preparar el correo al conductor; el documento quedó asociado"));
        (await EvidenciasAsync(comparendo)).Should().BeEmpty();
        (await ComparendoDbAsync(comparendo)).ArchivoEvidenciaId.Should().Be(doc.ArchivoId);
        (await api.EnDbAsync(db => db.DocumentosOcr.AsNoTracking().SingleAsync(d => d.Id == doc.Id, ct))).Estado.Should().Be(EstadoDocumentoOcr.Asociado);
    }

    /// <summary>El plazo vence durante el despacho: el correo ya está registrado y queda Pendiente para el ciclo; no lanza.</summary>
    [Fact(Timeout = 60_000)]
    public async Task EnviarAlAsociar_SiElPlazoVenceAlDespachar_QuedaPendiente_SinLanzar()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var (empresa, cliente, comparendo) = await ComparendoAsync("EVI-13");
        await ConConductorAsync(cliente, comparendo);
        var (_, doc) = await SubirAsync(cliente, "EVI-13", "evi-plazo-despacho");
        await using var scope = api.App.Services.CreateAsyncScope();

        // 4 s: el registro real (plantilla, BD) debe caber antes de que el despacho lento haga vencer el plazo, aun en frío o con carga.
        var r = await ServicioConPlazo(scope.ServiceProvider, plantillasLentas: false, despachoLento: true, segundos: 4)
            .EnviarAlAsociarAsync(comparendo, empresa, doc.ArchivoId, reemplazo: false, Actor.Sistema, TestContext.Current.CancellationToken);

        r.Should().Be(new ResultadoEvidencia(ResultadoEvidencia.Pendiente, null));
        (await EvidenciasAsync(comparendo)).Should().ContainSingle().Which.Estado.Should().Be(EstadoComunicacion.Pendiente);
    }

    [Fact]
    public async Task Asociar_AlReemplazar_EnviaElDocumentoNuevo()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var (_, cliente, comparendo) = await ComparendoAsync("EVI-2");
        await ConConductorAsync(cliente, comparendo);
        var (_, primero) = await SubirAsync(cliente, "EVI-2", "evi-reemplazo-1");
        (await CorreoDeAsync(await Asociar(cliente, primero.Id, reemplazar: false))).Estado.Should().Be("Enviado");
        var (_, segundo) = await SubirAsync(cliente, "EVI-2", "evi-reemplazo-2");

        var r = await Asociar(cliente, segundo.Id, reemplazar: true, vista: primero.ArchivoId);

        (await CorreoDeAsync(r)).Estado.Should().Be("Enviado");
        (await OmitidasAsync(comparendo)).Should().BeEmpty("si el correo sale, ni la asociación ni el reemplazo dejan nota de omitido");
        var evidencias = await EvidenciasAsync(comparendo);
        evidencias.Should().HaveCount(2);
        (await api.EnDbAsync(db => db.ComunicacionAdjuntos.AsNoTracking().Where(a => a.ComunicacionId == evidencias[1].Id).Select(a => a.ArchivoId).SingleAsync(ct))).Should().Be(segundo.ArchivoId, "el correo lleva el documento NUEVO");
        (await api.EnDbAsync(db => db.Timeline.Where(t => t.ComparendoId == comparendo && t.Tipo == TipoEventoTimeline.EvidenciaEnviada).OrderBy(t => t.OcurridoEn).ThenBy(t => t.Id).Select(t => t.Descripcion).ToListAsync(ct)))
            .Should().HaveCount(2).And.Subject.Last().Should().Be("Correo con el documento nuevo del comparendo registrado para envío a juan@example.test.");
    }

    [Fact]
    public async Task Asociar_AlReemplazarSinCorreo_LaNotaDiceQueEsReemplazo_YSiSaleNoHayNota()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var (_, cliente, comparendo) = await ComparendoAsync("EVI-10");
        await ConConductorAsync(cliente, comparendo);
        var (_, primero) = await SubirAsync(cliente, "EVI-10", "evi-nota-1");
        (await CorreoDeAsync(await Asociar(cliente, primero.Id, reemplazar: false))).Estado.Should().Be("Enviado");
        (await OmitidasAsync(comparendo)).Should().BeEmpty("si el correo sale no hay nota");
        var (_, segundo) = await SubirAsync(cliente, "EVI-10", "evi-nota-2");
        await api.EnDbAsync(db => db.Archivos.Where(a => a.Id == segundo.ArchivoId).ExecuteUpdateAsync(u => u.SetProperty(a => a.TamanoBytes, 3L * 1024 * 1024 + 1)));

        (await CorreoDeAsync(await Asociar(cliente, segundo.Id, reemplazar: true, vista: primero.ArchivoId))).Estado.Should().Be("Omitido");

        await DebeTenerNotaAsync(comparendo, "El documento pesa demasiado para enviarlo por correo", reemplazo: true);
    }

    [Fact]
    public async Task Asociar_UnDocumentoYaAsociado_NoReenvia()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var (_, cliente, comparendo) = await ComparendoAsync("EVI-3");
        await ConConductorAsync(cliente, comparendo);
        var (_, doc) = await SubirAsync(cliente, "EVI-3", "evi-idempotente");
        (await CorreoDeAsync(await Asociar(cliente, doc.Id, reemplazar: false))).Estado.Should().Be("Enviado");

        var otraVez = await CorreoDeAsync(await Asociar(cliente, doc.Id, reemplazar: false));

        otraVez.Should().Be(("Omitido", "El documento ya estaba asociado: no se vuelve a enviar"));
        (await OmitidasAsync(comparendo)).Should().BeEmpty("un documento ya asociado no deja nota nueva");
        (await EvidenciasAsync(comparendo)).Should().HaveCount(1);
    }

    [Theory]
    [InlineData("pagado", "El comparendo ya está pagado")]
    [InlineData("inactivo", "El comparendo está inactivo")]
    [InlineData("sin-conductor", "No hay un conductor asignado")]
    [InlineData("sin-correo", "El conductor no tiene correo registrado")]
    [InlineData("pesado", "El documento pesa demasiado para enviarlo por correo")]
    public async Task Asociar_SiNoSeCumpleUnaCondicion_AsociaIgual_ConElMotivo_YLaPantallaLoAnunciaIgual(string caso, string motivo)
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var (_, cliente, comparendo) = await ComparendoAsync($"EVI-M-{caso}");
        if (caso != "sin-conductor")
            await ConConductorAsync(cliente, comparendo, correo: caso == "sin-correo" ? null : "juan@example.test");
        if (caso == "pagado")
            await api.EnDbAsync(db => db.Comparendos.Where(c => c.Id == comparendo).ExecuteUpdateAsync(u => u.SetProperty(c => c.Pagado, true)));
        if (caso == "inactivo")
            await api.EnDbAsync(db => db.Comparendos.Where(c => c.Id == comparendo).ExecuteUpdateAsync(u => u.SetProperty(c => c.Activo, false).SetProperty(c => c.MotivoInactivacion, MotivoInactivacion.InactivadoOperador)));
        var (_, doc) = await SubirAsync(cliente, $"EVI-M-{caso}", $"evi-motivo-{caso}");
        if (caso == "pesado")
            await api.EnDbAsync(db => db.Archivos.Where(a => a.Id == doc.ArchivoId).ExecuteUpdateAsync(u => u.SetProperty(a => a.TamanoBytes, 3L * 1024 * 1024 + 1)));
        (await PrevistaAsync(cliente, doc.Id)).Should().Be((false, motivo));

        var r = await Asociar(cliente, doc.Id, reemplazar: false);

        r.IsSuccessStatusCode.Should().BeTrue();
        (await CorreoDeAsync(r)).Should().Be(("Omitido", motivo));
        (await ComparendoDbAsync(comparendo)).ArchivoEvidenciaId.Should().Be(doc.ArchivoId, "la asociación se hace igual");
        (await EvidenciasAsync(comparendo)).Should().BeEmpty();
        (await api.EnDbAsync(db => db.Timeline.AnyAsync(t => t.ComparendoId == comparendo && t.Tipo == TipoEventoTimeline.EvidenciaEnviada, ct))).Should().BeFalse();
        await DebeTenerNotaAsync(comparendo, motivo);
        // Asociarlo otra vez es idempotente: no agrega otra nota.
        (await CorreoDeAsync(await Asociar(cliente, doc.Id, reemplazar: false))).Estado.Should().Be("Omitido");
        (await OmitidasAsync(comparendo)).Should().HaveCount(1);
    }

    [Fact]
    public async Task Asociar_SiLaEmpresaNoUsaCorreo_NoEnvia_ConElMotivo()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var (empresa, cliente, comparendo) = await ComparendoAsync("EVI-4");
        await ConConductorAsync(cliente, comparendo);
        await api.EnDbAsync(db => db.ConfiguracionesEmpresa.Where(c => c.TenantId == empresa).ExecuteUpdateAsync(u => u.SetProperty(c => c.NotificationChannel, CanalComunicacion.Whatsapp)));
        var (_, doc) = await SubirAsync(cliente, "EVI-4", "evi-canal");
        (await PrevistaAsync(cliente, doc.Id)).Should().Be((false, "La empresa no usa correo para notificar"));

        (await CorreoDeAsync(await Asociar(cliente, doc.Id, reemplazar: false))).Should().Be(("Omitido", "La empresa no usa correo para notificar"));
        (await EvidenciasAsync(comparendo)).Should().BeEmpty();
        await DebeTenerNotaAsync(comparendo, "La empresa no usa correo para notificar");
    }

    /// <summary>Un fallo al preparar el correo (aquí, sin plantilla publicada) no deshace la asociación.</summary>
    [Fact]
    public async Task Asociar_SiElCorreoFalla_LaAsociacionQuedaHecha()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var (_, cliente, comparendo) = await ComparendoAsync("EVI-5");
        await ConConductorAsync(cliente, comparendo);
        var (_, doc) = await SubirAsync(cliente, "EVI-5", "evi-falla");
        await api.EnDbAsync(db => db.Plantillas.Where(p => p.Tipo == TipoPlantilla.CorreoEvidencia).ExecuteUpdateAsync(u => u.SetProperty(p => p.Activo, false)));
        try
        {
            var r = await Asociar(cliente, doc.Id, reemplazar: false);

            r.IsSuccessStatusCode.Should().BeTrue(await r.Content.ReadAsStringAsync(ct));
            (await CorreoDeAsync(r)).Should().Be(("Omitido", "No se pudo preparar el correo al conductor; el documento quedó asociado"));
            (await ComparendoDbAsync(comparendo)).ArchivoEvidenciaId.Should().Be(doc.ArchivoId);
            (await api.EnDbAsync(db => db.DocumentosOcr.AsNoTracking().SingleAsync(d => d.Id == doc.Id, ct))).Estado.Should().Be(EstadoDocumentoOcr.Asociado);
            (await EvidenciasAsync(comparendo)).Should().BeEmpty();
            (await api.EnDbAsync(db => db.Timeline.AnyAsync(t => t.ComparendoId == comparendo && t.Tipo == TipoEventoTimeline.EvidenciaEnviada, ct))).Should().BeFalse();
            await DebeTenerNotaAsync(comparendo, "No se pudo preparar el correo al conductor; el documento quedó asociado");
        }
        finally
        {
            await api.EnDbAsync(db => db.Plantillas.Where(p => p.Tipo == TipoPlantilla.CorreoEvidencia).ExecuteUpdateAsync(u => u.SetProperty(p => p.Activo, true)));
        }
    }

    [Fact]
    public async Task Asociar_RespetaLasCopiasPorTipo_DeLaEmpresa()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var (empresa, cliente, comparendo) = await ComparendoAsync("EVI-6");
        await ConConductorAsync(cliente, comparendo);
        const string cc = """[{"correo":"copia-evidencia@example.test","tipos":["Evidencia"]},{"correo":"copia-aviso@example.test","tipos":["Aviso1"]}]""";
        const string bcc = """[{"correo":"oculta@example.test","tipos":[]}]""";
        await api.EnDbAsync(db => db.ConfiguracionesEmpresa.Where(c => c.TenantId == empresa).ExecuteUpdateAsync(u => u.SetProperty(c => c.Cc, cc).SetProperty(c => c.Bcc, bcc)));
        var (_, doc) = await SubirAsync(cliente, "EVI-6", "evi-copias");

        (await CorreoDeAsync(await Asociar(cliente, doc.Id, reemplazar: false))).Estado.Should().Be("Enviado");

        var correos = (await EvidenciasAsync(comparendo)).Select(c => JsonDocument.Parse(c.Destinatarios).RootElement).ToList();
        correos.Should().HaveCount(2, "uno para el conductor y otro para la copia visible de ese tipo");
        string Para(JsonElement e) => e.GetProperty("para")[0].GetProperty("email").GetString()!;
        var delConductor = correos.Single(e => Para(e) == "juan@example.test");
        delConductor.GetProperty("cc").EnumerateArray().Select(x => x.GetString()).Should().Equal("copia-evidencia@example.test");
        correos.Single(e => Para(e) == "copia-evidencia@example.test").GetProperty("cc").EnumerateArray().Select(x => x.GetString()).Should().Equal("juan@example.test");
        correos.Should().OnlyContain(e => e.GetProperty("bcc").EnumerateArray().Select(x => x.GetString()).SequenceEqual(new[] { "oculta@example.test" }), "la oculta va en bcc, nunca visible, y la de otro tipo no va");
    }

    [Theory]
    [InlineData(true, false, true, null)]
    [InlineData(false, false, false, "El comparendo está inactivo")]
    [InlineData(true, true, false, "El comparendo ya está pagado")]
    public void Evaluar_DecideConUnaSolaFuncion(bool activo, bool pagado, bool seEnviara, string? motivo)
    {
        var c = Comparendo(activo, pagado, correo: "a@example.test");
        EvidenciaPorCorreo.Evaluar(c, new ConfiguracionEmpresa(), productoHabilitado: true, 1024).Should().Be(new DecisionEvidencia(seEnviara, motivo));
    }

    [Fact]
    public async Task Asociar_UnDocumentoDeOtraEmpresa_NoSeEncuentra_YNoEnviaNada()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var (_, cliente, comparendo) = await ComparendoAsync("EVI-7");
        await ConConductorAsync(cliente, comparendo);
        var (_, doc) = await SubirAsync(cliente, "EVI-7", "evi-tenant");
        var otraEmpresa = await EmpresaAsync(api, ocr: true);
        var intruso = api.Cliente(new UsuarioDePrueba(otraEmpresa, Operador));

        (await Asociar(intruso, doc.Id, reemplazar: false)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await intruso.GetAsync($"{Base}/documentos/{doc.Id}", TestContext.Current.CancellationToken)).StatusCode.Should().Be(HttpStatusCode.NotFound);

        (await EvidenciasAsync(comparendo)).Should().BeEmpty();
        (await ComparendoDbAsync(comparendo)).ArchivoEvidenciaId.Should().BeNull();
    }

    [Fact]
    public void Evaluar_SinProductoHabilitado_OSinDocumento_NoEnvia()
    {
        var c = Comparendo(true, pagado: false, correo: "a@example.test");
        EvidenciaPorCorreo.Evaluar(c, new ConfiguracionEmpresa(), productoHabilitado: false, 1024).Should().Be(DecisionEvidencia.No("La empresa no tiene habilitado el producto de Comparendos"));
        EvidenciaPorCorreo.Evaluar(c, new ConfiguracionEmpresa(), productoHabilitado: true, null).SeEnviara.Should().BeFalse();
        EvidenciaPorCorreo.Evaluar(c, new ConfiguracionEmpresa(), productoHabilitado: true, 3L * 1024 * 1024).SeEnviara.Should().BeTrue("el tope es inclusive");
    }

    private static Comparendo Comparendo(bool activo, bool pagado, string? correo) =>
        new() { Activo = activo, Pagado = pagado, Contraventor = new Contraventor { Correo = correo } };
}
