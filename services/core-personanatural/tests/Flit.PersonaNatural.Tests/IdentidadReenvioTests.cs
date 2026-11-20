using System.Net;
using Flit.PersonaNatural.Api.Identidad;
using Flit.PersonaNatural.Api.Persistence;
using Flit.PersonaNatural.Api.Pii;
using Flit.Platform.Sdk;
using Flit.Platform.Sdk.Messaging;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Flit.PersonaNatural.Tests;

/// <summary>
/// HU #14045 (Feature C, Épica #14021) — reenviar el enlace, estado de las partes y espejo por evento de FLIT 2.
/// </summary>
public sealed class IdentidadReenvioTests : IdentidadBase
{
    [Fact]
    public async Task Reenviar_UnaParteConValidacionPendiente_LlamaAFlit2_YRegistraElReenvio()
    {
        if (!await IniciarAsync()) return;
        var (solicitud, token) = await SembrarAsync();
        await SolicitarAsync(solicitud, token, "vendedor");

        var r = await ReenviarAsync(solicitud, token, "vendedor");

        r.StatusCode.Should().Be(HttpStatusCode.OK);
        Fachada.Reenvios.Should().Be(1);
        var persona = await PersonaAsync(solicitud, "vendedor");
        persona.Reenvios.Should().Be(1);
        persona.UltimoReenvioEn.Should().Be(Reloj.GetUtcNow());
        Fachada.ValidacionesCreadas.Should().Be(1, "reenviar no crea otra validación: sin cargos dobles");
    }

    [Fact]
    public async Task Reenviar_ConUnoEnLosUltimos60Segundos_Responde429ConLaEspera_YDespuesDeEsperarSale()
    {
        if (!await IniciarAsync()) return;
        var (solicitud, token) = await SembrarAsync();
        await SolicitarAsync(solicitud, token, "vendedor");
        await ReenviarAsync(solicitud, token, "vendedor");
        Reloj.Avanzar(TimeSpan.FromSeconds(20));

        var pronto = await ReenviarAsync(solicitud, token, "vendedor");

        pronto.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        (await CodigoAsync(pronto)).Should().Be("reenvio_muy_pronto");
        pronto.Headers.RetryAfter!.Delta.Should().Be(TimeSpan.FromSeconds(40));
        Fachada.Reenvios.Should().Be(1);

        Reloj.Avanzar(TimeSpan.FromSeconds(41));
        (await ReenviarAsync(solicitud, token, "vendedor")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Reenviar_ConCincoReenvios_Responde429_ReenviosAgotados()
    {
        if (!await IniciarAsync()) return;
        var (solicitud, token) = await SembrarAsync();
        await SolicitarAsync(solicitud, token, "vendedor");
        for (var i = 0; i < 5; i++)
        {
            Reloj.Avanzar(TimeSpan.FromSeconds(61));
            (await ReenviarAsync(solicitud, token, "vendedor")).StatusCode.Should().Be(HttpStatusCode.OK);
        }

        Reloj.Avanzar(TimeSpan.FromSeconds(61));
        var sexto = await ReenviarAsync(solicitud, token, "vendedor");

        sexto.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        (await CodigoAsync(sexto)).Should().Be("reenvios_agotados");
        Fachada.Reenvios.Should().Be(5);
    }

    [Fact]
    public async Task Reenviar_ConUnCorreoCorregido_SaleAlCorreoNuevo_YQuedaGuardadoElNuevo()
    {
        if (!await IniciarAsync()) return;
        var (solicitud, token) = await SembrarAsync();
        await SolicitarAsync(solicitud, token, "vendedor");

        var r = await ReenviarAsync(solicitud, token, "vendedor", new { correo = "corregido@example.org" });

        r.StatusCode.Should().Be(HttpStatusCode.OK);
        Fachada.CorreosDeReenvio.Should().Equal("corregido@example.org");
        var guardado = await EnScopeAsync(sp => sp.GetRequiredService<PersonaService>().LeerAsync(solicitud, "prueba", TestContext.Current.CancellationToken));
        guardado.Single(p => p.Parte == "vendedor").Correo.Should().Be("corregido@example.org");
        guardado.Single(p => p.Parte == "comprador").Correo.Should().Be(Correo, "solo cambia el de esa parte");
    }

    [Fact]
    public async Task Reenviar_ConUnCorreoMalFormado_Responde422_SinLlamarAFlit2()
    {
        if (!await IniciarAsync()) return;
        var (solicitud, token) = await SembrarAsync();
        await SolicitarAsync(solicitud, token, "vendedor");
        var llamadas = Fachada.Llamadas;

        var r = await ReenviarAsync(solicitud, token, "vendedor", new { correo = "esto-no-es-un-correo" });

        r.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await Json(r)).GetProperty("field").GetString().Should().Be("correo");
        Fachada.Llamadas.Should().Be(llamadas);
    }

    [Fact]
    public async Task Reenviar_UnaAprobada_Responde409_YUnaSinSolicitarPideSolicitarPrimero()
    {
        if (!await IniciarAsync()) return;
        var (solicitud, token) = await SembrarAsync();
        await PonerEstadoAsync(solicitud, "vendedor", "aprobada");

        var aprobada = await ReenviarAsync(solicitud, token, "vendedor");
        var sinSolicitar = await ReenviarAsync(solicitud, token, "comprador");

        aprobada.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await CodigoAsync(aprobada)).Should().Be("identidad_aprobada");
        sinSolicitar.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await CodigoAsync(sinSolicitar)).Should().Be("validacion_no_solicitada");
        Fachada.Reenvios.Should().Be(0);
    }

    [Fact]
    public async Task Reenviar_ConFlit2Caido_Responde503_YNoGastaUnReenvio()
    {
        if (!await IniciarAsync()) return;
        var (solicitud, token) = await SembrarAsync();
        await SolicitarAsync(solicitud, token, "vendedor");
        Fachada.Caida = true;

        var r = await ReenviarAsync(solicitud, token, "vendedor");

        r.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await PersonaAsync(solicitud, "vendedor")).Reenvios.Should().Be(0);
    }

    [Fact]
    public async Task Reenviar_UnaParteEnPendienteEnvio_HaceElEnvioPendiente_SinContarComoReenvio()
    {
        if (!await IniciarAsync()) return;
        var (solicitud, token) = await SembrarAsync();
        Fachada.Caida = true;
        await SolicitarAsync(solicitud, token, "vendedor");
        Fachada.Caida = false;

        var r = await ReenviarAsync(solicitud, token, "vendedor");

        r.StatusCode.Should().Be(HttpStatusCode.OK);
        var persona = await PersonaAsync(solicitud, "vendedor");
        persona.ValidacionEstado.Should().Be("pendiente");
        persona.Reenvios.Should().Be(0);
        Fachada.Reenvios.Should().Be(0);
    }

    // ── GET /identidad ───────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task EstadoDeLaSolicitud_ListaSoloLasPartesObligadas_YDiceCuandoEstaCompleta()
    {
        if (!await IniciarAsync()) return;
        var (solicitud, token) = await SembrarAsync();
        await SolicitarAsync(solicitud, token, "vendedor");

        var incompleta = await Json(await GetAsync($"{Base}/solicitudes/{solicitud}/identidad", token));

        incompleta.GetProperty("completa").GetBoolean().Should().BeFalse();
        var partes = incompleta.GetProperty("partes").EnumerateArray().ToList();
        partes.Select(p => p.GetProperty("parte").GetString()).Should().Equal("vendedor", "comprador");
        partes[0].GetProperty("estado").GetString().Should().Be("pendiente");
        partes[1].GetProperty("estado").GetString().Should().Be("sin_solicitar");

        await PonerEstadoAsync(solicitud, "vendedor", "aprobada");
        await PonerEstadoAsync(solicitud, "comprador", "aprobada");
        var completa = await Json(await GetAsync($"{Base}/solicitudes/{solicitud}/identidad", token));
        completa.GetProperty("completa").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task EstadoDeLaSolicitud_SinElEvento_PreguntaAFlit2PorLoPendiente_YRefleja()
    {
        if (!await IniciarAsync()) return;
        var (solicitud, token) = await SembrarAsync();
        await SolicitarAsync(solicitud, token, "vendedor");
        var id = (await PersonaAsync(solicitud, "vendedor")).ValidacionId!.Value;
        Fachada.Estados[id] = "aprobada";

        var cuerpo = await Json(await GetAsync($"{Base}/solicitudes/{solicitud}/identidad", token));

        cuerpo.GetProperty("partes")[0].GetProperty("estado").GetString().Should().Be("aprobada");
        (await AuditoriaAsync(solicitud)).Should().Contain(a => a.Origen == "sondeo" && a.Desde == "pendiente" && a.Hasta == "aprobada");
    }

    [Fact]
    public async Task EstadoDeLaSolicitud_ConFlit2Caido_ConservaLoQueElPortalSabe()
    {
        if (!await IniciarAsync()) return;
        var (solicitud, token) = await SembrarAsync();
        await SolicitarAsync(solicitud, token, "vendedor");
        Fachada.Caida = true;

        var r = await GetAsync($"{Base}/solicitudes/{solicitud}/identidad", token);

        r.StatusCode.Should().Be(HttpStatusCode.OK);
        (await Json(r)).GetProperty("partes")[0].GetProperty("estado").GetString().Should().Be("pendiente");
    }

    // ── AC4 y AC5: espejo por evento ─────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("aprobado", "aprobada")]
    [InlineData("rechazado", "rechazada")]
    public async Task ElEvento_CambiaElEstadoDeLaParte_YQuedaEnAuditoria(string estadoFlit, string esperado)
    {
        if (!await IniciarAsync()) return;
        var (solicitud, token) = await SembrarAsync();
        await SolicitarAsync(solicitud, token, "vendedor");
        var id = (await PersonaAsync(solicitud, "vendedor")).ValidacionId!.Value;
        var evento = Guid.CreateVersion7();

        await Consumir(Sobre(evento, PlatformTenants.PersonaNatural), Datos(id, estadoFlit));

        (await PersonaAsync(solicitud, "vendedor")).ValidacionEstado.Should().Be(esperado);
        var auditoria = (await AuditoriaAsync(solicitud)).Last();
        auditoria.Should().BeEquivalentTo(new { Parte = "vendedor", Desde = "pendiente", Hasta = esperado, Origen = "evento", EventId = (Guid?)evento });
    }

    [Fact]
    public async Task ElMismoEventoDosVeces_NoRepiteElEfecto()
    {
        if (!await IniciarAsync()) return;
        var (solicitud, token) = await SembrarAsync();
        await SolicitarAsync(solicitud, token, "vendedor");
        var id = (await PersonaAsync(solicitud, "vendedor")).ValidacionId!.Value;
        var sobre = Sobre(Guid.CreateVersion7(), PlatformTenants.PersonaNatural);
        var antes = (await AuditoriaAsync(solicitud)).Count;

        await Consumir(sobre, Datos(id, "aprobado"));
        await Consumir(sobre, Datos(id, "aprobado"));

        (await AuditoriaAsync(solicitud)).Count.Should().Be(antes + 1);
    }

    [Fact]
    public async Task UnaAprobada_NoLaDeshaceUnAvisoTardioDeRechazo()
    {
        if (!await IniciarAsync()) return;
        var (solicitud, token) = await SembrarAsync();
        await SolicitarAsync(solicitud, token, "vendedor");
        var id = (await PersonaAsync(solicitud, "vendedor")).ValidacionId!.Value;
        await Consumir(Sobre(Guid.CreateVersion7(), PlatformTenants.PersonaNatural), Datos(id, "aprobado"));

        await Consumir(Sobre(Guid.CreateVersion7(), PlatformTenants.PersonaNatural), Datos(id, "rechazado"));

        (await PersonaAsync(solicitud, "vendedor")).ValidacionEstado.Should().Be("aprobada");
    }

    [Fact]
    public async Task ElEventoDeOtraEmpresaOSinPersonaConEseId_SeIgnora()
    {
        if (!await IniciarAsync()) return;
        var (solicitud, token) = await SembrarAsync();
        await SolicitarAsync(solicitud, token, "vendedor");
        var id = (await PersonaAsync(solicitud, "vendedor")).ValidacionId!.Value;
        var antes = (await AuditoriaAsync(solicitud)).Count;

        await Consumir(Sobre(Guid.CreateVersion7(), Guid.NewGuid()), Datos(id, "aprobado"));
        await Consumir(Sobre(Guid.CreateVersion7(), PlatformTenants.PersonaNatural), Datos(Guid.NewGuid(), "aprobado"));
        await Consumir(Sobre(Guid.CreateVersion7(), PlatformTenants.PersonaNatural), Datos(id, "en_proceso"));

        (await PersonaAsync(solicitud, "vendedor")).ValidacionEstado.Should().Be("pendiente");
        (await AuditoriaAsync(solicitud)).Count.Should().Be(antes);
    }

    private static EventEnvelope Sobre(Guid eventoId, Guid empresa) =>
        new(eventoId, IdentidadEndpoints.EventoCompletada, 1, DateTimeOffset.UtcNow, empresa, "tramites", "corr", default);

    private static ValidacionCompletada Datos(Guid validacion, string estado) =>
        new(validacion, estado, "vendedor", null, "mock", estado, null);

    private async Task Consumir(EventEnvelope sobre, ValidacionCompletada datos) =>
        await EnScopeAsync(async sp =>
        {
            await sp.GetRequiredService<ValidacionCompletadaConsumer>().HandleAsync(sobre, datos, TestContext.Current.CancellationToken);
            return 0;
        });
}
