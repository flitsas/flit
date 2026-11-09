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
public sealed class PagoPuntoTests : IAsyncLifetime
{
    private const string Base = "/api/v1/persona-natural";
    private const string Punto = Base + "/punto";
    private const string Vendedor = "7001234567";
    private const string Comprador = "7009876543";

    private readonly AlmacenEnMemoria _almacen = new();
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

    private async Task IniciarAsync(bool compuertaConmutable = false)
    {
        _host = await PortalHost.IniciarAsync("pnpago", (servicios, _) =>
        {
            servicios.AddSingleton<IAlmacenAdjuntos>(_almacen);
            servicios.AddSingleton<ICanalPunto>(new CanalPuntoDePrueba());
            if (compuertaConmutable)
                servicios.AddSingleton<ICompuertaDeRadicacion>(_compuerta);
            // Varias pruebas avanzan el reloj un día: la sesión del trámite no debe vencer por eso.
        }, ("Sesion:VigenciaMinutos", "100000"), ("Sesion:TopeAbsolutoHoras", "1000"));
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
        (await db.Set<OutboxMessage>().CountAsync(TestContext.Current.CancellationToken)).Should().Be(1, "solo se radica una vez");
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

        pagado.EstadoSolicitud.Should().Be("en_cola");
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
        var estado = (await PortalHost.JsonAsync(await Pago(HttpMethod.Get, string.Empty, token))).RootElement;
        estado.GetProperty("estado").GetString().Should().Be("en_cola");
        estado.GetProperty("pago").GetProperty("valorCop").GetInt64().Should().Be(366_000);
        await using var db = _host.NuevaDb();
        (await db.Set<OutboxMessage>().CountAsync(TestContext.Current.CancellationToken)).Should().Be(1);
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
