using System.Net;
using System.Text.Json;
using Flit.PersonaNatural.Api.Identidad;
using Flit.PersonaNatural.Api.Pagos;
using Flit.PersonaNatural.Api.Persistence;
using Flit.PersonaNatural.Api.Pii;
using Flit.Platform.Sdk.Messaging;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Flit.PersonaNatural.Tests;

/// <summary>
/// Feature CIERRE OT — HU #14464 (estado de cierre del trámite pagado) y #14466 (correo de cierre a quien radicó).
/// Mismo host y ayudas que <see cref="PagoPuntoTests"/>.
/// </summary>
public sealed partial class PagoPuntoTests
{
    // ── HU #14464: estado de cierre ───────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task HU14464_SinPagoConfirmado_ElCierreResponde409PagoPendiente()
    {
        await IniciarAsync();
        var (_, token) = await EnPagoAsync();

        var r = await Pago(HttpMethod.Get, "/cierre", token);

        r.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await PortalHost.JsonAsync(r)).RootElement.GetProperty("code").GetString().Should().Be("pago_pendiente");
    }

    [Fact]
    public async Task HU14464_PagadaSinRespuestaDeFlit2_EsEnRegistro_ConPagoYOrganismo_SinRadicado()
    {
        await IniciarAsync(compuertaConmutable: true);
        var (id, token) = await EnPagoAsync();
        await Pago(HttpMethod.Post, "/caja", token);
        _compuerta.Permite = false;
        await PagarCajaAsync(id);

        var r = await Pago(HttpMethod.Get, "/cierre", token);

        r.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = (await PortalHost.JsonAsync(r)).RootElement;
        json.GetProperty("estadoCierre").GetString().Should().Be("en_registro");
        json.GetProperty("codigoPublico").GetString().Should().Be(await CodigoPublicoAsync(id)).And.StartWith("PN-");
        json.TryGetProperty("radicadoFlit2", out _).Should().BeFalse("los nulos no viajan");
        json.GetProperty("valorCop").GetInt64().Should().Be(366_000);
        json.GetProperty("medio").GetString().Should().Be("caja");
        json.GetProperty("medioCaja").GetString().Should().Be("efectivo");
        json.GetProperty("pagadoEn").GetDateTimeOffset().Should().Be(_host.Reloj.GetUtcNow());
        json.GetProperty("recogeEn").GetProperty("organismo").GetString().Should().Be("Tránsito de Prueba");
    }

    [Fact]
    public async Task HU14464_ConLaRespuestaDeFlit2_EsRegistrado_ConElRadicado()
    {
        await IniciarAsync();
        var (id, token) = await EnPagoAsync();
        await Pago(HttpMethod.Post, "/caja", token);
        await PagarCajaAsync(id);
        await ResolverAsync(id, "creada", "TR-2026-000123");

        var json = (await PortalHost.JsonAsync(await Pago(HttpMethod.Get, "/cierre", token))).RootElement;

        json.GetProperty("estadoCierre").GetString().Should().Be("registrado");
        json.GetProperty("radicadoFlit2").GetString().Should().Be("TR-2026-000123");
        json.GetProperty("codigoPublico").GetString().Should().Be(await CodigoPublicoAsync(id), "el código para consultar sigue siendo el público");
    }

    [Theory]
    [InlineData("El organismo no tiene habilitado este trámite.", "El organismo no tiene habilitado este trámite.")]
    [InlineData("regla_interna_violada", "El organismo no pudo registrar el trámite. Acércate al organismo con tu código para que lo revisen.")]
    [InlineData("System.NullReferenceException at Foo.Bar() in x", "El organismo no pudo registrar el trámite. Acércate al organismo con tu código para que lo revisen.")]
    public async Task HU14464_ConElRechazoDeFlit2_EsRechazado_ConUnMotivoLegibleSinDetallesInternos(string motivoInterno, string esperado)
    {
        await IniciarAsync();
        var (id, token) = await EnPagoAsync();
        await Pago(HttpMethod.Post, "/caja", token);
        await PagarCajaAsync(id);
        await ResolverAsync(id, "rechazada_creacion", null, motivoInterno);

        var json = (await PortalHost.JsonAsync(await Pago(HttpMethod.Get, "/cierre", token))).RootElement;

        json.GetProperty("estadoCierre").GetString().Should().Be("rechazado");
        json.GetProperty("motivo").GetString().Should().Be(esperado);
        json.TryGetProperty("radicadoFlit2", out _).Should().BeFalse("los nulos no viajan");
    }

    [Fact]
    public async Task HU14464_UnTramiteSinCobro_InformaMedioSinCobro_YValorCero()
    {
        await IniciarAsync(compuertaConmutable: true);
        var (_, token) = await EnPagoAsync();
        TotalCero();
        _compuerta.Permite = false;
        (await Pago(HttpMethod.Post, "/confirmar", token)).StatusCode.Should().Be(HttpStatusCode.OK);

        var json = (await PortalHost.JsonAsync(await Pago(HttpMethod.Get, "/cierre", token))).RootElement;

        json.GetProperty("medio").GetString().Should().Be("sin_cobro");
        json.GetProperty("valorCop").GetInt64().Should().Be(0);
        json.TryGetProperty("medioCaja", out _).Should().BeFalse("los nulos no viajan");
        json.GetProperty("estadoCierre").GetString().Should().Be("en_registro");
    }

    [Fact]
    public async Task HU14464_ElCierreSoloLlevaElNombreDeLasPartes_SinDocumentoNiContacto()
    {
        // Cambio del PO (2026-11-18, diseño de Lovable): la confirmación muestra la tarjeta «Actores» con el nombre de las
        // partes. Documento, correo y teléfono siguen sin viajar.
        await IniciarAsync();
        var (id, token) = await EnPagoAsync();
        await Pago(HttpMethod.Post, "/caja", token);
        await PagarCajaAsync(id);

        var cuerpo = await (await Pago(HttpMethod.Get, "/cierre", token)).Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        cuerpo.Should().Contain("Persona de Prueba");
        foreach (var dato in new[] { Vendedor, Comprador, "example.org", "3001234567" })
            cuerpo.Should().NotContain(dato);
    }

    [Fact]
    public async Task HU14464_ConsultarPorCodigo_ElMismoCierreDelMismoOrganismo_YSinPagoEs404Generico()
    {
        await IniciarAsync();
        var (id, token) = await EnPagoAsync();
        var codigo = await CodigoPublicoAsync(id);
        (await Pago(HttpMethod.Get, $"/cierre/{codigo}", null)).StatusCode.Should().Be(HttpStatusCode.NotFound, "sin pago no se revela que el código existe");
        await Pago(HttpMethod.Post, "/caja", token);
        await PagarCajaAsync(id);
        await ResolverAsync(id, "creada", "TR-2026-000124");

        var porCodigo = (await PortalHost.JsonAsync(await Pago(HttpMethod.Get, $"/cierre/{codigo.ToLowerInvariant()}", null))).RootElement;
        var porRadicado = (await PortalHost.JsonAsync(await Pago(HttpMethod.Get, "/cierre/TR-2026-000124", null))).RootElement;
        var delTramite = (await PortalHost.JsonAsync(await Pago(HttpMethod.Get, "/cierre", token))).RootElement;

        // La consulta por código o radicado es el mismo cierre SIN el resumen (nombres de las partes, documentos): ese solo
        // viaja en la sesión del trámite.
        porCodigo.TryGetProperty("resumen", out _).Should().BeFalse();
        delTramite.TryGetProperty("resumen", out _).Should().BeTrue();
        SinResumen(porCodigo).Should().Be(SinResumen(delTramite));
        SinResumen(porRadicado).Should().Be(SinResumen(delTramite));
        porCodigo.GetProperty("estadoCierre").GetString().Should().Be("registrado");
    }

    [Fact]
    public async Task HU14464_ConsultarPorCodigoDeOtroOrganismoOInexistente_Es404()
    {
        await IniciarAsync();
        var (id, token) = await EnPagoAsync();
        await Pago(HttpMethod.Post, "/caja", token);
        await PagarCajaAsync(id);
        var codigo = await CodigoPublicoAsync(id);
        await using (var db = _host.NuevaDb())
        {
            await db.Set<SolicitudTramite>().Where(s => s.Id == id).ExecuteUpdateAsync(set => set.SetProperty(s => s.OrganismoId, Guid.NewGuid()), TestContext.Current.CancellationToken);
        }

        (await Pago(HttpMethod.Get, $"/cierre/{codigo}", null)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await Pago(HttpMethod.Get, "/cierre/PN-2026-999999", null)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await Pago(HttpMethod.Get, "/cierre", token)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task HU14464_ConsultarPorCodigo_InexistenteOtroOrganismoYSinPago_RespondenElMismo404NoEncontrado()
    {
        await IniciarAsync();
        var (idSinPago, _) = await EnPagoAsync();
        var sinPago = await CodigoPublicoAsync(idSinPago);

        var inexistente = await Pago(HttpMethod.Get, "/cierre/PN-2026-999999", null);
        var conSolicitudSinPago = await Pago(HttpMethod.Get, $"/cierre/{sinPago}", null);
        await using (var db = _host.NuevaDb())
        {
            await db.Set<SolicitudTramite>().Where(s => s.Id == idSinPago).ExecuteUpdateAsync(set => set.SetProperty(s => s.OrganismoId, Guid.NewGuid()), TestContext.Current.CancellationToken);
        }

        var otroOrganismo = await Pago(HttpMethod.Get, $"/cierre/{sinPago}", null);

        foreach (var r in new[] { inexistente, conSolicitudSinPago, otroOrganismo })
        {
            r.StatusCode.Should().Be(HttpStatusCode.NotFound);
            (await PortalHost.CodigoAsync(r)).Should().Be("no_encontrado");
        }
    }

    [Fact]
    public async Task HU14464_ConsultarPorCodigo_TrasCincoFallosSeguidos_Bloquea429_YSeLiberaAlPasarLaVentana()
    {
        await IniciarAsync();
        var (id, token) = await EnPagoAsync();
        await Pago(HttpMethod.Post, "/caja", token);
        await PagarCajaAsync(id);
        var codigo = await CodigoPublicoAsync(id);

        for (var i = 0; i < 5; i++)
            (await Pago(HttpMethod.Get, $"/cierre/PN-2026-99999{i}", null)).StatusCode.Should().Be(HttpStatusCode.NotFound);

        var bloqueada = await Pago(HttpMethod.Get, $"/cierre/{codigo}", null);
        bloqueada.StatusCode.Should().Be(HttpStatusCode.TooManyRequests, "el bloqueo cubre también un código válido");
        (await PortalHost.CodigoAsync(bloqueada)).Should().Be("consulta_bloqueada");
        bloqueada.Headers.RetryAfter.Should().NotBeNull();

        _host.Reloj.Avanzar(TimeSpan.FromMinutes(11));
        (await Pago(HttpMethod.Get, $"/cierre/{codigo}", null)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task HU14464_ConsultarPorCodigo_UnAciertoLiberaLosFallosSeguidos()
    {
        await IniciarAsync();
        var (id, token) = await EnPagoAsync();
        await Pago(HttpMethod.Post, "/caja", token);
        await PagarCajaAsync(id);
        var codigo = await CodigoPublicoAsync(id);

        for (var ronda = 0; ronda < 2; ronda++)
        {
            for (var i = 0; i < 4; i++)
                (await Pago(HttpMethod.Get, $"/cierre/PN-2026-99999{i}", null)).StatusCode.Should().Be(HttpStatusCode.NotFound);
            (await Pago(HttpMethod.Get, $"/cierre/{codigo}", null)).StatusCode.Should().Be(HttpStatusCode.OK);
        }
    }

    [Fact]
    public async Task HU14464_ConsultarPorCodigo_MasDeDiezPorMinuto_Responde429RateLimited()
    {
        await IniciarAsync();
        var (id, token) = await EnPagoAsync();
        await Pago(HttpMethod.Post, "/caja", token);
        await PagarCajaAsync(id);
        var codigo = await CodigoPublicoAsync(id);

        for (var i = 0; i < 10; i++)
            (await Pago(HttpMethod.Get, $"/cierre/{codigo}", null)).StatusCode.Should().Be(HttpStatusCode.OK);

        var excedida = await Pago(HttpMethod.Get, $"/cierre/{codigo}", null);
        excedida.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        (await PortalHost.CodigoAsync(excedida)).Should().Be("rate_limited");
    }

    [Fact]
    public async Task HU14466_SiElTransporteCuelga_ElPagoNoEsperaMasDelTope_YElCierreLoReintenta()
    {
        await IniciarAsync();
        var (id, token) = await EnPagoAsync();
        await Pago(HttpMethod.Post, "/caja", token);
        _enviador.Cuelga = true;

        var reloj = System.Diagnostics.Stopwatch.StartNew();
        var pagado = await PagarCajaAsync(id);
        reloj.Stop();

        pagado.Should().NotBeNull();
        reloj.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(10));
        (await EstadoDeAsync(id)).Should().Be("pagada", "en caja espera la ventana de anulación");
        await using (var db = _host.NuevaDb())
            (await db.Set<CobroSolicitud>().SingleAsync(TestContext.Current.CancellationToken)).CorreoCierreEnviadoEn.Should().BeNull("el reclamo se libera para reintentar");

        _enviador.Cuelga = false;
        (await Pago(HttpMethod.Get, "/cierre", token)).StatusCode.Should().Be(HttpStatusCode.OK);
        _enviador.Enviados.Should().ContainSingle();
    }

    [Fact]
    public async Task HU14464_SinCookieDelPuntoNoHayCierre()
    {
        await IniciarAsync();
        var (_, token) = await EnPagoAsync();

        (await _host.EnviarAsync(HttpMethod.Get, $"{Punto}/pago/cierre", null, token)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await _host.EnviarAsync(HttpMethod.Get, $"{Punto}/pago/cierre/PN-2026-000001")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── HU #14466: correo de cierre ───────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task HU14466_AlQuedarPagada_SeEnviaUnCorreoAQuienRadico_ConCodigoYOrganismo()
    {
        await IniciarAsync();
        var (id, token) = await EnPagoAsync();
        await Pago(HttpMethod.Post, "/caja", token);

        await PagarCajaAsync(id);

        var (correo, html) = _enviador.Enviados.Should().ContainSingle().Subject;
        correo.Should().Be("vendedor@example.org", "quien radica es el primer rol del tipo");
        html.Should().Contain(await CodigoPublicoAsync(id)).And.Contain(System.Net.WebUtility.HtmlEncode("Tránsito de Prueba")).And.Contain("Persona de Prueba");
        foreach (var dato in new[] { Vendedor, Comprador, "3001234567", "comprador@example.org", "ABC123" })
            html.Should().NotContain(dato);
        _enviador.Plantillas.Should().ContainSingle().Which.Should().Be("persona-natural.cierre-punto");
    }

    [Fact]
    public async Task HU14466_SeEnviaTambienConUnTramiteSinCobro()
    {
        await IniciarAsync();
        var (_, token) = await EnPagoAsync();
        TotalCero();

        (await Pago(HttpMethod.Post, "/confirmar", token)).StatusCode.Should().Be(HttpStatusCode.OK);

        _enviador.Enviados.Should().ContainSingle().Which.Correo.Should().Be("vendedor@example.org");
    }

    [Fact]
    public async Task HU14466_ConsultasYReintentosDelCierre_NoReenvian()
    {
        await IniciarAsync();
        var (id, token) = await EnPagoAsync();
        await Pago(HttpMethod.Post, "/caja", token);
        await PagarCajaAsync(id);

        for (var i = 0; i < 3; i++)
        {
            (await Pago(HttpMethod.Get, "/cierre", token)).StatusCode.Should().Be(HttpStatusCode.OK);
            (await Pago(HttpMethod.Get, string.Empty, token)).StatusCode.Should().Be(HttpStatusCode.OK);
            await _host.EnScopeAsync(async sp => await sp.GetRequiredService<ICorreoDeCierre>().EnviarSiCorrespondeAsync(id, TestContext.Current.CancellationToken));
        }

        _enviador.Enviados.Should().HaveCount(1);
        await using var db = _host.NuevaDb();
        (await db.Set<CobroSolicitud>().SingleAsync(TestContext.Current.CancellationToken)).CorreoCierreEnviadoEn.Should().NotBeNull();
    }

    [Fact]
    public async Task HU14466_EnvioSimultaneo_SoloSaleUnCorreo()
    {
        await IniciarAsync(compuertaConmutable: true);
        var (id, token) = await EnPagoAsync();
        await Pago(HttpMethod.Post, "/caja", token);
        _compuerta.Permite = false;
        _enviador.Acepta = false; // el pago no envía
        await PagarCajaAsync(id);
        _enviador.Enviados.Clear();
        _enviador.Acepta = true;

        var resultados = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ =>
            _host.EnScopeAsync(sp => sp.GetRequiredService<ICorreoDeCierre>().EnviarSiCorrespondeAsync(id, TestContext.Current.CancellationToken))));

        resultados.Count(x => x).Should().Be(1);
        _enviador.Enviados.Should().HaveCount(1);
    }

    [Fact]
    public async Task HU14466_ConElCorreoTecnicoDelPunto_NoSeEnvia()
    {
        await IniciarAsync();
        var (id, token) = await EnPagoAsync();
        await Pago(HttpMethod.Post, "/caja", token);
        await GuardarCorreoDelVendedorAsync(id, CorreoTecnicoPunto.Para(null, id));

        await PagarCajaAsync(id);

        _enviador.Enviados.Should().BeEmpty("el correo técnico no es de la persona");
        await using var db = _host.NuevaDb();
        (await db.Set<CobroSolicitud>().SingleAsync(TestContext.Current.CancellationToken)).CorreoCierreEnviadoEn.Should().BeNull();
    }

    [Fact]
    public async Task HU14466_SinCorreoPropioDeQuienRadica_NoSeEnvia_AunqueOtraParteTengaCorreo()
    {
        await IniciarAsync();
        var (id, token) = await EnPagoAsync();
        await Pago(HttpMethod.Post, "/caja", token);
        await GuardarCorreoDelVendedorAsync(id, null);

        await PagarCajaAsync(id);

        _enviador.Enviados.Should().BeEmpty("el comprador (otra parte) tiene correo pero no es quien radicó");
    }

    [Fact]
    public async Task HU14466_SiElEnvioFalla_ElPagoSigueFirme_YLaSiguienteConsultaDelCierreLoReintentaUnaVez()
    {
        await IniciarAsync();
        var (id, token) = await EnPagoAsync();
        await Pago(HttpMethod.Post, "/caja", token);
        _enviador.Acepta = false;

        var pagado = await PagarCajaAsync(id);

        pagado.Should().NotBeNull();
        (await EstadoDeAsync(id)).Should().Be("pagada", "en caja espera la ventana de anulación");
        await using (var db = _host.NuevaDb())
            (await db.Set<CobroSolicitud>().SingleAsync(TestContext.Current.CancellationToken)).CorreoCierreEnviadoEn.Should().BeNull("se libera para reintentar");

        _enviador.Acepta = true;
        (await Pago(HttpMethod.Get, "/cierre", token)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await Pago(HttpMethod.Get, "/cierre", token)).StatusCode.Should().Be(HttpStatusCode.OK);

        _enviador.Enviados.Count(e => e.Correo == "vendedor@example.org").Should().Be(2, "un intento fallido y un único envío aceptado");
        await using var db2 = _host.NuevaDb();
        (await db2.Set<CobroSolicitud>().SingleAsync(TestContext.Current.CancellationToken)).CorreoCierreEnviadoEn.Should().NotBeNull();
    }

    [Fact]
    public async Task HU14466_SiElTransporteLanzaUnaExcepcion_ElPagoNoSeDeshace()
    {
        await IniciarAsync();
        var (id, token) = await EnPagoAsync();
        await Pago(HttpMethod.Post, "/caja", token);
        _enviador.Lanza = true;

        var pagado = await PagarCajaAsync(id);

        pagado.ValorCop.Should().Be(366_000);
        (await EstadoDeAsync(id)).Should().Be("pagada", "en caja espera la ventana de anulación");
        await using var db = _host.NuevaDb();
        (await db.Set<CobroSolicitud>().SingleAsync(c => c.Estado == "pagado", TestContext.Current.CancellationToken)).CorreoCierreEnviadoEn.Should().BeNull();
    }

    // ── Cierre y ventana de anulación de la caja (Feature #14453) ──────────────────────────────────────────────────────

    [Fact]
    public async Task HU14464_CobroEnCaja_DentroDeLaVentana_ElCierreEsEnRegistro_SinRadicar_YAlVencerRadica()
    {
        await IniciarAsync();
        var (id, token) = await EnPagoAsync();
        await Pago(HttpMethod.Post, "/caja", token);
        await PagarCajaAsync(id);

        _host.Reloj.Avanzar(TimeSpan.FromMinutes(14));
        var dentro = (await PortalHost.JsonAsync(await Pago(HttpMethod.Get, "/cierre", token))).RootElement;
        dentro.GetProperty("estadoCierre").GetString().Should().Be("en_registro");
        (await EstadoDeAsync(id)).Should().Be("pagada", "la ventana sigue abierta: no se radica");
        await using (var db = _host.NuevaDb())
            (await db.Set<OutboxMessage>().CountAsync(TestContext.Current.CancellationToken)).Should().Be(0);

        _host.Reloj.Avanzar(TimeSpan.FromMinutes(2));
        (await PortalHost.JsonAsync(await Pago(HttpMethod.Get, "/cierre", token))).RootElement.GetProperty("estadoCierre").GetString().Should().Be("en_registro");
        (await EstadoDeAsync(id)).Should().Be("en_cola", "al vencer la ventana el cierre radica");
    }

    [Fact]
    public async Task HU14464_CobroAnuladoEnLaVentana_ElCierrePorTokenEs409_YPorCodigoEs404Uniforme()
    {
        await IniciarAsync();
        var (id, token) = await EnPagoAsync();
        var codigo = await CodigoPublicoAsync(id);
        await Pago(HttpMethod.Post, "/caja", token);
        await PagarCajaAsync(id);
        var cobroId = await CobroPagadoAsync(id);
        _host.Reloj.Avanzar(TimeSpan.FromMinutes(5));
        await AnularAsync(cobroId);

        var porToken = await Pago(HttpMethod.Get, "/cierre", token);
        porToken.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await PortalHost.JsonAsync(porToken)).RootElement.GetProperty("code").GetString().Should().Be("pago_pendiente");
        var porCodigo = await Pago(HttpMethod.Get, $"/cierre/{codigo}", null);
        porCodigo.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await PortalHost.JsonAsync(porCodigo)).RootElement.GetProperty("code").GetString().Should().Be("no_encontrado");
    }

    // ── Soporte ──────────────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>Simula la respuesta de FLIT 2.0: la solicitud pasa a creada (con radicado) o rechazada_creacion (con motivo en el historial).</summary>
    private async Task ResolverAsync(Guid id, string hasta, string? radicado, string? motivo = null)
    {
        await using var db = _host.NuevaDb();
        var s = await db.Set<SolicitudTramite>().SingleAsync(x => x.Id == id, TestContext.Current.CancellationToken);
        db.Add(new HistorialEstado { Id = Guid.CreateVersion7(), SolicitudId = id, Desde = s.Estado, Hasta = hasta, Origen = "flit2", Motivo = motivo, OcurrioEn = _host.Reloj.GetUtcNow() });
        s.Estado = hasta;
        s.ReferenceNumber = radicado;
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private Task<bool> GuardarCorreoDelVendedorAsync(Guid id, string? correo) =>
        _host.EnScopeAsync(async sp =>
        {
            await sp.GetRequiredService<PersonaService>().GuardarAsync(
                id, new DatosPersona("vendedor", 1, "CC", Vendedor, "Persona de Prueba", correo, "3001234567", null), TestContext.Current.CancellationToken);
            return true;
        });

    /// <summary>El cierre sin la propiedad <c>resumen</c>, para comparar la consulta pública con el de la sesión.</summary>
    private static string SinResumen(JsonElement cierre) =>
        JsonSerializer.Serialize(cierre.EnumerateObject().Where(p => p.Name != "resumen").ToDictionary(p => p.Name, p => p.Value));
}
