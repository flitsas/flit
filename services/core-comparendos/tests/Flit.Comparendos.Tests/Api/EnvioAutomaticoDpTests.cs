using System.Diagnostics;
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
using Npgsql;
using Xunit;
using static Flit.Comparendos.Tests.Api.ComparendosDePrueba;

namespace Flit.Comparendos.Tests.Api;

/// <summary>
/// Epic #14606, HUs #14609 y #14610: el trabajo diario que envía solo los derechos de petición elegibles, sin duplicar, con
/// tope por empresa y día, apagado por defecto y solo con tipologías aprobadas por un abogado. Cada prueba arma su propia
/// empresa; la versión publicada del conjunto Flit y el organismo aaaaaaaa-0001 son compartidos, así que ninguna depende del orden.
/// </summary>
public sealed class EnvioAutomaticoDpTests(ApiConGanchosDp api) : IClassFixture<ApiConGanchosDp>, IAsyncLifetime
{
    private const string Base = "/api/v1/comparendos";
    private const string Organismo = "aaaaaaaa-0001-4000-8000-000000000001";
    private static readonly DateOnly Hoy = new(2026, 11, 18);
    private static readonly string[] Operador = [Permisos.BandejaVer, Permisos.DpGenerar, Permisos.DpEnviar, Permisos.RepresentantesGestionar, Permisos.ConfiguracionEditar];

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public ValueTask DisposeAsync()
    {
        api.Ganchos.Limpiar();
        api.Productos.Apagadas.Clear();
        api.Productos.Fallan.Clear();
        api.Productos.Expiran.Clear();
        return ValueTask.CompletedTask;
    }

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
    private async Task<Guid> EmpresaListaAsync(int tope = 50, bool encendido = true, bool conRepresentante = true, bool aprobada = true, bool admiteOrganismo = true, bool conFirma = true, string conjunto = "Flit")
    {
        var ct = TestContext.Current.CancellationToken;
        api.Reloj.Hoy(Hoy);
        (await Super.PatchAsJsonAsync($"{Base}/admin/organismos/{Organismo}", new { correosRadicacion = new[] { "radicacion@movilidad.test" }, admiteDpPorCorreo = admiteOrganismo }, ct)).EnsureSuccessStatusCode();
        var empresa = await EmpresaAsync(api, dp: true, conjunto: conjunto);
        var dci = await TipologiaAsync("DCI");
        (await Super.PutAsJsonAsync($"{Base}/admin/empresas/{empresa}/tipologias/{dci}", new { habilitada = true }, ct)).EnsureSuccessStatusCode();
        if (aprobada)
        {
            var plantillas = await Super.GetFromJsonAsync<JsonElement>($"{Base}/plantillas?conjunto=Flit&tipo=DpDocumento", ct);
            var version = plantillas.EnumerateArray().Single(p => p.GetProperty("tipologiaId").GetGuid() == dci).GetProperty("publicada").GetProperty("id").GetGuid();
            (await Super.PostAsJsonAsync($"{Base}/admin/plantillas/versiones/{version}/revision-juridica", new { nota = "Aprobado por el abogado" }, ct)).EnsureSuccessStatusCode();
            (await Super.PutAsJsonAsync($"{Base}/admin/empresas/{empresa}/tipologias/{dci}/automatizable", new { automatizable = true }, ct)).EnsureSuccessStatusCode();
        }

        if (conRepresentante)
            await RepresentanteConFirmaAsync(empresa, conFirma);
        if (encendido)
            (await Super.PutAsJsonAsync($"{Base}/admin/empresas/{empresa}/envio-automatico-dp", new { activo = true, topeDiario = tope }, ct)).EnsureSuccessStatusCode();
        return empresa;
    }

    private async Task RepresentanteConFirmaAsync(Guid empresa, bool conFirma = true)
    {
        var ct = TestContext.Current.CancellationToken;
        var cliente = api.Cliente(new UsuarioDePrueba(empresa, Operador));
        var rl = await (await cliente.PostAsJsonAsync($"{Base}/representantes", new
        {
            alcance = "Empresa", tipoDocumento = "CC", numeroDocumento = "52123456", nombres = "María Gómez", correo = "rl@gestora.test", cargo = "Representante legal",
            direccionNotificacion = "Calle 100 # 9A-45", permisoEnvioDp = true, permisoCambioContraventor = true,
        }, ct)).Content.ReadFromJsonAsync<JsonElement>(ct);
        if (!conFirma)
            return;
        const string png = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==";
        using var firma = new MultipartFormDataContent { { new ByteArrayContent(Convert.FromBase64String(png)) { Headers = { ContentType = new MediaTypeHeaderValue("image/png") } }, "archivo", "firma.png" } };
        (await cliente.PostAsync($"{Base}/representantes/{rl.GetProperty("id").GetGuid()}/documentos/firma", firma, ct)).EnsureSuccessStatusCode();
    }

    private async Task AlertasAsync(Guid empresa) =>
        (await api.Cliente(new UsuarioDePrueba(empresa, Operador)).PutAsJsonAsync($"{Base}/configuracion/correo", new { canalCorreo = "FlitSmtp", correosAlertaOperativa = new[] { "alertas@gestora.test" } }, TestContext.Current.CancellationToken)).EnsureSuccessStatusCode();

    /// <summary>Ingresa <paramref name="cantidad"/> comparendos viejos; devuelve sus ids por número.</summary>
    private async Task<Dictionary<string, Guid>> ComparendosAsync(Guid empresa, string prefijo, int cantidad) =>
        await IngresarAsync(api, empresa, [.. Enumerable.Range(1, cantidad).Select(i => Viejo($"{prefijo}-{i}"))]);

    private async Task CorrerAsync(Guid? empresa, Disparo disparo = Disparo.Programado)
    {
        await using var scope = api.App.Services.CreateAsyncScope();
        var ejecutor = scope.ServiceProvider.GetServices<IEjecutorTrabajo>().OfType<EnvioAutomaticoDp>().Single();
        await ejecutor.EjecutarAsync(new SolicitudTrabajo(TipoTrabajo.EnvioAutomaticoDp, disparo, empresa), CancellationToken.None);
    }

    private Task<List<DerechoPeticion>> DpsAsync(Guid empresa) =>
        api.EnDbAsync(db => db.DerechosPeticion.AsNoTracking().Where(d => d.TenantId == empresa).OrderBy(d => d.CreadoEn).ToListAsync());

    private Task<int> ArchivosAsync(Guid empresa) => api.EnDbAsync(db => db.Archivos.CountAsync(a => a.TenantId == empresa));

    private Task<int> RadicadosAsync() => api.EnDbAsync(db => db.RadicadosReservados.CountAsync());

    private Task<int> ResumenesAsync(Guid empresa) =>
        api.EnDbAsync(db => db.Comunicaciones.CountAsync(c => c.TenantId == empresa && c.Tipo == TipoComunicacion.ResumenEnvioAutomaticoDp));

    private async Task<HttpResponseMessage> EnviarManualAsync(Guid empresa, Guid comparendo) =>
        await api.Cliente(new UsuarioDePrueba(empresa, Operador)).PostAsJsonAsync($"{Base}/comparendos/{comparendo}/dp", new { tipologiaId = await TipologiaAsync("DCI") }, TestContext.Current.CancellationToken);

    // ───────────────────────── Lo básico ─────────────────────────

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
        dps.Select(d => d.ComparendoId).Should().BeEquivalentTo(ids.Values);
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
        respuesta.StatusCode.Should().Be(System.Net.HttpStatusCode.Accepted);

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
        (await DpsAsync(empresa)).Should().HaveCount(2);
        // Los correos salieron de verdad (por la plataforma de pruebas), no quedaron pendientes.
        (await api.EnDbAsync(db => db.Comunicaciones.Where(c => c.TenantId == empresa && c.Tipo == TipoComunicacion.DpOt).Select(c => c.Estado).ToListAsync(ct)))
            .Should().OnlyContain(e => e != EstadoComunicacion.Pendiente);
    }

    // ───────────────────────── Tope y reloj ─────────────────────────

    [Fact]
    public async Task El_tope_diario_deja_el_resto_para_el_siguiente_dia_habil()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = await EmpresaListaAsync(tope: 3);
        await ComparendosAsync(empresa, "AUTO-TOPE", 5);

        await CorrerAsync(empresa);
        (await DpsAsync(empresa)).Should().HaveCount(3, "con tope 3 y 5 elegibles salen 3");

        await CorrerAsync(empresa);
        (await DpsAsync(empresa)).Should().HaveCount(3, "otra corrida el mismo día no pasa del tope");

        api.Reloj.Hoy(Hoy.AddDays(1));
        await CorrerAsync(empresa);
        (await DpsAsync(empresa)).Should().HaveCount(5);
    }

    [Fact]
    public async Task Cuatro_corridas_simultaneas_con_tope_3_y_10_elegibles_envian_exactamente_3()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = await EmpresaListaAsync(tope: 3);
        await ComparendosAsync(empresa, "AUTO-TOPE4", 10);

        await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(() => CorrerAsync(empresa), TestContext.Current.CancellationToken)));

        var dps = await DpsAsync(empresa);
        dps.Should().HaveCount(3);
        dps.Select(d => d.ComparendoId).Distinct().Should().HaveCount(3);
    }

    [Fact]
    public async Task A_las_2330_y_a_las_0030_de_Colombia_son_dias_distintos_para_el_tope()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = await EmpresaListaAsync(tope: 2);
        await ComparendosAsync(empresa, "AUTO-MEDIANOCHE", 4);
        var offset = TimeSpan.FromHours(-5);

        api.Reloj.En(new DateTimeOffset(2026, 11, 18, 23, 30, 0, offset));
        await CorrerAsync(empresa);
        (await DpsAsync(empresa)).Should().HaveCount(2);

        api.Reloj.En(new DateTimeOffset(2026, 11, 18, 23, 59, 0, offset));
        await CorrerAsync(empresa);
        (await DpsAsync(empresa)).Should().HaveCount(2, "sigue siendo el mismo día");

        api.Reloj.En(new DateTimeOffset(2026, 11, 19, 0, 30, 0, offset));
        await CorrerAsync(empresa);
        (await DpsAsync(empresa)).Should().HaveCount(4, "pasada la medianoche de Colombia es otro día y otro tope");
    }

    [Fact]
    public async Task Los_dp_manuales_no_consumen_el_tope_del_automatico()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = await EmpresaListaAsync(tope: 2);
        var ids = (await ComparendosAsync(empresa, "AUTO-MANUAL", 5)).OrderBy(x => x.Key).Select(x => x.Value).ToList();
        foreach (var id in ids.Take(2))
            (await EnviarManualAsync(empresa, id)).EnsureSuccessStatusCode();

        await CorrerAsync(empresa);

        var dps = await DpsAsync(empresa);
        dps.Should().HaveCount(4, "2 manuales + 2 automáticos (el tope es solo de los automáticos)");
        dps.Count(d => d.EnvioAutomatico).Should().Be(2);
    }

    [Fact]
    public async Task Lo_que_queda_para_manana_son_los_comparendos_mas_recientes()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = await EmpresaListaAsync(tope: 2);
        var ids = await ComparendosAsync(empresa, "AUTO-ORDEN", 4);
        var baseFecha = api.Reloj.Ahora.AddDays(-10);
        // ORDEN-4 es el más antiguo y ORDEN-1 el más reciente.
        foreach (var (numero, id) in ids)
        {
            var n = int.Parse(numero[^1..], System.Globalization.CultureInfo.InvariantCulture);
            await api.EnDbAsync(db => db.Comparendos.Where(c => c.Id == id).ExecuteUpdateAsync(u => u.SetProperty(c => c.CreadoEn, baseFecha.AddHours(-n))));
        }

        await CorrerAsync(empresa);

        (await DpsAsync(empresa)).Select(d => d.ComparendoId).Should().BeEquivalentTo([ids["AUTOORDEN4"], ids["AUTOORDEN3"]], "salen los más antiguos");
    }

    // ───────────────────────── Duplicados y contención ─────────────────────────

    [Fact]
    public async Task Un_comparendo_bloqueado_por_otra_conexion_se_salta_sin_enviar_ni_dejar_huerfanos()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var empresa = await EmpresaListaAsync();
        var id = (await ComparendosAsync(empresa, "AUTO-BLOQUEO", 1)).Values.Single();
        var cadena = await api.EnDbAsync(db => Task.FromResult(db.Database.GetConnectionString()!));
        var radicados = await RadicadosAsync();
        var archivos = await ArchivosAsync(empresa);

        await using (var conexion = new NpgsqlConnection(cadena))
        {
            await conexion.OpenAsync(ct);
            await using var tx = await conexion.BeginTransactionAsync(ct);
            await using (var bloqueo = new NpgsqlCommand("SELECT 1 FROM comparendos.comparendo WHERE id = @id FOR UPDATE", conexion, tx))
            {
                bloqueo.Parameters.AddWithValue("id", id);
                await bloqueo.ExecuteScalarAsync(ct);
            }

            var reloj = Stopwatch.StartNew();
            await CorrerAsync(empresa);
            reloj.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(20), "SKIP LOCKED no espera al candado ajeno");

            (await DpsAsync(empresa)).Should().BeEmpty("otra conexión tenía el comparendo: el trabajo lo salta");
            (await RadicadosAsync()).Should().Be(radicados, "ningún radicado reservado");
            (await ArchivosAsync(empresa)).Should().Be(archivos, "ningún archivo huérfano");
            (await api.EnDbAsync(db => db.Comunicaciones.CountAsync(c => c.TenantId == empresa && c.Tipo == TipoComunicacion.DpOt, ct))).Should().Be(0);
            await tx.RollbackAsync(ct);
        }

        await CorrerAsync(empresa);
        (await DpsAsync(empresa)).Should().ContainSingle("liberado el candado, la siguiente corrida lo envía");
    }

    [Fact]
    public async Task Si_una_persona_inserta_el_dp_entre_el_reclamo_y_el_envio_no_quedan_radicados_ni_archivos_huerfanos()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = await EmpresaListaAsync();
        var id = (await ComparendosAsync(empresa, "AUTO-UNICO", 1)).Values.Single();
        var dci = await TipologiaAsync("DCI");
        var radicados = await RadicadosAsync();
        var archivos = await ArchivosAsync(empresa);
        // Justo antes de armar el PDF (candado ya puesto) otra conexión registra un DP del mismo comparendo y lo confirma.
        api.Ganchos.AlArmarDp = async () => await api.EnDbAsync(async db =>
        {
            db.DerechosPeticion.Add(new DerechoPeticion
            {
                Id = Guid.CreateVersion7(), TenantId = empresa, ComparendoId = id, TipologiaId = dci, PlantillaVersionId = Guid.NewGuid(), Radicado = $"PERSONA-{Guid.NewGuid():N}"[..30],
                OrganismoTransitoId = Guid.Parse(Organismo), DocumentoArchivoId = Guid.NewGuid(), EnviadoPor = Guid.NewGuid(), CreadoEn = api.Reloj.Ahora,
            });
            await db.SaveChangesAsync();
        });

        await CorrerAsync(empresa);

        var dps = await DpsAsync(empresa);
        dps.Should().ContainSingle().Which.EnvioAutomatico.Should().BeFalse("queda el de la persona; el del trabajo se descartó entero");
        (await RadicadosAsync()).Should().Be(radicados, "el radicado del trabajo se revirtió con la transacción");
        (await ArchivosAsync(empresa)).Should().Be(archivos);
        (await api.EnDbAsync(db => db.Comunicaciones.CountAsync(c => c.TenantId == empresa && c.Tipo == TipoComunicacion.DpOt))).Should().Be(0);
        (await api.EnDbAsync(db => db.Timeline.CountAsync(t => t.ComparendoId == id && t.Tipo == TipoEventoTimeline.DpAutomaticoOmitido))).Should().Be(0, "no es un error ni una omisión");
    }

    [Fact]
    public async Task Un_envio_del_trabajo_deja_exactamente_los_mismos_archivos_que_un_envio_manual()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = await EmpresaListaAsync();
        var ids = (await ComparendosAsync(empresa, "AUTO-ARCH", 2)).OrderBy(x => x.Key).Select(x => x.Value).ToList();

        var antesManual = await ArchivosAsync(empresa);
        (await EnviarManualAsync(empresa, ids[0])).EnsureSuccessStatusCode();
        var delta = await ArchivosAsync(empresa) - antesManual;
        delta.Should().BeGreaterThan(0);

        var antesJob = await ArchivosAsync(empresa);
        await CorrerAsync(empresa);

        (await DpsAsync(empresa)).Should().HaveCount(2);
        (await ArchivosAsync(empresa) - antesJob).Should().Be(delta, "el trabajo no deja ni más ni menos archivos que un envío manual");
    }

    /// <remarks>
    /// OJO: esta prueba NO garantiza que se ejerza el camino del deadlock (40P01) ni del índice único: pasa igual si el cruce nunca
    /// ocurre en las 20 vueltas. Lo que prueba es el resultado observable (un solo DP, el manual nunca da 5xx, el trabajo no falla).
    /// El índice único y el candado ajeno se ejercen de forma determinista en las dos pruebas de «contención» de más arriba.
    /// </remarks>
    [Fact]
    public async Task Envio_manual_y_trabajo_sobre_el_mismo_comparendo_dejan_un_solo_dp_y_el_manual_nunca_da_5xx()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = await EmpresaListaAsync();
        var estados = new List<int>();
        for (var i = 1; i <= 20; i++)
        {
            var id = (await ComparendosAsync(empresa, $"AUTO-CARRERA{i}", 1))[$"AUTOCARRERA{i}1"];
            var salida = new TaskCompletionSource();
            var manual = Task.Run(async () =>
            {
                await salida.Task;
                return await EnviarManualAsync(empresa, id);
            }, TestContext.Current.CancellationToken);
            var trabajo = Task.Run(async () =>
            {
                await salida.Task;
                await CorrerAsync(empresa);
            }, TestContext.Current.CancellationToken);
            salida.SetResult();
            await Task.WhenAll(manual, trabajo);

            var respuesta = await manual;
            estados.Add((int)respuesta.StatusCode);
            ((int)respuesta.StatusCode).Should().BeLessThan(500, $"iteración {i}: {await respuesta.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)}");
            (await api.EnDbAsync(db => db.DerechosPeticion.CountAsync(d => d.ComparendoId == id))).Should().Be(1, $"iteración {i}: un solo DP");
        }

        estados.Should().OnlyContain(e => e == 200 || e == 409 || e == 422);
        (await api.EnDbAsync(db => db.Timeline.Where(t => t.TenantId == empresa && t.Tipo == TipoEventoTimeline.DpAutomaticoOmitido).Select(t => t.Descripcion).ToListAsync()))
            .Should().BeEmpty("ningún error inesperado en el trabajo");
    }

    [Fact]
    public async Task Si_la_version_publicada_cambia_entre_la_decision_y_el_envio_no_sale_texto_sin_revisar()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = await EmpresaListaAsync();
        await ComparendosAsync(empresa, "AUTO-VERSION", 1);
        var radicados = await RadicadosAsync();
        api.Ganchos.ArmarSinRevision = true;

        await CorrerAsync(empresa);

        (await DpsAsync(empresa)).Should().BeEmpty("el PDF se habría armado con un texto sin la aprobación del abogado");
        (await RadicadosAsync()).Should().Be(radicados);
        (await api.EnDbAsync(db => db.Comunicaciones.CountAsync(c => c.TenantId == empresa && c.Tipo == TipoComunicacion.DpOt))).Should().Be(0);
    }

    // ───────────────────────── Quién entra y quién no ─────────────────────────

    [Fact]
    public async Task Pagados_inactivos_y_fotomultas_con_descuento_vigente_no_se_envian()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = await EmpresaListaAsync();
        var reciente = Item("AUTO-VENTANA", fotomulta: true) with { FechaImposicion = Hoy.AddDays(-3), FechaNotificacion = Hoy.AddDays(-1) };
        var ids = await IngresarAsync(api, empresa, Viejo("AUTO-PAGADO"), Viejo("AUTO-INACTIVO"), Viejo("AUTO-CONTROL"), reciente);
        await api.EnDbAsync(async db =>
        {
            (await db.Comparendos.SingleAsync(c => c.Id == ids["AUTOPAGADO"])).Pagado = true;
            var inactivo = await db.Comparendos.SingleAsync(c => c.Id == ids["AUTOINACTIVO"]);
            (inactivo.Activo, inactivo.MotivoInactivacion) = (false, MotivoInactivacion.InactivadoOperador);
            return await db.SaveChangesAsync();
        });

        await CorrerAsync(empresa);

        (await DpsAsync(empresa)).Select(d => d.ComparendoId).Should().Equal(ids["AUTOCONTROL"]);
    }

    [Fact]
    public async Task Sin_modulo_de_dp_o_con_el_producto_deshabilitado_no_se_envia()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var sinModulo = await EmpresaListaAsync();
        await ComparendosAsync(sinModulo, "AUTO-SINMOD", 1);
        await api.EnDbAsync(db => db.ConfiguracionesEmpresa.Where(e => e.TenantId == sinModulo).ExecuteUpdateAsync(u => u.SetProperty(e => e.HasRightPetitionModule, false)));
        var sinProducto = await EmpresaListaAsync();
        await ComparendosAsync(sinProducto, "AUTO-SINPROD", 1);
        api.Productos.Apagadas[sinProducto] = true;

        await CorrerAsync(sinModulo);
        await CorrerAsync(sinProducto);

        (await DpsAsync(sinModulo)).Should().BeEmpty("el automático está encendido pero el módulo de DP está apagado");
        (await DpsAsync(sinProducto)).Should().BeEmpty("el producto Comparendos está deshabilitado para la empresa");
    }

    [Fact]
    public async Task El_ultimo_resultado_del_trabajo_se_ve_y_una_empresa_saltada_por_producto_no_pasa_en_silencio()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        async Task<ResultadoCiclo?> UltimoAsync() => await api.EnDbAsync(db => db.TrabajosProgramados.Where(t => t.Tipo == TipoTrabajo.EnvioAutomaticoDp).Select(t => t.UltimoResultado).SingleAsync());
        var sana = await EmpresaListaAsync();
        await ComparendosAsync(sana, "AUTO-RES-SANA", 1);
        var sinProducto = await EmpresaListaAsync();
        await ComparendosAsync(sinProducto, "AUTO-RES-SINPROD", 1);

        await CorrerAsync(sana);
        (await UltimoAsync()).Should().Be(ResultadoCiclo.Exitoso);

        api.Productos.Apagadas[sinProducto] = true;
        await CorrerAsync(sinProducto);
        (await UltimoAsync()).Should().Be(ResultadoCiclo.Fallido, "la única empresa pedida se saltó: se ve en la pantalla de Trabajos, no en silencio");
        (await DpsAsync(sinProducto)).Should().BeEmpty();

        // Si Identidad "no responde" de verdad (gRPC no disponible) pasa lo mismo: ProductosHabilitadosIdentidad devuelve false.
        api.Productos.Apagadas.Clear();
        await CorrerAsync(sinProducto);
        (await UltimoAsync()).Should().Be(ResultadoCiclo.Exitoso);
        (await DpsAsync(sinProducto)).Should().ContainSingle();
    }

    [Fact]
    public async Task HU14621_El_resumen_del_DP_automatico_sale_con_el_marco_y_la_cabecera_de_la_empresa()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        // Resumen (sin firma → omisión → resumen) con marco FLIT y, después, con el conjunto Renting de la empresa.
        var empresa = await EmpresaListaAsync(conFirma: false);
        await AlertasAsync(empresa);
        await ComparendosAsync(empresa, "AUTO-MARCO", 1);
        async Task<string> ResumenAsync()
        {
            var c = (await api.EnDbAsync(db => db.Comunicaciones.AsNoTracking().Where(x => x.TenantId == empresa && x.Tipo == TipoComunicacion.ResumenEnvioAutomaticoDp).OrderByDescending(x => x.CreadoEn).ToListAsync(ct)))[0];
            await using var scope = api.App.Services.CreateAsyncScope();
            return System.Text.Encoding.UTF8.GetString((await scope.ServiceProvider.GetRequiredService<Flit.Comparendos.Api.Modulos.Comun.RegistroArchivos>().LeerAsync(c.CuerpoHtmlArchivoId, ct))!.Value.Contenido);
        }

        await CorrerAsync(empresa);
        (await ResumenAsync()).Should().Contain("flit-banner-correo.png");

        // El conjunto sigue al canal que Trámites tiene para la empresa (HU #13882): «API Renting cliente» ⇒ conjunto Renting.
        api.Empresas.Agregar(empresa, "Renting Colombia S.A.S", NitGestora, CanalCorreo.EmpresaApi);
        api.App.Services.GetRequiredService<Microsoft.Extensions.Caching.Memory.IMemoryCache>().Remove(CanalDeTramites.ClaveCache);
        api.Reloj.Hoy(Hoy.AddDays(1));
        await CorrerAsync(empresa);
        var renting = await ResumenAsync();
        renting.Should().Contain("renting-cabecera-dp.jpg").And.NotContain("renting-cabecera.jpg").And.NotContain("flit-banner-correo.png").And.Contain("Derechos de petici");
        renting.ToLowerInvariant().Should().NotContain("flit");
    }

    [Fact]
    public async Task Una_empresa_que_falla_no_frena_a_las_demas()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var rota = await EmpresaListaAsync();
        await ComparendosAsync(rota, "AUTO-ROTA", 1);
        var sana = await EmpresaListaAsync();
        await ComparendosAsync(sana, "AUTO-SANA", 1);
        api.Productos.Fallan[rota] = true;

        await CorrerAsync(null);

        (await DpsAsync(rota)).Should().BeEmpty();
        (await DpsAsync(sana)).Should().ContainSingle("la otra empresa se procesó igual");
    }

    [Fact]
    public async Task Un_timeout_de_una_empresa_no_tumba_a_las_demas_pero_una_cancelacion_real_si_se_propaga()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var lenta = await EmpresaListaAsync();
        await ComparendosAsync(lenta, "AUTO-LENTA", 1);
        var normal = await EmpresaListaAsync();
        await ComparendosAsync(normal, "AUTO-NORMAL", 1);
        api.Productos.Expiran[lenta] = true;

        await CorrerAsync(null);

        (await DpsAsync(lenta)).Should().BeEmpty();
        (await DpsAsync(normal)).Should().ContainSingle("el timeout de otra empresa (TaskCanceledException sin cancelación) no la frena");

        using var cancelada = new CancellationTokenSource();
        await cancelada.CancelAsync();
        await using var scope = api.App.Services.CreateAsyncScope();
        var ejecutor = scope.ServiceProvider.GetServices<IEjecutorTrabajo>().OfType<EnvioAutomaticoDp>().Single();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ejecutor.EjecutarAsync(new SolicitudTrabajo(TipoTrabajo.EnvioAutomaticoDp, Disparo.Programado, normal), cancelada.Token));
    }

    [Fact]
    public async Task Una_colision_de_radicado_es_un_error_real_no_un_falso_ya_tiene_dp()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = await EmpresaListaAsync();
        await AlertasAsync(empresa);
        var ids = await ComparendosAsync(empresa, "AUTO-RADICADO", 3);
        var ordenados = ids.OrderBy(x => x.Key).Select(x => x.Value).ToList();
        var dci = await TipologiaAsync("DCI");
        // Un DP ya existente (p. ej. migrado de V1) tiene justo el radicado que le tocaría al siguiente envío: la reserva no lo ve.
        var momento = new DateTimeOffset(2026, 11, 18, 10, 15, 7, 123, TimeSpan.FromHours(-5));
        api.Reloj.En(momento);
        await api.EnDbAsync(async db =>
        {
            db.DerechosPeticion.Add(new DerechoPeticion
            {
                Id = Guid.CreateVersion7(), TenantId = empresa, ComparendoId = ordenados[0], TipologiaId = dci, PlantillaVersionId = Guid.NewGuid(),
                Radicado = ServicioDp.FormatoRadicado("DCI", momento), OrganismoTransitoId = Guid.Parse(Organismo), DocumentoArchivoId = Guid.NewGuid(), EnviadoPor = Guid.NewGuid(), CreadoEn = momento.ToUniversalTime(), Estado = EstadoDp.Enviado,
            });
            (await db.Comparendos.SingleAsync(c => c.Id == ordenados[0])).TieneDp = true;
            await db.SaveChangesAsync();
            return 0;
        });

        // El trabajo va primero (su reserva de radicado se revierte con su transacción): la colisión es un ERROR_ENVIO anotado, no un Saltado silencioso.
        await CorrerAsync(empresa);
        (await DpsAsync(empresa)).Should().ContainSingle("solo queda el DP sembrado: los dos del trabajo chocaron con el radicado");
        var errores = await api.EnDbAsync(db => db.Timeline.Where(t => t.TenantId == empresa && t.Tipo == TipoEventoTimeline.DpAutomaticoOmitido).Select(t => t.Datos).ToListAsync());
        errores.Should().HaveCount(2).And.OnlyContain(d => d!.Contains("ERROR_ENVIO"));

        // Manual: es un error del servidor de verdad, no un 409 YA_TIENE_DP mentiroso.
        var manual = await EnviarManualAsync(empresa, ordenados[1]);
        ((int)manual.StatusCode).Should().BeGreaterThanOrEqualTo(500);
        (await DpsAsync(empresa)).Should().ContainSingle();
    }

    [Fact]
    public async Task Cinco_errores_seguidos_cortan_la_corrida_con_una_sola_alerta_clara()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = await EmpresaListaAsync();
        await AlertasAsync(empresa);
        await ComparendosAsync(empresa, "AUTO-SISTEMICO", 20);
        var llamadas = 0;
        api.Ganchos.AlArmarDp = () =>
        {
            Interlocked.Increment(ref llamadas);
            throw new InvalidOperationException("El servicio de archivos no responde (prueba).");
        };

        await CorrerAsync(empresa);

        llamadas.Should().Be(EnvioAutomaticoDp.MaxErroresConsecutivos, "se corta al llegar al límite, no se intentan los 20");
        (await DpsAsync(empresa)).Should().BeEmpty();
        (await api.EnDbAsync(db => db.Timeline.CountAsync(t => t.TenantId == empresa && t.Tipo == TipoEventoTimeline.DpAutomaticoOmitido))).Should().Be(EnvioAutomaticoDp.MaxErroresConsecutivos);
        var resumenes = await api.EnDbAsync(db => db.Comunicaciones.AsNoTracking().Where(c => c.TenantId == empresa && c.Tipo == TipoComunicacion.ResumenEnvioAutomaticoDp).ToListAsync());
        resumenes.Should().ContainSingle();
        await using var scope = api.App.Services.CreateAsyncScope();
        var cuerpo = await scope.ServiceProvider.GetRequiredService<Flit.Comparendos.Api.Modulos.Comun.RegistroArchivos>().LeerAsync(resumenes[0].CuerpoHtmlArchivoId, TestContext.Current.CancellationToken);
        System.Text.Encoding.UTF8.GetString(cuerpo!.Value.Contenido).Should().Contain("se detuvo por errores repetidos");
    }

    [Fact]
    public async Task Un_envio_exitoso_reinicia_el_contador_de_errores_seguidos()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = await EmpresaListaAsync();
        await ComparendosAsync(empresa, "AUTO-INTERCALADO", 20);
        var llamadas = 0;
        // 4 fallos y 1 éxito, una y otra vez: nunca hay 5 seguidos.
        api.Ganchos.AlArmarDp = () => Interlocked.Increment(ref llamadas) % 5 == 0 ? Task.CompletedTask : throw new InvalidOperationException("Falla intermitente (prueba).");

        await CorrerAsync(empresa);

        llamadas.Should().Be(20, "no se cortó");
        (await DpsAsync(empresa)).Should().HaveCount(4);
    }

    [Fact]
    public async Task El_error_de_un_comparendo_no_frena_a_los_demas_y_el_resumen_lo_cuenta()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = await EmpresaListaAsync();
        await AlertasAsync(empresa);
        var ids = await ComparendosAsync(empresa, "AUTO-ERROR", 3);
        var llamadas = 0;
        api.Ganchos.AlArmarDp = () => Interlocked.Increment(ref llamadas) == 1 ? throw new InvalidOperationException("Falla de prueba al generar el documento.") : Task.CompletedTask;

        await CorrerAsync(empresa);

        (await DpsAsync(empresa)).Should().HaveCount(2, "el primero falló y los otros dos salieron");
        var errores = await api.EnDbAsync(db => db.Timeline.AsNoTracking().Where(t => t.TenantId == empresa && t.Tipo == TipoEventoTimeline.DpAutomaticoOmitido).ToListAsync());
        errores.Should().ContainSingle().Which.Datos.Should().Contain("ERROR_ENVIO");
        errores[0].Descripcion.Should().NotContain("Falla de prueba", "al usuario no se le muestra el detalle técnico");
        (await ResumenesAsync(empresa)).Should().Be(1, "el resumen cuenta el error");
        ids.Should().HaveCount(3);

        // Al día siguiente, sin el fallo, sale el que había quedado.
        api.Ganchos.AlArmarDp = null;
        api.Reloj.Hoy(Hoy.AddDays(1));
        await CorrerAsync(empresa);
        (await DpsAsync(empresa)).Should().HaveCount(3);
    }

    [Fact]
    public async Task Apagar_el_interruptor_a_mitad_de_la_corrida_la_corta()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = await EmpresaListaAsync();
        await ComparendosAsync(empresa, "AUTO-CORTE", 4);
        var llamadas = 0;
        api.Ganchos.AlArmarDp = async () =>
        {
            if (Interlocked.Increment(ref llamadas) == 1)
                await api.EnDbAsync(db => db.ConfiguracionesEmpresa.Where(e => e.TenantId == empresa).ExecuteUpdateAsync(u => u.SetProperty(e => e.DpAutomaticoActivo, false)));
        };

        await CorrerAsync(empresa);

        (await DpsAsync(empresa)).Should().ContainSingle("el que ya estaba en curso termina; el resto no sale");
    }

    // ───────────────────────── Omisiones, resumen y rescate ─────────────────────────

    [Fact]
    public async Task Un_comparendo_que_no_se_puede_enviar_se_omite_con_su_motivo_una_sola_vez()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = await EmpresaListaAsync(conRepresentante: false);
        var id = (await ComparendosAsync(empresa, "AUTO-OMITE", 1)).Values.Single();

        await CorrerAsync(empresa);
        await CorrerAsync(empresa);

        (await DpsAsync(empresa)).Should().BeEmpty();
        var eventos = await api.EnDbAsync(db => db.Timeline.AsNoTracking().Where(t => t.ComparendoId == id && t.Tipo == TipoEventoTimeline.DpAutomaticoOmitido).ToListAsync());
        eventos.Should().ContainSingle("el mismo motivo no se repite cada día").Which.Descripcion.Should().Contain("representante legal");
    }

    [Fact]
    public async Task Sin_representante_o_sin_su_firma_se_omite_con_el_motivo_claro()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var sinFirma = await EmpresaListaAsync(conFirma: false);
        var idFirma = (await ComparendosAsync(sinFirma, "AUTO-SINFIRMA", 1)).Values.Single();
        var sinRl = await EmpresaListaAsync(conRepresentante: false);
        var idRl = (await ComparendosAsync(sinRl, "AUTO-SINRL", 1)).Values.Single();

        await CorrerAsync(sinFirma);
        await CorrerAsync(sinRl);

        (await DpsAsync(sinFirma)).Should().BeEmpty();
        (await DpsAsync(sinRl)).Should().BeEmpty();
        (await api.EnDbAsync(db => db.Timeline.Where(t => t.ComparendoId == idFirma && t.Tipo == TipoEventoTimeline.DpAutomaticoOmitido).Select(t => t.Descripcion).SingleAsync(ct))).Should().Contain("firma");
        (await api.EnDbAsync(db => db.Timeline.Where(t => t.ComparendoId == idRl && t.Tipo == TipoEventoTimeline.DpAutomaticoOmitido).Select(t => t.Descripcion).SingleAsync(ct))).Should().Contain("representante legal");
    }

    [Fact]
    public async Task Un_organismo_que_no_admite_dp_por_correo_no_se_toca_y_el_resumen_avisa_una_sola_vez_al_dia()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = await EmpresaListaAsync(admiteOrganismo: false);
        await AlertasAsync(empresa);
        var ids = await ComparendosAsync(empresa, "AUTO-NOADMITE", 2);

        await CorrerAsync(empresa);
        await CorrerAsync(empresa);

        (await DpsAsync(empresa)).Should().BeEmpty();
        (await api.EnDbAsync(db => db.Timeline.CountAsync(t => ids.Values.Contains(t.ComparendoId) && t.Tipo == TipoEventoTimeline.DpAutomaticoOmitido)))
            .Should().Be(0, "no se recorren uno por uno: se cuentan por organismo en el resumen");
        var resumenes = await api.EnDbAsync(db => db.Comunicaciones.AsNoTracking().Where(c => c.TenantId == empresa && c.Tipo == TipoComunicacion.ResumenEnvioAutomaticoDp).ToListAsync());
        resumenes.Should().ContainSingle("una sola alerta por empresa y día aunque el trabajo corra dos veces");
        resumenes[0].Destinatarios.Should().Contain("alertas@gestora.test");
        await using (var scope = api.App.Services.CreateAsyncScope())
        {
            var cuerpo = await scope.ServiceProvider.GetRequiredService<Flit.Comparendos.Api.Modulos.Comun.RegistroArchivos>().LeerAsync(resumenes[0].CuerpoHtmlArchivoId, TestContext.Current.CancellationToken);
            var html = System.Text.Encoding.UTF8.GetString(cuerpo!.Value.Contenido);
            html.Should().Contain("<b>2</b> en ").And.Contain("marca el organismo").And.Contain("como que admite derechos de petici");
        }

        api.Reloj.Hoy(Hoy.AddDays(1));
        await CorrerAsync(empresa);
        (await ResumenesAsync(empresa)).Should().Be(2, "un aviso por día mientras siga igual");
    }

    [Fact]
    public async Task El_resumen_usa_a_quienes_gestionan_representantes_si_no_hay_correos_de_alerta_y_no_envia_si_no_hay_nadie()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var conGestores = await EmpresaListaAsync(conFirma: false);
        await ComparendosAsync(conGestores, "AUTO-GEST", 1);
        api.Usuarios.PorEmpresa[conGestores] = [new UsuarioConPermiso("gestor@gestora.test", "Gestor de representantes")];
        var sinNadie = await EmpresaListaAsync(conFirma: false);
        await ComparendosAsync(sinNadie, "AUTO-NADIE", 1);

        await CorrerAsync(conGestores);
        await CorrerAsync(sinNadie);

        var resumen = await api.EnDbAsync(db => db.Comunicaciones.AsNoTracking().Where(c => c.TenantId == conGestores && c.Tipo == TipoComunicacion.ResumenEnvioAutomaticoDp).ToListAsync());
        resumen.Should().ContainSingle().Which.Destinatarios.Should().Contain("gestor@gestora.test");
        (await ResumenesAsync(sinNadie)).Should().Be(0, "sin destinatarios no se envía nada");
    }

    [Fact]
    public async Task Un_dp_automatico_colgado_se_marca_a_las_dos_horas_no_antes_ni_si_fue_manual_y_va_en_el_resumen()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = await EmpresaListaAsync();
        await AlertasAsync(empresa);
        var ids = await ComparendosAsync(empresa, "AUTO-COLGADO", 3);
        await CorrerAsync(empresa);
        var dps = (await DpsAsync(empresa)).OrderBy(d => d.Id).ToList();
        dps.Should().HaveCount(3);
        var ahora = api.Reloj.Ahora;
        async Task Colgar(Guid dp, TimeSpan edad, bool automatico) =>
            await api.EnDbAsync(db => db.DerechosPeticion.Where(d => d.Id == dp)
                .ExecuteUpdateAsync(u => u.SetProperty(d => d.Estado, EstadoDp.Enviando).SetProperty(d => d.CreadoEn, ahora - edad).SetProperty(d => d.EnvioAutomatico, automatico)));
        await Colgar(dps[0].Id, TimeSpan.FromHours(1), true);
        await Colgar(dps[1].Id, TimeSpan.FromHours(3), true);
        await Colgar(dps[2].Id, TimeSpan.FromHours(3), false);
        var correosAntes = await api.EnDbAsync(db => db.Comunicaciones.CountAsync(c => c.TenantId == empresa && c.Tipo == TipoComunicacion.DpOt));

        await CorrerAsync(empresa);
        await CorrerAsync(empresa);

        var marcados = await api.EnDbAsync(db => db.Timeline.Where(t => t.TenantId == empresa && t.Tipo == TipoEventoTimeline.DpEnviandoSinReaccion).Select(t => t.RelacionadoId).ToListAsync());
        marcados.Should().Equal(dps[1].Id);
        (await DpsAsync(empresa)).Should().OnlyContain(d => d.Estado == EstadoDp.Enviando, "no se tocó el estado");
        (await api.EnDbAsync(db => db.Comunicaciones.CountAsync(c => c.TenantId == empresa && c.Tipo == TipoComunicacion.DpOt))).Should().Be(correosAntes, "no se reenvía");
        (await ResumenesAsync(empresa)).Should().Be(1, "el resumen sale aunque lo único sea un DP sin reacción");
        ids.Should().HaveCount(3);
    }

    // ───────────────────────── Aprobación jurídica y rendimiento ─────────────────────────

    [Fact]
    public async Task Aprobar_otra_vez_una_version_ya_aprobada_conserva_la_primera_aprobacion()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        await EmpresaListaAsync();
        var dci = await TipologiaAsync("DCI");
        var plantillas = await Super.GetFromJsonAsync<JsonElement>($"{Base}/plantillas?conjunto=Flit&tipo=DpDocumento", ct);
        var version = plantillas.EnumerateArray().Single(p => p.GetProperty("tipologiaId").GetGuid() == dci).GetProperty("publicada").GetProperty("id").GetGuid();
        var antes = await api.EnDbAsync(db => db.PlantillaVersiones.AsNoTracking().Where(v => v.Id == version).Select(v => new { v.RevisadaJuridicamenteEn, v.NotaRevisionJuridica, v.RevisadaJuridicamentePor }).SingleAsync(ct));

        var otra = await Super.PostAsJsonAsync($"{Base}/admin/plantillas/versiones/{version}/revision-juridica", new { nota = "Otra nota distinta" }, ct);

        otra.EnsureSuccessStatusCode();
        var despues = await api.EnDbAsync(db => db.PlantillaVersiones.AsNoTracking().Where(v => v.Id == version).Select(v => new { v.RevisadaJuridicamenteEn, v.NotaRevisionJuridica, v.RevisadaJuridicamentePor }).SingleAsync(ct));
        despues.Should().BeEquivalentTo(antes, "la segunda aprobación no reescribe fecha, quién ni nota");
        despues.NotaRevisionJuridica.Should().NotBe("Otra nota distinta");
    }

    /// <summary>Clona directo en la base un comparendo ya ingresado hasta tener <paramref name="total"/> (ingresar 2.000 por la API no es lo que se mide).</summary>
    private async Task SembrarAsync(Guid empresa, Guid plantilla, int total)
    {
        await api.EnDbAsync(async db =>
        {
            var original = await db.Comparendos.AsNoTracking().SingleAsync(c => c.Id == plantilla);
            for (var i = 1; i < total; i++)
            {
                var copia = new Comparendo();
                db.Entry(copia).CurrentValues.SetValues(original);
                copia.Id = Guid.CreateVersion7();
                copia.Numero = $"{original.Numero}-{i}";
                copia.NumeroNormalizado = $"{original.NumeroNormalizado}X{i}";
                copia.CreadoEn = original.CreadoEn.AddSeconds(i);
                db.Comparendos.Add(copia);
            }

            await db.SaveChangesAsync();
            return 0;
        });
    }

    /// <summary>
    /// Límite de la prueba de rendimiento: 30 s en una máquina de desarrollo y 120 s en el CI (GitHub Actions define
    /// CI=true). El runner compartido de GitHub tarda entre 36 s y 1 min 30 s con el mismo código, así que con 30 s la
    /// prueba fallaba por la máquina y no por el trabajo. 120 s sigue atrapando una regresión real (minutos).
    /// </summary>
    private static readonly TimeSpan LimiteRendimiento =
        TimeSpan.FromSeconds(Environment.GetEnvironmentVariable("CI") == "true" ? 120 : 30);

    [Fact]
    public async Task Rendimiento_con_2000_comparendos_sin_dp_la_corrida_termina_en_un_tiempo_razonable()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        // (b1) Peor caso evitable: el automático está encendido pero ningún organismo admite DP por correo.
        var sinOrganismo = await EmpresaListaAsync(admiteOrganismo: false);
        var creacion = Stopwatch.StartNew();
        await SembrarAsync(sinOrganismo, (await ComparendosAsync(sinOrganismo, "AUTO-PERFA", 1)).Values.Single(), 2000);
        creacion.Stop();
        (await api.EnDbAsync(db => db.Comparendos.CountAsync(c => c.TenantId == sinOrganismo))).Should().Be(2000);
        var a = Stopwatch.StartNew();
        await CorrerAsync(sinOrganismo);
        a.Stop();
        (await DpsAsync(sinOrganismo)).Should().BeEmpty();

        // (b2) Carga real: los 2.000 son elegibles; sin firma todos se omiten (cada uno con su anotación), el peor caso por comparendo.
        var conOrganismo = await EmpresaListaAsync(tope: 50, conFirma: false);
        await SembrarAsync(conOrganismo, (await ComparendosAsync(conOrganismo, "AUTO-PERFB", 1)).Values.Single(), 2000);
        var b = Stopwatch.StartNew();
        await CorrerAsync(conOrganismo);
        b.Stop();
        (await api.EnDbAsync(db => db.Timeline.CountAsync(t => t.TenantId == conOrganismo && t.Tipo == TipoEventoTimeline.DpAutomaticoOmitido))).Should().Be(2000);

        // Segundo día: las mismas omisiones, sin anotaciones nuevas.
        api.Reloj.Hoy(Hoy.AddDays(1));
        var segundo = Stopwatch.StartNew();
        await CorrerAsync(conOrganismo);
        segundo.Stop();
        (await api.EnDbAsync(db => db.Timeline.CountAsync(t => t.TenantId == conOrganismo && t.Tipo == TipoEventoTimeline.DpAutomaticoOmitido))).Should().Be(2000, "el segundo día no agrega anotaciones");
        segundo.Elapsed.Should().BeLessThan(LimiteRendimiento);

        // Con firma: sale el tope (50) y el resto espera.
        var rlId = await api.EnDbAsync(db => db.RepresentantesLegales.Where(r => r.TenantId == conOrganismo).Select(r => r.Id).SingleAsync());
        const string png = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==";
        using var firma = new MultipartFormDataContent { { new ByteArrayContent(Convert.FromBase64String(png)) { Headers = { ContentType = new MediaTypeHeaderValue("image/png") } }, "archivo", "firma.png" } };
        (await api.Cliente(new UsuarioDePrueba(conOrganismo, Operador)).PostAsync($"{Base}/representantes/{rlId}/documentos/firma", firma, TestContext.Current.CancellationToken)).EnsureSuccessStatusCode();
        var c = Stopwatch.StartNew();
        await CorrerAsync(conOrganismo);
        c.Stop();
        (await DpsAsync(conOrganismo)).Should().HaveCount(50);

        TestContext.Current.TestOutputHelper?.WriteLine($"RENDIMIENTO 2000 comparendos: crear datos {creacion.Elapsed.TotalSeconds:0.0}s; trabajo sin organismo que admita {a.Elapsed.TotalSeconds:0.0}s; trabajo 100% elegibles todos omitidos {b.Elapsed.TotalSeconds:0.0}s; segundo día {segundo.Elapsed.TotalSeconds:0.0}s; con firma y tope 50 {c.Elapsed.TotalSeconds:0.0}s");
        a.Elapsed.Should().BeLessThan(LimiteRendimiento);
        b.Elapsed.Should().BeLessThan(LimiteRendimiento);
    }
}
