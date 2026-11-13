using System.Net;
using System.Text.Json;
using Flit.PersonaNatural.Api.Adjuntos;
using Flit.PersonaNatural.Api.Pagos;
using Flit.PersonaNatural.Api.Persistence;
using Flit.PersonaNatural.Api.Punto;
using Flit.PersonaNatural.Api.Radicacion;
using Flit.PersonaNatural.Api.Solicitudes;
using Flit.Platform.Sdk.Messaging;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Flit.PersonaNatural.Tests;

/// <summary>Canal del punto de prueba: resuelve el código <c>AB1</c> al organismo de prueba.</summary>
internal sealed class CanalPuntoDePrueba : ICanalPunto
{
    public const string Codigo = "AB1";

    public Task<ResolucionCanalPunto> ResolverAsync(string codigo, CancellationToken ct) =>
        Task.FromResult(new ResolucionCanalPunto("ok", Guid.Parse(PortalHost.Organismo), Codigo, "Tránsito de Prueba"));
}

/// <summary>Compuerta que se enciende y apaga a mano, para comprobar que el pago sobrevive a una radicación que falla.</summary>
internal sealed class CompuertaConmutable : ICompuertaDeRadicacion
{
    public bool Permite = true;

    public Task<ResultadoCompuerta> EvaluarAsync(Guid solicitudId, CancellationToken ct) =>
        Task.FromResult(Permite ? ResultadoCompuerta.Aprobada : ResultadoCompuerta.Bloqueada(MotivosCompuerta.IdentidadPendiente, [new ParteFaltante("vendedor", 1, "pendiente")]));
}

/// <summary>
/// Feature #14447 — pago en caja del punto. HU #14448 (estados de pago), #14449 (habilitar el cobro con el ID del trámite),
/// #14450 (confirmar un trámite en $0) y #14468 (radicar solo con el pago confirmado). Sobre el host real, una base efímera
/// propia y las fachadas de core-api falsas.
/// </summary>
public sealed partial class PagoPuntoTests : IAsyncLifetime
{
    private const string Base = "/api/v1/persona-natural";
    private const string Punto = Base + "/punto";
    private const string Vendedor = "7001234567";
    private const string Comprador = "7009876543";

    private readonly AlmacenEnMemoria _almacen = new();
    private readonly EnviadorFalso _enviador = new();
    private readonly CompuertaConmutable _compuerta = new();
    private PortalHost _host = null!;
    private bool _iniciado;
    private string _cookie = string.Empty;

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        if (_iniciado)
            await _host.DisposeAsync();
    }

    private async Task IniciarAsync(bool compuertaConmutable = false, (string Clave, string Valor)[]? extras = null)
    {
        _host = await PortalHost.IniciarAsync("pnpago", (servicios, _) =>
        {
            servicios.AddSingleton<IAlmacenAdjuntos>(_almacen);
            servicios.AddSingleton<Flit.PersonaNatural.Api.Cotizaciones.IEnviadorCotizacion>(_enviador);
            servicios.AddSingleton<ICanalPunto>(new CanalPuntoDePrueba());
            if (compuertaConmutable)
                servicios.AddSingleton<ICompuertaDeRadicacion>(_compuerta);
            // Varias pruebas avanzan el reloj un día: la sesión del trámite no debe vencer por eso.
        }, [("Sesion:VigenciaMinutos", "100000"), ("Sesion:TopeAbsolutoHoras", "1000"), ("Caja:Habilitado", "true"), .. extras ?? []]);
        _iniciado = true;
        _host.Definiciones.PorCodigo["TRASPASO_STANDARD"] = DefinicionesDePrueba.Traspaso();
        var entrada = await _host.EnviarAsync(HttpMethod.Post, $"{Punto}/entrada", new { codigo = CanalPuntoDePrueba.Codigo });
        entrada.StatusCode.Should().Be(HttpStatusCode.OK);
        _cookie = entrada.Headers.GetValues("Set-Cookie").First().Split(';')[0];
    }

    // ── HU #14448: estados de pago ────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task HU14448_ConUnaIdentidadPendiente_ResponderConflictoConLasPartes_YNoCambiaElEstado()
    {
        await IniciarAsync();
        var (id, token) = await CompletaAsync(validar: false);

        var r = await Pago(HttpMethod.Post, "/iniciar", token);

        r.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var json = (await PortalHost.JsonAsync(r)).RootElement;
        json.GetProperty("code").GetString().Should().Be("identity_pending");
        json.GetProperty("partes").GetArrayLength().Should().BeGreaterThan(0);
        (await EstadoDeAsync(id)).Should().Be("borrador");
    }

    [Fact]
    public async Task HU14448_ConLosRequisitosIncompletos_ResponderConflictoConLoPendiente_YNoCambiaElEstado()
    {
        await IniciarAsync();
        var (id, token) = await CompletaAsync(conSoat: false);

        var r = await Pago(HttpMethod.Post, "/iniciar", token);

        r.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var json = (await PortalHost.JsonAsync(r)).RootElement;
        json.GetProperty("code").GetString().Should().Be("requisitos_incompletos");
        json.GetProperty("pendientes").GetArrayLength().Should().BeGreaterThan(0);
        (await EstadoDeAsync(id)).Should().Be("borrador");
    }

    [Fact]
    public async Task HU14448_ConTodoValidado_PasaAPendientePago_YQuedaEnElHistorial_Idempotente()
    {
        await IniciarAsync();
        var (id, token) = await CompletaAsync();

        var r = await Pago(HttpMethod.Post, "/iniciar", token);
        var otra = await Pago(HttpMethod.Post, "/iniciar", token);

        r.StatusCode.Should().Be(HttpStatusCode.OK);
        otra.StatusCode.Should().Be(HttpStatusCode.OK, "repetir la petición no cambia nada");
        var json = (await PortalHost.JsonAsync(r)).RootElement;
        json.GetProperty("estado").GetString().Should().Be("pendiente_pago");
        json.GetProperty("puedePagar").GetBoolean().Should().BeTrue();
        json.GetProperty("totalCop").GetInt64().Should().Be(366_000);
        json.GetProperty("desglose").GetArrayLength().Should().Be(2);
        await using var db = _host.NuevaDb();
        (await db.Set<HistorialEstado>().CountAsync(h => h.SolicitudId == id && h.Hasta == "pendiente_pago", TestContext.Current.CancellationToken)).Should().Be(1);
    }

    [Fact]
    public async Task HU14448_ElEstadoDePagoAntesDePagarListaLasIdentidadesQueFaltan_SinDesglose()
    {
        await IniciarAsync();
        var (_, token) = await CompletaAsync(validar: false);

        var r = await Pago(HttpMethod.Get, string.Empty, token);

        r.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = (await PortalHost.JsonAsync(r)).RootElement;
        json.GetProperty("puedePagar").GetBoolean().Should().BeFalse();
        json.GetProperty("identidadesPendientes").GetArrayLength().Should().BeGreaterThan(0);
        json.TryGetProperty("desglose", out _).Should().BeFalse();
    }

    [Fact]
    public async Task HU14448_UnPagoConfirmado_DejaPagada_ConValorMedioYFecha_YNoAdmiteOtro()
    {
        await IniciarAsync(compuertaConmutable: true);
        var (id, token) = await EnPagoAsync();
        await Pago(HttpMethod.Post, "/caja", token);
        _compuerta.Permite = false; // sin radicación automática: se observa el estado «pagada»

        var pagado = await PagarCajaAsync(id);

        pagado.ValorCop.Should().Be(366_000);
        pagado.EstadoSolicitud.Should().Be("pagada");
        await using (var db = _host.NuevaDb())
        {
            var cobro = await db.Set<CobroSolicitud>().SingleAsync(c => c.Estado == "pagado", TestContext.Current.CancellationToken);
            cobro.ValorCop.Should().Be(366_000);
            cobro.Medio.Should().Be("caja");
            cobro.MedioCaja.Should().Be("efectivo");
            cobro.PagadoPor.Should().Be("caja-1");
            cobro.PagadoEn.Should().Be(_host.Reloj.GetUtcNow());
            (await db.Set<HistorialEstado>().CountAsync(h => h.Hasta == "pagada", TestContext.Current.CancellationToken)).Should().Be(1);
        }

        await FluentActions.Awaiting(() => PagarCajaAsync(id)).Should().ThrowAsync<ReglaDeNegocioException>().Where(e => e.Codigo == "ya_pagado");
    }

    [Fact]
    public async Task HU14448_LaWebSigueSinEstadosDePago()
    {
        await IniciarAsync();
        var (id, token) = await CompletaAsync(canalPunto: false);

        var r = await _host.EnviarAsync(HttpMethod.Post, $"{Base}/solicitudes/{id}/radicar", null, token, false, [("Idempotency-Key", "k-web")]);

        r.StatusCode.Should().Be(HttpStatusCode.Accepted);
        (await EstadoDeAsync(id)).Should().Be("en_cola");
        await using var db = _host.NuevaDb();
        (await db.Set<CobroSolicitud>().CountAsync(TestContext.Current.CancellationToken)).Should().Be(0);
        (await db.Set<HistorialEstado>().CountAsync(h => h.Hasta == "pendiente_pago" || h.Hasta == "pagada", TestContext.Current.CancellationToken)).Should().Be(0);
    }

    [Fact]
    public async Task HU14448_UnaSolicitudDeOtroOrganismoNoPuedeVerNiPagarse()
    {
        await IniciarAsync();
        var (id, token) = await CompletaAsync();
        await using (var db = _host.NuevaDb())
        {
            await db.Set<SolicitudTramite>().Where(s => s.Id == id).ExecuteUpdateAsync(set => set.SetProperty(s => s.OrganismoId, Guid.NewGuid()), TestContext.Current.CancellationToken);
        }

        (await Pago(HttpMethod.Get, string.Empty, token)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await Pago(HttpMethod.Post, "/iniciar", token)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await Pago(HttpMethod.Post, "/caja", token)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task HU14448_SinCookieDelPuntoNoHayPago()
    {
        await IniciarAsync();
        var (_, token) = await CompletaAsync();

        var r = await _host.EnviarAsync(HttpMethod.Get, $"{Punto}/pago", null, token);

        r.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── HU #14449: habilitar el cobro en caja ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task HU14449_Habilitar_ResponderElIdDelTramite_ElDesglose_ElTotal_YElVencimientoAMedianoche()
    {
        await IniciarAsync();
        var (id, token) = await EnPagoAsync();

        var r = await Pago(HttpMethod.Post, "/caja", token);

        r.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = (await PortalHost.JsonAsync(r)).RootElement;
        json.GetProperty("codigoCobro").GetString().Should().Be(await CodigoPublicoAsync(id)).And.StartWith("PN-");
        json.GetProperty("totalCop").GetInt64().Should().Be(366_000);
        json.GetProperty("desglose").GetArrayLength().Should().Be(2);
        // 15:00 UTC = 10:00 en Colombia: vale hasta 23:59:59 de ese día (04:59:59 UTC del siguiente).
        json.GetProperty("venceEn").GetDateTimeOffset().Should().Be(new DateTimeOffset(2026, 10, 27, 23, 59, 59, TimeSpan.FromHours(-5)));
        json.GetProperty("venceEn").GetDateTimeOffset().UtcDateTime.Should().Be(new DateTime(2026, 10, 28, 4, 59, 59, DateTimeKind.Utc));
        _host.Medios.UltimoAmbito.Should().Be(PortalHost.Organismo, "se cotiza con el ámbito del organismo");
    }

    [Fact]
    public async Task HU14449_HabilitarDosVeces_EnElMismoDia_DevuelveElMismoCobro()
    {
        await IniciarAsync();
        var (_, token) = await EnPagoAsync();

        var primero = (await PortalHost.JsonAsync(await Pago(HttpMethod.Post, "/caja", token))).RootElement.Clone();
        _host.Reloj.Avanzar(TimeSpan.FromHours(2));
        var segundo = (await PortalHost.JsonAsync(await Pago(HttpMethod.Post, "/caja", token))).RootElement.Clone();

        segundo.GetProperty("venceEn").GetDateTimeOffset().Should().Be(primero.GetProperty("venceEn").GetDateTimeOffset());
        segundo.GetProperty("habilitadoEn").GetDateTimeOffset().Should().Be(primero.GetProperty("habilitadoEn").GetDateTimeOffset(), "el código vigente no se mueve");
        await using var db = _host.NuevaDb();
        (await db.Set<CobroSolicitud>().CountAsync(TestContext.Current.CancellationToken)).Should().Be(1);
    }

    [Fact]
    public async Task HU14449_SiCambiaLaTarifaAntesDePagar_ElTotalQueSeConsultaEsElNuevo()
    {
        await IniciarAsync();
        var (_, token) = await EnPagoAsync();
        await Pago(HttpMethod.Post, "/caja", token);

        _host.Medios.Derechos = 200_000;
        var estado = (await PortalHost.JsonAsync(await Pago(HttpMethod.Get, string.Empty, token))).RootElement;

        estado.GetProperty("totalCop").GetInt64().Should().Be(381_000);
        estado.GetProperty("cobro").GetProperty("estado").GetString().Should().Be("vigente");
    }

    [Fact]
    public async Task HU14449_SiCambiaLaTarifaEntreLaConsultaYLaConfirmacion_SeCobraElTotalNuevo()
    {
        await IniciarAsync();
        var (id, token) = await EnPagoAsync();
        await Pago(HttpMethod.Post, "/caja", token);
        var antes = (await PortalHost.JsonAsync(await Pago(HttpMethod.Get, string.Empty, token))).RootElement;
        antes.GetProperty("totalCop").GetInt64().Should().Be(366_000);

        _host.Medios.Derechos = 200_000;
        var pagado = await PagarCajaAsync(id);

        pagado.ValorCop.Should().Be(381_000);
        await using var db = _host.NuevaDb();
        (await db.Set<CobroSolicitud>().SingleAsync(c => c.SolicitudId == id, TestContext.Current.CancellationToken)).ValorCop.Should().Be(381_000);
    }

    [Fact]
    public async Task HU14449_SiLaCotizacionFallaAlConfirmar_NoSeRegistraElPago()
    {
        await IniciarAsync();
        var (id, token) = await EnPagoAsync();
        await Pago(HttpMethod.Post, "/caja", token);

        _host.Medios.SinTarifa = true;
        await FluentActions.Awaiting(() => PagarCajaAsync(id)).Should().ThrowAsync<ReglaDeNegocioException>().Where(e => e.Codigo == "sin_tarifa");

        (await EstadoDeAsync(id)).Should().Be("pendiente_pago");
        await using var db = _host.NuevaDb();
        (await db.Set<CobroSolicitud>().CountAsync(c => c.Estado == "pagado", TestContext.Current.CancellationToken)).Should().Be(0);
    }

    [Fact]
    public async Task HU14449_ConElIdVencido_ElEstadoLoMarca_YAlHabilitarDeNuevoVigeHastaElNuevoDia_ConElTotalRecalculado()
    {
        await IniciarAsync();
        var (id, token) = await EnPagoAsync();
        await Pago(HttpMethod.Post, "/caja", token);

        _host.Reloj.Avanzar(TimeSpan.FromDays(1));
        _host.Medios.Derechos = 190_000;
        var vencido = (await PortalHost.JsonAsync(await Pago(HttpMethod.Get, string.Empty, token))).RootElement;
        vencido.GetProperty("cobro").GetProperty("estado").GetString().Should().Be("vencido");

        var r = await Pago(HttpMethod.Post, "/caja", token);

        r.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = (await PortalHost.JsonAsync(r)).RootElement;
        json.GetProperty("totalCop").GetInt64().Should().Be(371_000);
        json.GetProperty("venceEn").GetDateTimeOffset().Should().Be(new DateTimeOffset(2026, 10, 28, 23, 59, 59, TimeSpan.FromHours(-5)));
        await using var db = _host.NuevaDb();
        (await db.Set<CobroSolicitud>().CountAsync(c => c.SolicitudId == id, TestContext.Current.CancellationToken)).Should().Be(1, "se renueva el mismo cobro");
    }

    [Fact]
    public async Task HU14449_ElCodigoVigenteAunPagaUnSegundoAntesDeLaMedianoche_YDejaDeServirEnLaMedianoche()
    {
        await IniciarAsync(compuertaConmutable: true);
        var (idA, tokenA) = await EnPagoAsync();
        await Pago(HttpMethod.Post, "/caja", tokenA);
        var (idB, tokenB) = await EnPagoAsync();
        await Pago(HttpMethod.Post, "/caja", tokenB);
        _compuerta.Permite = false;

        _host.Reloj.Avanzar(TimeSpan.FromHours(13) + TimeSpan.FromMinutes(59) + TimeSpan.FromSeconds(59)); // 23:59:59 en Colombia
        (await PagarCajaAsync(idA)).Medio.Should().Be("caja");

        _host.Reloj.Avanzar(TimeSpan.FromSeconds(1)); // 00:00:00 del día siguiente
        await FluentActions.Awaiting(() => PagarCajaAsync(idB)).Should().ThrowAsync<ReglaDeNegocioException>().Where(e => e.Codigo == "vencido");
    }

    [Fact]
    public async Task HU14449_SinTarifa_ResponderConflictoSinTarifa()
    {
        await IniciarAsync();
        var (_, token) = await EnPagoAsync();
        _host.Medios.SinTarifa = true;

        var r = await Pago(HttpMethod.Post, "/caja", token);

        r.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await PortalHost.CodigoAsync(r)).Should().Be("sin_tarifa");
        var estado = (await PortalHost.JsonAsync(await Pago(HttpMethod.Get, string.Empty, token))).RootElement;
        estado.GetProperty("puedePagar").GetBoolean().Should().BeFalse();
        estado.GetProperty("bloqueo").GetProperty("code").GetString().Should().Be("sin_tarifa");
    }

    [Fact]
    public async Task HU14449_ConElMedioPinApagado_ElPuntoNoTienePagoEnCaja()
    {
        await IniciarAsync();
        var (_, token) = await EnPagoAsync();
        _host.Medios.MedioDisponible = false;

        var r = await Pago(HttpMethod.Post, "/caja", token);

        r.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var json = (await PortalHost.JsonAsync(r)).RootElement;
        json.GetProperty("code").GetString().Should().Be("medio_no_disponible");
        json.GetProperty("detail").GetString().Should().Be("El punto no tiene habilitado el pago en caja.");
        var estado = (await PortalHost.JsonAsync(await Pago(HttpMethod.Get, string.Empty, token))).RootElement;
        estado.GetProperty("bloqueo").GetProperty("code").GetString().Should().Be("medio_no_disponible");
    }

    [Fact]
    public async Task HU14449_ConTotalCero_NoSeHabilitaCaja()
    {
        await IniciarAsync();
        var (_, token) = await EnPagoAsync();
        TotalCero();

        var r = await Pago(HttpMethod.Post, "/caja", token);

        r.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await PortalHost.CodigoAsync(r)).Should().Be("total_cero");
    }

    [Fact]
    public async Task HU14449_AntesDePasarAPago_NoSeHabilitaCaja()
    {
        await IniciarAsync();
        var (_, token) = await CompletaAsync();

        var r = await Pago(HttpMethod.Post, "/caja", token);

        r.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await PortalHost.CodigoAsync(r)).Should().Be("estado_no_permite_pago");
    }

    [Fact]
    public async Task HU14449_LaCajaNoPuedePagarSinHabilitarElCobro()
    {
        await IniciarAsync(compuertaConmutable: true);
        var (id, _) = await EnPagoAsync();

        await FluentActions.Awaiting(() => PagarCajaAsync(id)).Should().ThrowAsync<ReglaDeNegocioException>().Where(e => e.Codigo == "no_habilitado");
    }

    [Theory]
    [InlineData(null, null, "medio_caja_invalido")]
    [InlineData("tarjeta", null, "medio_caja_invalido")]
    [InlineData("otro", null, "detalle_requerido")]
    [InlineData("otro", "  ", "detalle_requerido")]
    public async Task HU14449_ElMedioDeCajaEsObligatorio_YOtroExigeDetalle(string? medioCaja, string? detalle, string codigo)
    {
        await IniciarAsync();
        var (id, token) = await EnPagoAsync();
        await Pago(HttpMethod.Post, "/caja", token);

        var pedido = new PedidoDePago(id, "caja", medioCaja, detalle, null, "caja-1");
        await FluentActions.Awaiting(() => _host.EnScopeAsync(sp => sp.GetRequiredService<IRegistroDePagos>().RegistrarPagoAsync(pedido, TestContext.Current.CancellationToken)))
            .Should().ThrowAsync<ReglaDeNegocioException>().Where(e => e.Codigo == codigo && e.Estado == 400);
        (await EstadoDeAsync(id)).Should().Be("pendiente_pago");
    }

    [Fact]
    public async Task HU14449_UnPagoDeOtroOrganismoSeRechaza()
    {
        await IniciarAsync();
        var (id, token) = await EnPagoAsync();
        await Pago(HttpMethod.Post, "/caja", token);

        var pedido = new PedidoDePago(id, "caja", "efectivo", OrganismoEsperado: Guid.NewGuid());
        await FluentActions.Awaiting(() => _host.EnScopeAsync(sp => sp.GetRequiredService<IRegistroDePagos>().RegistrarPagoAsync(pedido, TestContext.Current.CancellationToken)))
            .Should().ThrowAsync<ReglaDeNegocioException>().Where(e => e.Codigo == "otro_organismo" && e.Estado == 403);
    }

    [Fact]
    public async Task HU14449_DosCajasQueConfirmanAlMismoTiempo_SoloUnaQuedaRegistrada_LaOtraRecibeYaPagado()
    {
        await IniciarAsync();
        var (id, token) = await EnPagoAsync();
        await Pago(HttpMethod.Post, "/caja", token);

        var resultados = await Task.WhenAll(Enumerable.Range(0, 6).Select(async Task<(PagoRegistrado? Pago, string? Error)> (i) =>
        {
            try
            {
                return (await PagarCajaAsync(id, $"caja-{i}"), null);
            }
            catch (ReglaDeNegocioException ex)
            {
                return (null, ex.Codigo);
            }
        }));

        resultados.Count(r => r.Pago is not null).Should().Be(1);
        resultados.Where(r => r.Pago is null).Should().OnlyContain(r => r.Error == "ya_pagado");
        await using var db = _host.NuevaDb();
        (await db.Set<CobroSolicitud>().CountAsync(c => c.Estado == "pagado", TestContext.Current.CancellationToken)).Should().Be(1);
        (await db.Set<OutboxMessage>().CountAsync(TestContext.Current.CancellationToken)).Should().Be(0, "el cobro en caja espera la ventana de anulación antes de radicarse");

        // Vencida la ventana, varias réplicas radicando a la vez: la solicitud se bloquea y solo se radica una vez.
        _host.Reloj.Avanzar(TimeSpan.FromMinutes(16));
        await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => _host.EnScopeAsync(sp => sp.GetRequiredService<Flit.PersonaNatural.Api.Radicacion.RadicacionDePagadasService>().RadicarVencidasAsync(TestContext.Current.CancellationToken))));
        await using var db2 = _host.NuevaDb();
        (await db2.Set<OutboxMessage>().CountAsync(TestContext.Current.CancellationToken)).Should().Be(1, "solo se radica una vez");
    }

    // ── HU #14450: confirmar un trámite en $0 ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task HU14450_ConTotalCero_ConfirmarDejaPagadaConValorCeroYSinCobro_YSeRadica()
    {
        await IniciarAsync();
        var (id, token) = await EnPagoAsync();
        TotalCero();

        var r = await Pago(HttpMethod.Post, "/confirmar", token);

        r.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = (await PortalHost.JsonAsync(r)).RootElement;
        json.GetProperty("estado").GetString().Should().Be("en_cola");
        json.GetProperty("pago").GetProperty("valorCop").GetInt64().Should().Be(0);
        json.GetProperty("pago").GetProperty("medio").GetString().Should().Be("sin_cobro");
        await using var db = _host.NuevaDb();
        var cobro = await db.Set<CobroSolicitud>().SingleAsync(c => c.SolicitudId == id, TestContext.Current.CancellationToken);
        (cobro.Estado, cobro.ValorCop, cobro.Medio, cobro.MedioCaja).Should().Be(("pagado", 0L, "sin_cobro", (string?)null));
    }

    [Fact]
    public async Task HU14450_ConTotalMayorQueCero_ConfirmarResponde409_YNoQuedaPagada_AunqueElClienteEnvieValorCero()
    {
        await IniciarAsync();
        var (id, token) = await EnPagoAsync();

        var r = await _host.EnviarAsync(HttpMethod.Post, $"{Punto}/pago/confirmar", new { valorCop = 0, totalCop = 0 }, token, false, [("Cookie", _cookie)]);

        r.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await PortalHost.CodigoAsync(r)).Should().Be("total_mayor_cero");
        (await EstadoDeAsync(id)).Should().Be("pendiente_pago");
        await using var db = _host.NuevaDb();
        (await db.Set<CobroSolicitud>().CountAsync(TestContext.Current.CancellationToken)).Should().Be(0);
    }

    [Fact]
    public async Task HU14450_ConfirmarDosVecesEnCero_LaSegundaEsYaPagado()
    {
        await IniciarAsync();
        var (_, token) = await EnPagoAsync();
        TotalCero();

        (await Pago(HttpMethod.Post, "/confirmar", token)).StatusCode.Should().Be(HttpStatusCode.OK);
        var otra = await Pago(HttpMethod.Post, "/confirmar", token);

        otra.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await PortalHost.CodigoAsync(otra)).Should().Be("ya_pagado");
    }

    // ── HU #14468: radicar solo con el pago confirmado ────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HU14468_UnaSolicitudDelPuntoSinPago_NoSeRadica_Responde409PagoPendiente_YNoEncola(bool enPago)
    {
        await IniciarAsync();
        var (id, token) = enPago ? await EnPagoAsync() : await CompletaAsync();

        var r = await _host.EnviarAsync(HttpMethod.Post, $"{Base}/solicitudes/{id}/radicar", null, token, false, [("Idempotency-Key", "k-punto")]);

        r.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await PortalHost.CodigoAsync(r)).Should().Be("pago_pendiente");
        await using var db = _host.NuevaDb();
        (await db.Set<OutboxMessage>().CountAsync(TestContext.Current.CancellationToken)).Should().Be(0);
        (await db.Set<SolicitudTramite>().SingleAsync(TestContext.Current.CancellationToken)).Estado.Should().NotBe("en_cola");
    }

    [Fact]
    public async Task HU14468_AlConfirmarElPago_SeRadica_ConLaCotizacionDelPago_YElEventoNoLlevaValores()
    {
        await IniciarAsync();
        var (id, token) = await EnPagoAsync();
        await Pago(HttpMethod.Post, "/caja", token);
        _host.Medios.Derechos = 185_000;

        var pagado = await PagarCajaAsync(id);
        _host.Medios.Derechos = 999_000; // un cambio posterior de tarifa no altera lo que se pagó

        pagado.EstadoSolicitud.Should().Be("pagada", "el cobro en caja espera la ventana de anulación");
        _host.Reloj.Avanzar(TimeSpan.FromMinutes(16));
        (await _host.EnScopeAsync(async sp => await sp.GetRequiredService<Flit.PersonaNatural.Api.Radicacion.RadicacionDePagadasService>().RadicarVencidasAsync(TestContext.Current.CancellationToken))).Should().Be(1);
        await using var db = _host.NuevaDb();
        var s = await db.Set<SolicitudTramite>().SingleAsync(TestContext.Current.CancellationToken);
        s.Estado.Should().Be("en_cola");
        s.IdempotencyKey.Should().Be(RadicacionService.LlaveDelPago(id));
        var q = await db.Set<CotizacionCongelada>().SingleAsync(TestContext.Current.CancellationToken);
        (q.Medio, q.TotalCop, q.AmbitoOrganismoId).Should().Be(("PIN", 366_000L, Guid.Parse(PortalHost.Organismo)));
        var evento = await db.Set<OutboxMessage>().SingleAsync(TestContext.Current.CancellationToken);
        evento.RoutingKey.Should().Be("tramites.persona_natural.radicacion_solicitada");
        var payload = evento.Payload.ToString();
        payload.Should().NotContain("366000").And.NotContain("efectivo").And.NotContain("valor", "el evento es un aviso: el pago viaja por el RPC");
        (await db.Set<HistorialEstado>().Where(h => h.SolicitudId == id).OrderBy(h => h.OcurrioEn).Select(h => h.Hasta).ToListAsync(TestContext.Current.CancellationToken))
            .Should().Contain(["pendiente_pago", "pagada", "en_cola"]);
    }

    [Fact]
    public async Task HU14468_SiLaRadicacionFallaTrasPagar_ElPagoQuedaFirme_YLaSiguienteConsultaLaRadica()
    {
        await IniciarAsync(compuertaConmutable: true);
        var (id, token) = await EnPagoAsync();
        await Pago(HttpMethod.Post, "/caja", token);
        _compuerta.Permite = false;

        var pagado = await PagarCajaAsync(id);

        pagado.EstadoSolicitud.Should().Be("pagada");
        (await EstadoDeAsync(id)).Should().Be("pagada");
        _compuerta.Permite = true;
        _host.Reloj.Avanzar(TimeSpan.FromMinutes(16)); // vencida la ventana de anulación, la consulta del estado radica
        var estado = (await PortalHost.JsonAsync(await Pago(HttpMethod.Get, string.Empty, token))).RootElement;
        estado.GetProperty("estado").GetString().Should().Be("en_cola");
        estado.GetProperty("pago").GetProperty("valorCop").GetInt64().Should().Be(366_000);
        await using var db = _host.NuevaDb();
        (await db.Set<OutboxMessage>().CountAsync(TestContext.Current.CancellationToken)).Should().Be(1);
    }

    // ── Feature #14453: ventana de anulación del cobro en caja ───────────────────────────────────────────────────────

    private Task<int> RadicarVencidasAsync() =>
        _host.EnScopeAsync(sp => sp.GetRequiredService<Flit.PersonaNatural.Api.Radicacion.RadicacionDePagadasService>().RadicarVencidasAsync(TestContext.Current.CancellationToken));

    private async Task<Guid> CobroPagadoAsync(Guid solicitudId)
    {
        await using var db = _host.NuevaDb();
        return await db.Set<CobroSolicitud>().Where(c => c.SolicitudId == solicitudId && c.Estado == "pagado").Select(c => c.Id).SingleAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>Anula con la versión VIGENTE del cobro (la que mostraría la lista) salvo que se indique otra: <c>rowVersion</c> es obligatorio (&gt; 0).</summary>
    private async Task<CobroSolicitud> AnularAsync(Guid cobroId, string? motivo = "El cliente pagó por error en la caja", Guid? organismo = null, long? rowVersion = null)
    {
        long version;
        if (rowVersion is { } dada)
        {
            version = dada;
        }
        else
        {
            await using var db = _host.NuevaDb();
            version = await db.Set<CobroSolicitud>().Where(c => c.Id == cobroId).Select(c => c.RowVersion).SingleAsync(TestContext.Current.CancellationToken);
        }

        return await _host.EnScopeAsync(sp => sp.GetRequiredService<Flit.PersonaNatural.Api.Caja.CajaAnulacionService>().AnularAsync(
            organismo ?? Guid.Parse(PortalHost.Organismo), cobroId, motivo, Guid.Parse("00000000-0000-0000-0000-0000000000a1"), "Admin del OT", version, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task HU14453_UnCobroEnCaja_NoSeRadicaAntesDeLos15Minutos_YSiAlVencerLaVentana()
    {
        await IniciarAsync();
        var (id, token) = await EnPagoAsync();
        await Pago(HttpMethod.Post, "/caja", token);

        var pagado = await PagarCajaAsync(id);

        pagado.EstadoSolicitud.Should().Be("pagada");
        pagado.RadicaEn.Should().Be(pagado.PagadoEn.AddMinutes(15));
        var enRegistro = (await PortalHost.JsonAsync(await Pago(HttpMethod.Get, string.Empty, token))).RootElement;
        enRegistro.GetProperty("estado").GetString().Should().Be("pagada");
        enRegistro.GetProperty("pago").GetProperty("enRegistro").GetBoolean().Should().BeTrue();
        enRegistro.GetProperty("pago").GetProperty("radicaEn").GetDateTimeOffset().Should().Be(pagado.PagadoEn.AddMinutes(15));

        _host.Reloj.Avanzar(TimeSpan.FromMinutes(14) + TimeSpan.FromSeconds(59));
        (await RadicarVencidasAsync()).Should().Be(0);
        (await EstadoDeAsync(id)).Should().Be("pagada");
        await using (var db = _host.NuevaDb())
            (await db.Set<OutboxMessage>().CountAsync(TestContext.Current.CancellationToken)).Should().Be(0);

        // Ventana vencida (15:00) pero dentro de la gracia de 5 s: en el borde gana la anulación, la radicación todavía no.
        _host.Reloj.Avanzar(TimeSpan.FromSeconds(1));
        (await RadicarVencidasAsync()).Should().Be(0);
        (await EstadoDeAsync(id)).Should().Be("pagada");
        _host.Reloj.Avanzar(TimeSpan.FromSeconds(5));
        (await RadicarVencidasAsync()).Should().Be(1);
        (await EstadoDeAsync(id)).Should().Be("en_cola");
        await using (var db = _host.NuevaDb())
            (await db.Set<OutboxMessage>().CountAsync(TestContext.Current.CancellationToken)).Should().Be(1);
        (await RadicarVencidasAsync()).Should().Be(0, "es idempotente");
    }

    [Fact]
    public async Task HU14453_ElTrabajoDeFondo_RadicaSoloLosCobrosCuyaVentanaVencio()
    {
        await IniciarAsync(extras: [("Caja:RadicacionArranqueSegundos", "0"), ("Caja:RadicacionIntervaloSegundos", "1")]);
        var (id, token) = await EnPagoAsync();
        await Pago(HttpMethod.Post, "/caja", token);
        await PagarCajaAsync(id);

        await Task.Delay(TimeSpan.FromSeconds(2.5), TestContext.Current.CancellationToken);
        (await EstadoDeAsync(id)).Should().Be("pagada", "la ventana sigue abierta");

        _host.Reloj.Avanzar(TimeSpan.FromMinutes(15) + TimeSpan.FromSeconds(6)); // ventana + gracia
        var limite = DateTime.UtcNow.AddSeconds(20);
        while (DateTime.UtcNow < limite && await EstadoDeAsync(id) != "en_cola")
            await Task.Delay(500, TestContext.Current.CancellationToken);

        (await EstadoDeAsync(id)).Should().Be("en_cola");
        await using var db = _host.NuevaDb();
        (await db.Set<OutboxMessage>().CountAsync(TestContext.Current.CancellationToken)).Should().Be(1);
    }

    [Fact]
    public async Task HU14453_LaConsultaDelEstadoDePago_RadicaSoloCuandoLaVentanaYaVencio()
    {
        await IniciarAsync();
        var (id, token) = await EnPagoAsync();
        await Pago(HttpMethod.Post, "/caja", token);
        await PagarCajaAsync(id);

        _host.Reloj.Avanzar(TimeSpan.FromMinutes(10));
        (await PortalHost.JsonAsync(await Pago(HttpMethod.Get, string.Empty, token))).RootElement.GetProperty("estado").GetString().Should().Be("pagada");

        _host.Reloj.Avanzar(TimeSpan.FromMinutes(5) + TimeSpan.FromSeconds(6));
        (await PortalHost.JsonAsync(await Pago(HttpMethod.Get, string.Empty, token))).RootElement.GetProperty("estado").GetString().Should().Be("en_cola");
    }

    [Fact]
    public async Task HU14453_UnTotalEnCero_SeRadicaDeInmediato_SinEsperarLaVentana()
    {
        await IniciarAsync();
        var (id, token) = await EnPagoAsync();
        TotalCero();

        var r = await Pago(HttpMethod.Post, "/confirmar", token);

        r.StatusCode.Should().Be(HttpStatusCode.OK);
        (await EstadoDeAsync(id)).Should().Be("en_cola");
    }

    [Fact]
    public async Task HU14461_AlAnular_SeEnviaElCorreoDeAnulacionAQuienRadico_ConElCodigo_SinMotivoNiDatosDeLasPartes()
    {
        await IniciarAsync();
        var (id, token) = await EnPagoAsync();
        await Pago(HttpMethod.Post, "/caja", token);
        await PagarCajaAsync(id);
        var cobroId = await CobroPagadoAsync(id);
        _enviador.Enviados.Should().ContainSingle("el de cierre");
        _host.Reloj.Avanzar(TimeSpan.FromMinutes(5));

        await AnularAsync(cobroId);

        _enviador.Enviados.Should().HaveCount(2);
        var (correo, html) = _enviador.Enviados[1];
        correo.Should().Be("vendedor@example.org");
        _enviador.Plantillas[1].Should().Be("persona-natural.pago-anulado-punto");
        html.Should().Contain(await CodigoPublicoAsync(id)).And.Contain("anulado").And.Contain("Persona de Prueba");
        html.Should().NotContain("El cliente pagó por error en la caja", "el motivo interno no se comparte");
        foreach (var dato in new[] { Vendedor, Comprador, "3001234567", "comprador@example.org", "ABC123" })
            html.Should().NotContain(dato);
    }

    [Fact]
    public async Task HU14461_SiQuienRadicoNoTieneCorreoPropio_AlAnularNoSeEnviaNada()
    {
        await IniciarAsync();
        var (id, token) = await EnPagoAsync();
        await Pago(HttpMethod.Post, "/caja", token);
        await _host.EnScopeAsync(async sp =>
        {
            await sp.GetRequiredService<Flit.PersonaNatural.Api.Pii.PersonaService>().GuardarAsync(
                id, new Flit.PersonaNatural.Api.Pii.DatosPersona("vendedor", 1, "CC", Vendedor, "Persona de Prueba", null, "3001234567", null), TestContext.Current.CancellationToken);
            return true;
        });
        await PagarCajaAsync(id);
        var cobroId = await CobroPagadoAsync(id);

        await AnularAsync(cobroId);

        _enviador.Enviados.Should().BeEmpty("sin correo propio no hubo correo de cierre ni lo hay de anulación");
    }

    [Fact]
    public async Task HU14461_SiElEnvioDeLaAnulacionFalla_LaAnulacionQuedaFirme()
    {
        await IniciarAsync();
        var (id, token) = await EnPagoAsync();
        await Pago(HttpMethod.Post, "/caja", token);
        await PagarCajaAsync(id);
        var cobroId = await CobroPagadoAsync(id);
        _enviador.Lanza = true;

        var anulado = await AnularAsync(cobroId);

        anulado.Estado.Should().Be("anulado");
        (await EstadoDeAsync(id)).Should().Be("pendiente_pago");
        _enviador.Enviados.Should().ContainSingle("solo el de cierre");
    }

    [Fact]
    public async Task HU14461_SiElTransporteDeLaAnulacionCuelga_LaAnulacionNoEsperaMasDelTope_YQuedaFirme()
    {
        await IniciarAsync();
        var (id, token) = await EnPagoAsync();
        await Pago(HttpMethod.Post, "/caja", token);
        await PagarCajaAsync(id);
        var cobroId = await CobroPagadoAsync(id);
        _enviador.Cuelga = true;

        var anulado = await AnularAsync(cobroId);

        anulado.Estado.Should().Be("anulado");
        (await EstadoDeAsync(id)).Should().Be("pendiente_pago");
    }

    [Fact]
    public async Task HU14461_UnaSegundaAnulacionDelMismoCobro_NoReenviaElCorreo()
    {
        await IniciarAsync();
        var (id, token) = await EnPagoAsync();
        await Pago(HttpMethod.Post, "/caja", token);
        await PagarCajaAsync(id);
        var cobroId = await CobroPagadoAsync(id);

        await AnularAsync(cobroId);
        await FluentActions.Awaiting(() => AnularAsync(cobroId)).Should().ThrowAsync<ReglaDeNegocioException>().Where(e => e.Codigo == "cobro_no_anulable");

        _enviador.Plantillas.Count(p => p == "persona-natural.pago-anulado-punto").Should().Be(1);
    }

    [Fact]
    public async Task HU14461_AnularDentroDeLaVentana_DejaElCobroAnulado_ConAuditoria_YLaSolicitudVuelveAPendientePago()
    {
        await IniciarAsync();
        var (id, token) = await EnPagoAsync();
        await Pago(HttpMethod.Post, "/caja", token);
        await PagarCajaAsync(id);
        var cobroId = await CobroPagadoAsync(id);
        _host.Reloj.Avanzar(TimeSpan.FromMinutes(14));

        var anulado = await AnularAsync(cobroId);

        anulado.Estado.Should().Be("anulado");
        (await EstadoDeAsync(id)).Should().Be("pendiente_pago");
        await using var db = _host.NuevaDb();
        var c = await db.Set<CobroSolicitud>().SingleAsync(x => x.Id == cobroId, TestContext.Current.CancellationToken);
        (c.AnuladoPorNombre, c.AnulacionMotivo, c.AnuladoEn).Should().Be(("Admin del OT", "El cliente pagó por error en la caja", _host.Reloj.GetUtcNow()));
        c.AnuladoPorId.Should().Be(Guid.Parse("00000000-0000-0000-0000-0000000000a1"));
        (await db.Set<HistorialEstado>().CountAsync(h => h.SolicitudId == id && h.Motivo == "pago_anulado", TestContext.Current.CancellationToken)).Should().Be(1);

        // La solicitud puede volver a cobrarse (nuevo código) y el vencimiento corre desde el nuevo pago.
        _host.Reloj.Avanzar(TimeSpan.FromMinutes(1));
        (await Pago(HttpMethod.Post, "/caja", token)).StatusCode.Should().Be(HttpStatusCode.OK);
        var segundo = await PagarCajaAsync(id);
        _host.Reloj.Avanzar(TimeSpan.FromMinutes(14));
        (await RadicarVencidasAsync()).Should().Be(0);
        _host.Reloj.Avanzar(TimeSpan.FromMinutes(1) + TimeSpan.FromSeconds(5));
        (await RadicarVencidasAsync()).Should().Be(1);
        segundo.CobroId.Should().NotBe(cobroId);
    }

    [Fact]
    public async Task HU14461_AnularFueraDeLaVentana_OYaEnCola_ResponderYaEnFlit2_YNoCambiaNada()
    {
        await IniciarAsync();
        var (id, token) = await EnPagoAsync();
        await Pago(HttpMethod.Post, "/caja", token);
        await PagarCajaAsync(id);
        var cobroId = await CobroPagadoAsync(id);

        _host.Reloj.Avanzar(TimeSpan.FromMinutes(15)); // la ventana venció pero el trabajo aún no radicó
        await FluentActions.Awaiting(() => AnularAsync(cobroId)).Should().ThrowAsync<ReglaDeNegocioException>().Where(e => e.Codigo == "cobro_no_anulable" && e.Estado == 409);
        (await EstadoDeAsync(id)).Should().Be("pagada");
        _host.Reloj.Avanzar(TimeSpan.FromSeconds(6)); // la gracia de la radicación (5 s) también venció

        await RadicarVencidasAsync();
        await FluentActions.Awaiting(() => AnularAsync(cobroId)).Should().ThrowAsync<ReglaDeNegocioException>().Where(e => e.Codigo == "cobro_no_anulable");
        (await EstadoDeAsync(id)).Should().Be("en_cola");
        await using var db = _host.NuevaDb();
        (await db.Set<CobroSolicitud>().SingleAsync(c => c.Id == cobroId, TestContext.Current.CancellationToken)).Estado.Should().Be("pagado");
    }

    [Fact]
    public async Task HU14461_AnularExigeMotivo_ElCobroDelOrganismo_YDosAnulacionesAlMismoTiempoSoloUnaGana()
    {
        await IniciarAsync();
        var (id, token) = await EnPagoAsync();
        await Pago(HttpMethod.Post, "/caja", token);
        await PagarCajaAsync(id);
        var cobroId = await CobroPagadoAsync(id);

        await FluentActions.Awaiting(() => AnularAsync(cobroId, motivo: "  corto ")).Should().ThrowAsync<ReglaDeNegocioException>().Where(e => e.Codigo == "motivo_invalido" && e.Estado == 400);
        await FluentActions.Awaiting(() => AnularAsync(cobroId, motivo: null)).Should().ThrowAsync<ReglaDeNegocioException>().Where(e => e.Codigo == "motivo_invalido");
        await FluentActions.Awaiting(() => AnularAsync(cobroId, organismo: Guid.NewGuid())).Should().ThrowAsync<ReglaDeNegocioException>().Where(e => e.Codigo == "cobro_no_encontrado" && e.Estado == 404);
        await FluentActions.Awaiting(() => AnularAsync(cobroId, rowVersion: 999)).Should().ThrowAsync<ReglaDeNegocioException>().Where(e => e.Codigo == "cobro_no_anulable");
        (await EstadoDeAsync(id)).Should().Be("pagada");

        var resultados = await Task.WhenAll(Enumerable.Range(0, 4).Select(async Task<string> (_) =>
        {
            try
            {
                await AnularAsync(cobroId);
                return "ok";
            }
            catch (ReglaDeNegocioException ex)
            {
                return ex.Codigo;
            }
        }));
        resultados.Count(r => r == "ok").Should().Be(1);
        resultados.Where(r => r != "ok").Should().OnlyContain(r => r == "cobro_no_anulable");
    }

    [Fact]
    public async Task HU14453_ConLaCajaApagada_LaVentanaEsCero_ElPagoEnCajaSeRadicaDeInmediato()
    {
        await IniciarAsync(extras: [("Caja:Habilitado", "false")]);
        var (id, token) = await EnPagoAsync();
        await Pago(HttpMethod.Post, "/caja", token);

        var pagado = await PagarCajaAsync(id);

        pagado.RadicaEn.Should().BeNull("sin caja no hay ventana de anulación");
        pagado.EstadoSolicitud.Should().Be("en_cola");
        (await RadicarVencidasAsync()).Should().Be(0);
        new Flit.PersonaNatural.Api.Caja.CajaOptions { Habilitado = false, VentanaAnulacionMinutos = 15 }.Ventana.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public async Task HU14453_RadicarVencidas_AntesDeLos15MinutosNoRadica_YDespuesDeLaVentanaMasLaGraciaSi()
    {
        await IniciarAsync(extras: [("Caja:RadicacionGraciaSegundos", "5")]);
        var (id, token) = await EnPagoAsync();
        await Pago(HttpMethod.Post, "/caja", token);
        await PagarCajaAsync(id);

        _host.Reloj.Avanzar(TimeSpan.FromMinutes(14) + TimeSpan.FromSeconds(59));
        (await RadicarVencidasAsync()).Should().Be(0, "antes de 15 minutos");
        _host.Reloj.Avanzar(TimeSpan.FromSeconds(1)); // exactamente 15:00
        (await RadicarVencidasAsync()).Should().Be(0, "el borde exacto es de la anulación: la radicación espera la gracia");
        _host.Reloj.Avanzar(TimeSpan.FromSeconds(4)); // 15:04
        (await RadicarVencidasAsync()).Should().Be(0);
        _host.Reloj.Avanzar(TimeSpan.FromSeconds(1)); // 15:05
        (await RadicarVencidasAsync()).Should().Be(1);
        (await EstadoDeAsync(id)).Should().Be("en_cola");
    }

    [Fact]
    public async Task HU14453_LaGraciaEsConfigurable_YEnCeroLaRadicacionEsAlSegundoExacto()
    {
        await IniciarAsync(extras: [("Caja:RadicacionGraciaSegundos", "0")]);
        var (id, token) = await EnPagoAsync();
        await Pago(HttpMethod.Post, "/caja", token);
        await PagarCajaAsync(id);

        _host.Reloj.Avanzar(TimeSpan.FromMinutes(15));

        (await RadicarVencidasAsync()).Should().Be(1);
    }

    [Fact]
    public async Task HU14453_ElBordeDeLaVentana_SoloUnoGana_YElEstadoQuedaCoherente()
    {
        // 14:59 — la anulación llega a tiempo y el trabajo de radicación no puede ganarle.
        await IniciarAsync();
        var (idA, tokenA) = await EnPagoAsync();
        await Pago(HttpMethod.Post, "/caja", tokenA);
        await PagarCajaAsync(idA);
        var cobroA = await CobroPagadoAsync(idA);
        _host.Reloj.Avanzar(TimeSpan.FromMinutes(14) + TimeSpan.FromSeconds(59));

        var antes = await Task.WhenAll(
            AnularEnCarreraAsync(cobroA),
            RadicarVencidasAsync().ContinueWith(t => t.Result == 0 ? "sin_radicar" : "radico", TaskScheduler.Default));

        antes[0].Should().Be("anulado");
        antes[1].Should().Be("sin_radicar");
        (await EstadoDeAsync(idA)).Should().Be("pendiente_pago");
        await using (var db = _host.NuevaDb())
            (await db.Set<OutboxMessage>().CountAsync(TestContext.Current.CancellationToken)).Should().Be(0);

        // 15:06 — ya venció la ventana y la gracia: gana la radicación y la anulación recibe cobro_no_anulable.
        var (idB, tokenB) = await EnPagoAsync();
        await Pago(HttpMethod.Post, "/caja", tokenB);
        await PagarCajaAsync(idB);
        var cobroB = await CobroPagadoAsync(idB);
        _host.Reloj.Avanzar(TimeSpan.FromMinutes(15) + TimeSpan.FromSeconds(6));

        var despues = await Task.WhenAll(
            AnularEnCarreraAsync(cobroB),
            RadicarVencidasAsync().ContinueWith(t => t.Result == 0 ? "sin_radicar" : "radico", TaskScheduler.Default));

        despues[0].Should().Be("cobro_no_anulable");
        despues[1].Should().Be("radico");
        (await EstadoDeAsync(idB)).Should().Be("en_cola");
        await using var db2 = _host.NuevaDb();
        (await db2.Set<CobroSolicitud>().SingleAsync(c => c.Id == cobroB, TestContext.Current.CancellationToken)).Estado.Should().Be("pagado");
    }

    private async Task<string> AnularEnCarreraAsync(Guid cobroId)
    {
        try
        {
            await AnularAsync(cobroId);
            return "anulado";
        }
        catch (ReglaDeNegocioException ex)
        {
            return ex.Codigo;
        }
    }

    // ── Vigencia a medianoche (hora de Colombia) ──────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("2026-10-27T05:00:00Z", "2026-10-28T04:59:59Z")] // 00:00:00 en Colombia: el día empieza
    [InlineData("2026-10-27T04:59:59Z", "2026-10-27T04:59:59Z")] // 23:59:59 del día anterior en Colombia
    [InlineData("2026-10-27T23:00:00Z", "2026-10-28T04:59:59Z")] // 18:00 en Colombia
    public void LaVigenciaTerminaALas235959DeColombia(string ahora, string esperadoUtc)
    {
        var fin = VigenciaDelCobro.FinDelDia(DateTimeOffset.Parse(ahora, null, System.Globalization.DateTimeStyles.AssumeUniversal));

        fin.UtcDateTime.Should().Be(DateTime.Parse(esperadoUtc, null, System.Globalization.DateTimeStyles.AdjustToUniversal));
        VigenciaDelCobro.EstaVigente(fin, fin).Should().BeTrue();
        VigenciaDelCobro.EstaVigente(fin, fin.AddSeconds(1)).Should().BeFalse("a la medianoche ya venció");
    }

    // ── Soporte ──────────────────────────────────────────────────────────────────────────────────────────────────────

    private void TotalCero()
    {
        _host.Medios.Derechos = 0;
        _host.Medios.Gestion = 0;
        _host.Medios.GestionEncendida = false;
    }

    private Task<HttpResponseMessage> Pago(HttpMethod metodo, string ruta, string? token) =>
        _host.EnviarAsync(metodo, $"{Punto}/pago{ruta}", null, token, false, [("Cookie", _cookie)]);

    private Task<PagoRegistrado> PagarCajaAsync(Guid id, string usuario = "caja-1") =>
        _host.EnScopeAsync(sp => sp.GetRequiredService<IRegistroDePagos>().RegistrarPagoAsync(
            new PedidoDePago(id, "caja", "efectivo", null, null, usuario, Guid.Parse(PortalHost.Organismo)), TestContext.Current.CancellationToken));

    private async Task<string> EstadoDeAsync(Guid id)
    {
        await using var db = _host.NuevaDb();
        return await db.Set<SolicitudTramite>().Where(s => s.Id == id).Select(s => s.Estado).SingleAsync(TestContext.Current.CancellationToken);
    }

    private async Task<string> CodigoPublicoAsync(Guid id)
    {
        await using var db = _host.NuevaDb();
        return await db.Set<SolicitudTramite>().Where(s => s.Id == id).Select(s => s.CodigoPublico).SingleAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>Un borrador del punto completo y validado, ya pasado a pendiente de pago por el endpoint.</summary>
    private async Task<(Guid Id, string Token)> EnPagoAsync()
    {
        var (id, token) = await CompletaAsync();
        var r = await Pago(HttpMethod.Post, "/iniciar", token);
        r.StatusCode.Should().Be(HttpStatusCode.OK, await r.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        return (id, token);
    }

    /// <summary>Un borrador de traspaso listo: partes, campos, valor comercial y documentos; del punto o de la web.</summary>
    private async Task<(Guid Id, string Token)> CompletaAsync(bool validar = true, bool conSoat = true, bool canalPunto = true)
    {
        var r = await _host.EnviarAsync(HttpMethod.Post, $"{Base}/sesiones",
            new { tipoCodigo = "TRASPASO_STANDARD", organismoId = PortalHost.Organismo, cedula = PortalHost.Cedula, placa = "ABC123", consentimiento = true }, captcha: true);
        r.StatusCode.Should().Be(HttpStatusCode.Created);
        var json = (await PortalHost.JsonAsync(r)).RootElement;
        var id = json.GetProperty("solicitudId").GetGuid();
        var token = json.GetProperty("token").GetString()!;

        if (canalPunto)
        {
            await using var dbPunto = _host.NuevaDb();
            var sol = await dbPunto.Set<SolicitudTramite>().SingleAsync(x => x.Id == id, TestContext.Current.CancellationToken);
            sol.Datos = "{\"canal\":\"pn_punto\",\"codigoOrganismo\":\"AB1\",\"organismoId\":\"" + sol.OrganismoId + "\"}";
            await dbPunto.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        (await _host.EnviarAsync(HttpMethod.Patch, $"{Base}/solicitudes/{id}/actores", new
        {
            actores = new object[]
            {
                new { rol = "vendedor", tipoDocumento = "CC", numeroDocumento = Vendedor, nombre = "Persona de Prueba", correo = "vendedor@example.org", celular = "3001234567" },
                new { rol = "comprador", tipoDocumento = "CC", numeroDocumento = Comprador, nombre = "Persona de Prueba", correo = "comprador@example.org", celular = "3009876543" },
            },
        }, token)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await _host.EnviarAsync(HttpMethod.Put, $"{Base}/solicitudes/{id}/campos", new { valores = new Dictionary<string, object?> { ["km"] = 85000 } }, token)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await _host.EnviarAsync(HttpMethod.Put, $"{Base}/solicitudes/{id}/comercial", new { valorComercial = 45_000_000 }, token)).StatusCode.Should().Be(HttpStatusCode.OK);
        foreach (var doc in conSoat ? new[] { "compraventa", "soat" } : ["compraventa"])
            (await _host.SubirAsync($"{Base}/solicitudes/{id}/adjuntos", token, Archivos.Pdf(), $"{doc}.pdf", doc)).StatusCode.Should().Be(HttpStatusCode.Created);
        if (!canalPunto)
            (await _host.SubirAsync($"{Base}/solicitudes/{id}/pago/comprobante", token, Archivos.Png(), "pago.png")).StatusCode.Should().Be(HttpStatusCode.Created);

        if (validar)
        {
            await using var db = _host.NuevaDb();
            await db.Set<PersonaSolicitante>().Where(p => p.SolicitudId == id)
                .ExecuteUpdateAsync(set => set.SetProperty(p => p.ValidacionEstado, "aprobada").SetProperty(p => p.ValidacionId, Guid.NewGuid()), TestContext.Current.CancellationToken);
        }

        return (id, token);
    }
}
