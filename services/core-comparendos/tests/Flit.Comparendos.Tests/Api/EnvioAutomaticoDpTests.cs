using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Flit.Comparendos.Api.Modulos.DerechosPeticion;
using Flit.Comparendos.Api.Modulos.Ingesta.Fuentes;
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

/// <summary>
/// Epic #14606, HU #14609: el trabajo diario que envía solo los derechos de petición elegibles, sin duplicar, con tope
/// por empresa y día, apagado por defecto y solo con tipologías aprobadas por un abogado.
/// </summary>
public sealed class EnvioAutomaticoDpTests(ApiConFuente api) : IClassFixture<ApiConFuente>
{
    private const string Base = "/api/v1/comparendos";
    private static readonly DateOnly Hoy = new(2026, 11, 18);
    private static readonly string[] Operador = [Permisos.BandejaVer, Permisos.DpGenerar, Permisos.DpEnviar, Permisos.RepresentantesGestionar, Permisos.ConfiguracionEditar];

    private HttpClient Super => api.Cliente(new UsuarioDePrueba(Guid.NewGuid(), [], SuperAdmin: true));

    private async Task<Guid> TipologiaAsync(string codigo) =>
        (await Super.GetFromJsonAsync<JsonElement>($"{Base}/admin/tipologias", TestContext.Current.CancellationToken))
        .EnumerateArray().Single(t => t.GetProperty("codigo").GetString() == codigo).GetProperty("id").GetGuid();

    /// <summary>Comparendo de agente de una persona jurídica, impuesto y notificado hace más de un año: la regla de V1 elige caducidad (DCI).</summary>
    private static ItemFuente Viejo(string numero) => Item(numero, fotomulta: false) with
    {
        FechaImposicion = new DateOnly(2025, 6, 1), FechaNotificacion = new DateOnly(2025, 6, 5), CodigoInfraccion = "C29",
    };

    /// <summary>
    /// Empresa con DP, DCI habilitada, versión publicada aprobada por el abogado y marcada automatizable, el organismo con su
    /// correo de radicación, un representante con firma y el envío automático encendido con el tope dado.
    /// </summary>
    private async Task<Guid> EmpresaListaAsync(int tope = 50, bool encendido = true, bool conRepresentante = true, bool aprobada = true)
    {
        var ct = TestContext.Current.CancellationToken;
        api.Reloj.Hoy(Hoy);
        (await Super.PatchAsJsonAsync($"{Base}/admin/organismos/aaaaaaaa-0001-4000-8000-000000000001", new { correosRadicacion = new[] { "radicacion@movilidad.test" } }, ct)).EnsureSuccessStatusCode();
        var empresa = await EmpresaAsync(api, dp: true);
        var dci = await TipologiaAsync("DCI");
        (await Super.PutAsJsonAsync($"{Base}/admin/empresas/{empresa}/tipologias/{dci}", new { habilitada = true }, ct)).EnsureSuccessStatusCode();
        if (aprobada)
        {
            var plantillas = await Super.GetFromJsonAsync<JsonElement>($"{Base}/plantillas?conjunto=Flit&tipo=DpDocumento", ct);
            var version = plantillas.EnumerateArray().Single(p => p.GetProperty("tipologiaId").GetGuid() == dci).GetProperty("publicada").GetProperty("id").GetGuid();
            await Super.PostAsJsonAsync($"{Base}/admin/plantillas/versiones/{version}/revision-juridica", new { nota = "Aprobado por el abogado" }, ct);
            (await Super.PutAsJsonAsync($"{Base}/admin/empresas/{empresa}/tipologias/{dci}/automatizable", new { automatizable = true }, ct)).EnsureSuccessStatusCode();
        }

        if (conRepresentante)
            await RepresentanteConFirmaAsync(empresa);
        if (encendido)
            (await Super.PutAsJsonAsync($"{Base}/admin/empresas/{empresa}/envio-automatico-dp", new { activo = true, topeDiario = tope }, ct)).EnsureSuccessStatusCode();
        return empresa;
    }

    private async Task RepresentanteConFirmaAsync(Guid empresa)
    {
        var ct = TestContext.Current.CancellationToken;
        var cliente = api.Cliente(new UsuarioDePrueba(empresa, Operador));
        var rl = await (await cliente.PostAsJsonAsync($"{Base}/representantes", new
        {
            alcance = "Empresa", tipoDocumento = "CC", numeroDocumento = "52123456", nombres = "María Gómez", correo = "rl@gestora.test", cargo = "Representante legal",
            direccionNotificacion = "Calle 100 # 9A-45", permisoEnvioDp = true, permisoCambioContraventor = true,
        }, ct)).Content.ReadFromJsonAsync<JsonElement>(ct);
        const string png = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==";
        using var firma = new MultipartFormDataContent { { new ByteArrayContent(Convert.FromBase64String(png)) { Headers = { ContentType = new MediaTypeHeaderValue("image/png") } }, "archivo", "firma.png" } };
        (await cliente.PostAsync($"{Base}/representantes/{rl.GetProperty("id").GetGuid()}/documentos/firma", firma, ct)).EnsureSuccessStatusCode();
    }

    private async Task<List<Guid>> ComparendosAsync(Guid empresa, string prefijo, int cantidad)
    {
        var items = Enumerable.Range(1, cantidad).Select(i => Viejo($"{prefijo}-{i}")).ToArray();
        return [.. (await IngresarAsync(api, empresa, items)).Values];
    }

    private async Task CorrerAsync(Guid? empresa, Disparo disparo = Disparo.Programado)
    {
        await using var scope = api.App.Services.CreateAsyncScope();
        var ejecutor = scope.ServiceProvider.GetServices<IEjecutorTrabajo>().OfType<EnvioAutomaticoDp>().Single();
        await ejecutor.EjecutarAsync(new SolicitudTrabajo(TipoTrabajo.EnvioAutomaticoDp, disparo, empresa), CancellationToken.None);
    }

    private Task<List<DerechoPeticion>> DpsAsync(Guid empresa) =>
        api.EnDbAsync(db => db.DerechosPeticion.AsNoTracking().Where(d => d.TenantId == empresa).OrderBy(d => d.CreadoEn).ToListAsync());

    [Fact]
    public async Task Apagado_por_defecto_no_envia_nada()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = await EmpresaListaAsync(encendido: false);
        await ComparendosAsync(empresa, "AUTO-OFF", 2);

        await CorrerAsync(empresa);

        (await DpsAsync(empresa)).Should().BeEmpty("sin dpAutomaticoActivo el trabajo no hace nada");
    }

    [Fact]
    public async Task Envia_los_elegibles_como_sistema_y_no_cuentan_como_trabajo_de_una_persona()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = await EmpresaListaAsync();
        var ids = await ComparendosAsync(empresa, "AUTO-OK", 2);

        await CorrerAsync(empresa);

        var dps = await DpsAsync(empresa);
        dps.Should().HaveCount(2);
        dps.Should().OnlyContain(d => d.EnvioAutomatico && d.EnviadoPor == Guid.Empty && d.Estado == EstadoDp.Enviado && d.Radicado.StartsWith("DCI-", StringComparison.Ordinal));
        dps.Select(d => d.ComparendoId).Should().BeEquivalentTo(ids);
        (await api.EnDbAsync(db => db.Comparendos.CountAsync(c => c.TenantId == empresa && c.TieneDp))).Should().Be(2);
        (await api.EnDbAsync(db => db.Comunicaciones.CountAsync(c => c.TenantId == empresa && c.Tipo == TipoComunicacion.DpOt))).Should().Be(2);
    }

    [Fact]
    public async Task Un_segundo_dia_no_repite_lo_que_ya_tiene_dp()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = await EmpresaListaAsync();
        await ComparendosAsync(empresa, "AUTO-REP", 2);

        await CorrerAsync(empresa);
        await CorrerAsync(empresa);
        api.Reloj.Hoy(Hoy.AddDays(1));
        await CorrerAsync(empresa);

        (await DpsAsync(empresa)).Should().HaveCount(2, "un DP por comparendo de por vida");
    }

    [Fact]
    public async Task El_tope_diario_deja_el_resto_para_el_siguiente_dia_habil()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = await EmpresaListaAsync(tope: 3);
        await ComparendosAsync(empresa, "AUTO-TOPE", 5);

        await CorrerAsync(empresa);
        (await DpsAsync(empresa)).Should().HaveCount(3, "con tope 3 y 5 elegibles salen 3");

        // Otra corrida el mismo día (p. ej. «Ejecutar ahora») no pasa del tope.
        await CorrerAsync(empresa);
        (await DpsAsync(empresa)).Should().HaveCount(3);

        // Al día hábil siguiente salen los 2 que faltaban.
        api.Reloj.Hoy(Hoy.AddDays(1));
        await CorrerAsync(empresa);
        (await DpsAsync(empresa)).Should().HaveCount(5);
    }

    [Fact]
    public async Task Dos_corridas_simultaneas_sobre_el_mismo_comparendo_generan_un_solo_dp_sin_huerfanos()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = await EmpresaListaAsync();
        var ids = await ComparendosAsync(empresa, "AUTO-CARRERA", 1);
        var radicadosAntes = await api.EnDbAsync(db => db.RadicadosReservados.CountAsync());
        var archivosAntes = await api.EnDbAsync(db => db.Archivos.CountAsync(a => a.TenantId == empresa));

        await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(() => CorrerAsync(empresa))));

        var dps = await DpsAsync(empresa);
        dps.Should().ContainSingle().Which.ComparendoId.Should().Be(ids[0]);
        (await api.EnDbAsync(db => db.RadicadosReservados.CountAsync())).Should().Be(radicadosAntes + 1, "un solo radicado reservado, ninguno huérfano");
        // PDF del DP + cuerpo del correo + el PDF adjunto reutilizado: lo mismo que deja un solo envío manual.
        var archivosDespues = await api.EnDbAsync(db => db.Archivos.CountAsync(a => a.TenantId == empresa));
        archivosDespues.Should().BeLessThan(archivosAntes + 6, "las corridas perdedoras no dejan archivos");
        (await api.EnDbAsync(db => db.Comunicaciones.CountAsync(c => c.TenantId == empresa && c.Tipo == TipoComunicacion.DpOt))).Should().Be(1);
    }

    [Fact]
    public async Task Una_tipologia_sin_revision_juridica_vigente_no_se_envia()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = await EmpresaListaAsync();
        await ComparendosAsync(empresa, "AUTO-SINREV", 1);
        var dci = await TipologiaAsync("DCI");
        // El texto publicado pierde la aprobación (como si alguien hubiera publicado uno nuevo sin pasar por el abogado). Se restaura al final.
        var version = await api.EnDbAsync(db => db.PlantillaVersiones.Where(v => db.Plantillas.Any(p => p.VersionPublicadaId == v.Id && p.Tipo == TipoPlantilla.DpDocumento && p.TipologiaId == dci && p.Conjunto == ConjuntoPlantillas.Flit))
            .Select(v => new { v.Id, v.RevisadaJuridicamenteEn }).SingleAsync());
        try
        {
            await api.EnDbAsync(db => db.PlantillaVersiones.Where(v => v.Id == version.Id).ExecuteUpdateAsync(u => u.SetProperty(v => v.RevisadaJuridicamenteEn, (DateTimeOffset?)null)));
            await CorrerAsync(empresa);
        }
        finally
        {
            await api.EnDbAsync(db => db.PlantillaVersiones.Where(v => v.Id == version.Id).ExecuteUpdateAsync(u => u.SetProperty(v => v.RevisadaJuridicamenteEn, version.RevisadaJuridicamenteEn)));
        }

        (await DpsAsync(empresa)).Should().BeEmpty("el texto no tiene revisión jurídica: no sale solo");
    }

    [Fact]
    public async Task Ejecutar_ahora_del_superadmin_si_envia_los_correos()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var empresa = await EmpresaListaAsync();
        await ComparendosAsync(empresa, "AUTO-AHORA", 2);

        var respuesta = await Super.PostAsync($"{Base}/admin/ingesta/trabajos/EnvioAutomaticoDp/ejecutar?empresaId={empresa}", null, ct);
        respuesta.StatusCode.Should().Be(HttpStatusCode.Accepted);

        // En las pruebas los trabajos en segundo plano están apagados: se toma la solicitud de la cola y se procesa aquí.
        var cola = api.App.Services.GetRequiredService<ColaTrabajos>();
        SolicitudTrabajo? solicitud = null;
        await foreach (var s in cola.LeerAsync(ct))
        {
            if (s.Tipo == TipoTrabajo.EnvioAutomaticoDp && s.TenantId == empresa)
            {
                solicitud = s;
                break;
            }
        }

        solicitud!.Disparo.Should().Be(Disparo.Manual);
        await ActivatorUtilities.CreateInstance<ProcesadorTrabajos>(api.App.Services).ProcesarAsync(solicitud, ct);
        var dps = await DpsAsync(empresa);

        dps.Should().HaveCount(2);
        // Los correos salieron de verdad (por la plataforma de pruebas), no quedaron pendientes.
        (await api.EnDbAsync(db => db.Comunicaciones.Where(c => c.TenantId == empresa && c.Tipo == TipoComunicacion.DpOt).Select(c => c.Estado).ToListAsync(ct)))
            .Should().OnlyContain(e => e != EstadoComunicacion.Pendiente);
    }

    [Fact]
    public async Task Un_comparendo_que_no_se_puede_enviar_se_omite_con_su_motivo_una_sola_vez()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = await EmpresaListaAsync(conRepresentante: false);
        var ids = await ComparendosAsync(empresa, "AUTO-OMITE", 1);

        await CorrerAsync(empresa);
        await CorrerAsync(empresa);

        (await DpsAsync(empresa)).Should().BeEmpty();
        var eventos = await api.EnDbAsync(db => db.Timeline.AsNoTracking().Where(t => t.ComparendoId == ids[0] && t.Tipo == TipoEventoTimeline.DpAutomaticoOmitido).ToListAsync());
        eventos.Should().ContainSingle("el mismo motivo no se repite cada día").Which.Descripcion.Should().Contain("representante");
    }

    [Fact]
    public async Task Un_dp_automatico_colgado_en_enviando_se_marca_para_revisar_y_no_se_reenvia()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = await EmpresaListaAsync();
        var ids = await ComparendosAsync(empresa, "AUTO-COLGADO", 1);
        await CorrerAsync(empresa);
        var dp = (await DpsAsync(empresa)).Single();
        await api.EnDbAsync(db => db.DerechosPeticion.Where(d => d.Id == dp.Id)
            .ExecuteUpdateAsync(u => u.SetProperty(d => d.Estado, EstadoDp.Enviando).SetProperty(d => d.CreadoEn, api.Reloj.Ahora.AddHours(-5))));
        var correosAntes = await api.EnDbAsync(db => db.Comunicaciones.CountAsync(c => c.TenantId == empresa && c.Tipo == TipoComunicacion.DpOt));

        await CorrerAsync(empresa);
        await CorrerAsync(empresa);

        var despues = (await DpsAsync(empresa)).Single();
        despues.Estado.Should().Be(EstadoDp.Enviando, "no se toca el estado ni se reenvía a ciegas");
        (await api.EnDbAsync(db => db.Comunicaciones.CountAsync(c => c.TenantId == empresa && c.Tipo == TipoComunicacion.DpOt))).Should().Be(correosAntes);
        (await api.EnDbAsync(db => db.Timeline.CountAsync(t => t.ComparendoId == ids[0] && t.Tipo == TipoEventoTimeline.DpEnviandoSinReaccion))).Should().Be(1);
    }
}
