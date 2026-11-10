using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Flit.PersonaNatural.Api.Adjuntos;
using Flit.PersonaNatural.Api.Caja;
using Flit.PersonaNatural.Api.Definiciones;
using Flit.PersonaNatural.Api.Persistence;
using Flit.PersonaNatural.Grpc.V1;
using Flit.Platform.Sdk;
using Flit.Platform.Sdk.Grpc;
using FluentAssertions;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Flit.PersonaNatural.Tests;

/// <summary>Resolutor de cajeros de prueba: quién está activo y de qué organismo; cuenta las preguntas (no hay caché).</summary>
internal sealed class ResolverCajeroFalso : IResolverCajero
{
    public Dictionary<Guid, Cajero> Activos { get; } = [];
    public int Llamadas;

    public Task<Cajero?> ResolverAsync(Guid tenantId, Guid usuarioId, CancellationToken ct)
    {
        Interlocked.Increment(ref Llamadas);
        return Task.FromResult(Activos.TryGetValue(usuarioId, out var c) ? c : null);
    }
}

/// <summary>
/// Feature #14453 — portal de caja del organismo (BFF): autenticación y organismo (ADR-0082), HU #14456 (buscar), #14457
/// (confirmar), #14459 (lista del día) y #14461 (administrar cobros por gRPC). Sobre el host real, una base efímera propia y un
/// esquema de autenticación de prueba que arma el principal desde cabeceras (sin firmar JWT).
/// </summary>
public sealed class CajaTests : IAsyncLifetime
{
    private const string Ruta = "/api/v1/persona-natural/caja";
    private static readonly Guid OrganismoA = Guid.Parse(PortalHost.Organismo);
    private static readonly Guid OrganismoB = Guid.Parse("0199b7e0-0000-7000-8000-0000000000b2");
    private static readonly Guid Empresa = PlatformTenants.PersonaNatural;

    private static readonly Cajero Ana = new(Guid.Parse("00000000-0000-0000-0000-00000000a001"), "Ana Caja", OrganismoA, "AB1", "Tránsito de Prueba");
    private static readonly Cajero Beto = new(Guid.Parse("00000000-0000-0000-0000-00000000a002"), "Beto Caja", OrganismoA, "AB1", "Tránsito de Prueba");
    private static readonly Cajero Carla = new(Guid.Parse("00000000-0000-0000-0000-00000000b001"), "Carla Caja", OrganismoB, "ZZ9", "Otro Tránsito");

    private readonly ResolverCajeroFalso _resolver = new();
    private readonly AlmacenEnMemoria _almacen = new();
    private PortalHost _host = null!;
    private bool _iniciado;
    private int _secuencia;

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        if (_iniciado)
            await _host.DisposeAsync();
    }

    private async Task IniciarAsync(bool habilitado = true, params (string Clave, string Valor)[] ajustes)
    {
        foreach (var c in new[] { Ana, Beto, Carla })
            _resolver.Activos[c.UsuarioId] = c;
        (string, string)[] todos = [("Caja:Habilitado", habilitado ? "true" : "false"), .. ajustes];
        _host = await PortalHost.IniciarAsync("pncajafix", (servicios, _) =>
        {
            servicios.AddSingleton<IResolverCajero>(_resolver);
            servicios.AddSingleton<IAlmacenAdjuntos>(_almacen);
            servicios.AddAuthentication().AddScheme<AuthenticationSchemeOptions, EsquemaDeAutenticacionDePrueba>(EsquemaDeAutenticacionDePrueba.Nombre, _ => { });
            servicios.PostConfigure<AuthenticationOptions>(o =>
            {
                o.DefaultAuthenticateScheme = EsquemaDeAutenticacionDePrueba.Nombre;
                o.DefaultChallengeScheme = EsquemaDeAutenticacionDePrueba.Nombre;
            });
        }, todos);
        _iniciado = true;
        _host.Definiciones.PorCodigo["TRASPASO_STANDARD"] = DefinicionesDePrueba.Traspaso();
    }

    // ── Autenticación y organismo ────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SinToken_Responde401SinSesion()
    {
        await IniciarAsync();

        var r = await _host.EnviarAsync(HttpMethod.Get, $"{Ruta}/sesion");

        r.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await PortalHost.CodigoAsync(r)).Should().Be("sin_sesion");
        r.Headers.CacheControl!.NoStore.Should().BeTrue();
        _resolver.Llamadas.Should().Be(0);
    }

    [Theory]
    [InlineData("otro-producto", "ot_caja", "caja")]
    [InlineData("caja", "ot_admin", "caja")]
    [InlineData("caja", "ot_caja", "personanatural")]
    public async Task UnTokenQueNoEsDeCaja_Responde403SinPermiso_YNoPreguntaPorElUsuario(string producto, string rol, string audiencia)
    {
        await IniciarAsync();

        var r = await Enviar(HttpMethod.Get, "/sesion", Ana, producto: producto, rol: rol, audiencia: audiencia);

        r.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await PortalHost.CodigoAsync(r)).Should().Be("sin_permiso");
        _resolver.Llamadas.Should().Be(0);
    }

    [Fact]
    public async Task UnTokenDeServicioNoEsUnaCaja()
    {
        await IniciarAsync();

        var r = await Enviar(HttpMethod.Get, "/sesion", Ana, sub: "svc-tramites");

        r.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task UnUsuarioDesactivadoPierdeElAccesoEnLaSiguientePeticion_SinCache()
    {
        await IniciarAsync();
        (await Enviar(HttpMethod.Get, "/sesion", Ana)).StatusCode.Should().Be(HttpStatusCode.OK);

        _resolver.Activos.Remove(Ana.UsuarioId);
        var r = await Enviar(HttpMethod.Get, "/sesion", Ana);

        r.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await PortalHost.CodigoAsync(r)).Should().Be("sin_permiso");
        _resolver.Llamadas.Should().Be(2, "se pregunta en cada petición");
    }

    [Fact]
    public async Task ElOrganismoSalePorElResolutor_NuncaDelCliente()
    {
        await IniciarAsync();
        var codigo = await CrearAsync(organismo: OrganismoB);

        // Carla pide con cabeceras y consulta que intentan elegir el organismo A: se ignoran.
        var sesion = await Enviar(HttpMethod.Get, "/sesion?organismoId=" + OrganismoA, Carla, extra: [("X-Organismo-Id", OrganismoA.ToString())]);
        var busqueda = await Enviar(HttpMethod.Get, $"/tramites/{codigo}?organismoId={OrganismoA}", Carla, extra: [("X-Organismo-Id", OrganismoA.ToString())]);

        var json = (await PortalHost.JsonAsync(sesion)).RootElement;
        json.GetProperty("organismo").GetProperty("id").GetGuid().Should().Be(OrganismoB);
        json.GetProperty("usuario").GetProperty("nombre").GetString().Should().Be("Carla Caja");
        json.GetProperty("expiraEn").GetDateTimeOffset().Should().Be(_host.Reloj.GetUtcNow().AddMinutes(-1).AddHours(12));
        busqueda.StatusCode.Should().Be(HttpStatusCode.OK, "es el organismo B de Carla, no el A del cliente");
    }

    [Fact]
    public async Task UnTokenConMasDe12HorasDeSesion_Responde401()
    {
        await IniciarAsync();

        var r = await Enviar(HttpMethod.Get, "/sesion", Ana, iat: _host.Reloj.GetUtcNow().AddHours(-12).AddMinutes(-1));

        r.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await PortalHost.CodigoAsync(r)).Should().Be("sin_sesion");
        (await Enviar(HttpMethod.Get, "/sesion", Ana, iat: _host.Reloj.GetUtcNow().AddHours(-11))).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ConLaCajaApagada_NoExisteNingunaRutaDeCaja()
    {
        await IniciarAsync(habilitado: false);

        var r = await Enviar(HttpMethod.Get, "/sesion", Ana);

        r.StatusCode.Should().Be(HttpStatusCode.NotFound);
        _host.App.Services.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>().Get(JwtBearerDefaults.AuthenticationScheme)
            .TokenValidationParameters.ValidAudiences?.Should().NotContain("caja", "con la caja apagada el BFF solo acepta aud=personanatural");
    }

    [Fact]
    public async Task ConLaCajaEncendida_ElBffAceptaLaAudienciaCaja_ConfigurandoElJwt()
    {
        await IniciarAsync();

        var jwt = _host.App.Services.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>().Get(JwtBearerDefaults.AuthenticationScheme).TokenValidationParameters;

        jwt.ValidAudiences.Should().Contain("caja");
        jwt.ValidAudience.Should().Be("personanatural");
    }

    // ── HU #14456: buscar el trámite por su ID ───────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task HU14456_UnCobroVigente_DevuelveTipoOrganismoDesglose_Total_SinDatosDeLasPartes()
    {
        await IniciarAsync();
        var codigo = await CrearAsync();

        var r = await Enviar(HttpMethod.Get, $"/tramites/{codigo.ToLowerInvariant()}", Ana);

        r.StatusCode.Should().Be(HttpStatusCode.OK);
        var texto = await r.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var json = JsonDocument.Parse(texto).RootElement;
        json.GetProperty("codigo").GetString().Should().Be(codigo);
        json.GetProperty("tipoTramite").GetProperty("codigo").GetString().Should().Be("TRASPASO_STANDARD");
        json.GetProperty("tipoTramite").GetProperty("nombre").GetString().Should().Be("Traspaso");
        json.GetProperty("organismo").GetProperty("nombre").GetString().Should().Be("Tránsito de Prueba");
        json.GetProperty("totalCop").GetInt64().Should().Be(366_000);
        json.GetProperty("lineas").GetArrayLength().Should().Be(2);
        json.GetProperty("lineas")[0].GetProperty("concepto").GetString().Should().Be("Valor del Servicio de tránsito");
        json.GetProperty("versionTarifa").GetString().Should().NotBeNullOrEmpty();
        json.GetProperty("venceEn").GetDateTimeOffset().Should().Be(new DateTimeOffset(2026, 10, 27, 23, 59, 59, TimeSpan.FromHours(-5)));
        texto.Should().NotContain("ABC123", "la caja no ve la placa").And.NotContain("Persona de Prueba").And.NotContain("7001234567").And.NotContain("documento").And.NotContain("placa");
    }

    [Fact]
    public async Task HU14456_UnIdQueNoExiste_OConFormatoInvalido_Responde404SinDatos()
    {
        await IniciarAsync();

        var inexistente = await Enviar(HttpMethod.Get, "/tramites/PN-2026-999999", Ana);
        var malo = await Enviar(HttpMethod.Get, "/tramites/DROP-TABLE", Ana);

        foreach (var r in new[] { inexistente, malo })
        {
            r.StatusCode.Should().Be(HttpStatusCode.NotFound);
            var json = (await PortalHost.JsonAsync(r)).RootElement;
            json.GetProperty("code").GetString().Should().Be("no_encontrado");
            json.GetProperty("message").GetString().Should().Be("No encontramos ese trámite.");
            json.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo("code", "message");
        }
    }

    [Fact]
    public async Task HU14456_UnTramiteDeOtroOrganismo_Responde403OtroOrganismo_SinNingunDato()
    {
        await IniciarAsync();
        var codigo = await CrearAsync(organismo: OrganismoB);

        var r = await Enviar(HttpMethod.Get, $"/tramites/{codigo}", Ana);

        r.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var json = (await PortalHost.JsonAsync(r)).RootElement;
        json.GetProperty("code").GetString().Should().Be("otro_organismo");
        json.GetProperty("message").GetString().Should().Be("Este trámite no es de este organismo.");
        json.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo("code", "message");
    }

    [Fact]
    public async Task HU14456_UnTramiteYaPagado_Responde409YaPagado()
    {
        await IniciarAsync();
        var codigo = await CrearAsync();
        (await Confirmar(codigo, Ana, "efectivo")).StatusCode.Should().Be(HttpStatusCode.OK);

        var r = await Enviar(HttpMethod.Get, $"/tramites/{codigo}", Beto);

        r.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await PortalHost.CodigoAsync(r)).Should().Be("ya_pagado");
    }

    [Fact]
    public async Task HU14456_ConElCodigoVencido_Responde409CobroVencido()
    {
        await IniciarAsync();
        var codigo = await CrearAsync();

        _host.Reloj.Avanzar(TimeSpan.FromDays(1));
        var r = await Enviar(HttpMethod.Get, $"/tramites/{codigo}", Ana);

        r.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await PortalHost.CodigoAsync(r)).Should().Be("cobro_vencido");
    }

    [Theory]
    [InlineData("borrador", false)]
    [InlineData("pendiente_pago", false)]
    [InlineData("cancelada", false)]
    public async Task HU14456_SinElPagoEnCajaHabilitado_Responde409NoHabilitado(string estado, bool habilitado)
    {
        await IniciarAsync();
        var codigo = await CrearAsync(estado: estado, habilitado: habilitado);

        var r = await Enviar(HttpMethod.Get, $"/tramites/{codigo}", Ana);

        r.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await PortalHost.CodigoAsync(r)).Should().Be("no_habilitado");
    }

    [Fact]
    public async Task HU14456_ConElMedioPinApagado_ElTramiteNoTieneElPagoEnCajaHabilitado()
    {
        await IniciarAsync();
        var codigo = await CrearAsync();
        _host.Medios.MedioDisponible = false;

        var r = await Enviar(HttpMethod.Get, $"/tramites/{codigo}", Ana);

        r.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await PortalHost.CodigoAsync(r)).Should().Be("no_habilitado");
    }

    [Fact]
    public async Task HU14456_ElDesgloseSeRecalculaEnCadaBusqueda()
    {
        await IniciarAsync();
        var codigo = await CrearAsync();
        (await Enviar(HttpMethod.Get, $"/tramites/{codigo}", Ana)).StatusCode.Should().Be(HttpStatusCode.OK);

        _host.Medios.Derechos = 200_000;
        var json = (await PortalHost.JsonAsync(await Enviar(HttpMethod.Get, $"/tramites/{codigo}", Ana))).RootElement;

        json.GetProperty("totalCop").GetInt64().Should().Be(381_000);
    }

    [Fact]
    public async Task HU14456_DiezBusquedasSinResultadoEnCincoMinutos_BloqueanAlUsuario_Responde429ConRetryAfter()
    {
        await IniciarAsync();
        for (var i = 0; i < 10; i++)
            (await Enviar(HttpMethod.Get, $"/tramites/PN-2026-9000{i:D2}", Ana)).StatusCode.Should().Be(HttpStatusCode.NotFound);

        var bloqueada = await Enviar(HttpMethod.Get, "/tramites/PN-2026-900099", Ana);

        bloqueada.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        (await PortalHost.CodigoAsync(bloqueada)).Should().Be("demasiados_intentos");
        var espera = int.Parse(bloqueada.Headers.GetValues("Retry-After").Single());
        espera.Should().BeInRange(1, 300);
        (await PortalHost.JsonAsync(bloqueada)).RootElement.GetProperty("message").GetString().Should().Be("Demasiadas búsquedas sin resultado. Espere unos minutos.");

        // El tope es POR usuario: otra caja del mismo organismo sigue buscando.
        var existente = await CrearAsync();
        (await Enviar(HttpMethod.Get, $"/tramites/{existente}", Beto)).StatusCode.Should().Be(HttpStatusCode.OK);
        // Un trámite real tampoco se encuentra mientras dure el bloqueo (ni siquiera uno bueno).
        (await Enviar(HttpMethod.Get, $"/tramites/{existente}", Ana)).StatusCode.Should().Be(HttpStatusCode.TooManyRequests);

        _host.Reloj.Avanzar(TimeSpan.FromMinutes(5) + TimeSpan.FromSeconds(1));
        (await Enviar(HttpMethod.Get, $"/tramites/{existente}", Ana)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task HU14456_LasBusquedasQueSiEncuentranElTramite_NoCuentanContraElTope()
    {
        await IniciarAsync();
        var pagado = await CrearAsync();
        (await Confirmar(pagado, Ana, "efectivo")).StatusCode.Should().Be(HttpStatusCode.OK);

        for (var i = 0; i < 15; i++)
            (await Enviar(HttpMethod.Get, $"/tramites/{pagado}", Ana)).StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task HU14456_LaAuditoriaGuardaLaIpComoHuella_YNoElCodigoCompletoEnLosLogs()
    {
        await IniciarAsync();
        var codigo = await CrearAsync();

        await Enviar(HttpMethod.Get, $"/tramites/{codigo}", Ana);
        await Enviar(HttpMethod.Get, "/tramites/PN-2026-999999", Ana);

        await using var db = _host.NuevaDb();
        var eventos = await db.Set<EventoCaja>().OrderBy(e => e.CreadoEn).ToListAsync(TestContext.Current.CancellationToken);
        eventos.Select(e => (e.Accion, e.Resultado)).Should().Contain([("busqueda_ok", "ok"), ("busqueda_fallida", "no_encontrado")]);
        eventos.Should().OnlyContain(e => e.OrganismoId == OrganismoA && e.UsuarioId == Ana.UsuarioId);
        eventos.Should().OnlyContain(e => e.IpHuella == null || e.IpHuella.Length == 64);
        var huella = CajaAuditoria.Nuevo(OrganismoA, Ana.UsuarioId, "busqueda_ok", codigo, "ok", "203.0.113.9", _host.Reloj.GetUtcNow(), _host.App.Services.GetRequiredService<Flit.PersonaNatural.Api.Sesiones.PiiHasher>()).IpHuella;
        huella.Should().HaveLength(64).And.NotContain("203.0.113.9").And.MatchRegex("^[0-9a-f]{64}$");
        lock (_host.Logs.Mensajes)
            _host.Logs.Mensajes.Should().NotContain(m => m.Contains(codigo));
    }

    // ── HU #14457: confirmar con el medio y la prefactura ────────────────────────────────────────────────────────────

    [Fact]
    public async Task HU14457_Confirmar_RegistraElPagoConElUsuario_ElMedio_YNoRadicaDeInmediato()
    {
        await IniciarAsync();
        var codigo = await CrearAsync();

        var r = await Confirmar(codigo, Ana, "efectivo");

        r.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = (await PortalHost.JsonAsync(r)).RootElement;
        json.GetProperty("codigo").GetString().Should().Be(codigo);
        json.GetProperty("valorCop").GetInt64().Should().Be(366_000);
        json.GetProperty("medio").GetString().Should().Be("efectivo");
        json.GetProperty("mensaje").GetString().Should().Be("Pago registrado.");
        await using var db = _host.NuevaDb();
        var c = await db.Set<CobroSolicitud>().SingleAsync(x => x.Codigo == codigo, TestContext.Current.CancellationToken);
        (c.Estado, c.ValorCop, c.Medio, c.MedioCaja, c.PagadoPorId, c.PagadoPorNombre).Should().Be(("pagado", 366_000L, "caja", "efectivo", Ana.UsuarioId, "Ana Caja"));
        c.PagadoEnDia.Should().Be(new DateOnly(2026, 10, 27));
        (await db.Set<SolicitudTramite>().SingleAsync(TestContext.Current.CancellationToken)).Estado.Should().Be("pagada", "espera la ventana de anulación");
        (await db.Set<EventoCaja>().AnyAsync(e => e.Accion == "confirmar" && e.Resultado == "ok", TestContext.Current.CancellationToken)).Should().BeTrue();
    }

    [Theory]
    [InlineData(null, null, "medio_invalido")]
    [InlineData("", null, "medio_invalido")]
    [InlineData("tarjeta", null, "medio_invalido")]
    [InlineData("otro", null, "detalle_requerido")]
    [InlineData("otro", "   ", "detalle_requerido")]
    public async Task HU14457_ElMedioEsObligatorio_YOtroExigeDetalle_Responde400(string? medio, string? detalle, string codigoError)
    {
        await IniciarAsync();
        var codigo = await CrearAsync();

        var r = await Confirmar(codigo, Ana, medio, detalle);

        r.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await PortalHost.CodigoAsync(r)).Should().Be(codigoError);
        await using var db = _host.NuevaDb();
        (await db.Set<CobroSolicitud>().CountAsync(c => c.Estado == "pagado", TestContext.Current.CancellationToken)).Should().Be(0);
    }

    [Fact]
    public async Task HU14457_ElDetalleDeOtro_SeGuarda_YTieneTopeDe120()
    {
        await IniciarAsync();
        var codigo = await CrearAsync();

        (await Confirmar(codigo, Ana, "otro", new string('x', 121))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var ok = await Confirmar(codigo, Ana, "otro", " Nequi ");

        ok.StatusCode.Should().Be(HttpStatusCode.OK);
        (await PortalHost.JsonAsync(ok)).RootElement.GetProperty("medioDetalle").GetString().Should().Be("Nequi");
    }

    [Fact]
    public async Task HU14457_FaltaLaIdempotencyKey_Responde400()
    {
        await IniciarAsync();
        var codigo = await CrearAsync();

        var r = await Confirmar(codigo, Ana, "efectivo", llave: "");

        r.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await PortalHost.CodigoAsync(r)).Should().Be("idempotency_key_requerida");
    }

    [Fact]
    public async Task HU14457_LaPrefacturaPdfJpgOPng_SeGuardaLigadaAlCobro_SinViajarAFlit2()
    {
        await IniciarAsync();
        var codigo = await CrearAsync();

        var r = await Confirmar(codigo, Ana, "datafono", prefactura: ("prefactura.pdf", Archivos.Pdf(64)));

        r.StatusCode.Should().Be(HttpStatusCode.OK);
        await using var db = _host.NuevaDb();
        var cobro = await db.Set<CobroSolicitud>().SingleAsync(x => x.Codigo == codigo, TestContext.Current.CancellationToken);
        cobro.PrefacturaAdjuntoId.Should().NotBeNull();
        var adjunto = await db.Set<Adjunto>().SingleAsync(a => a.Id == cobro.PrefacturaAdjuntoId, TestContext.Current.CancellationToken);
        (adjunto.Tipo, adjunto.MimeType).Should().Be(("prefactura_caja", "application/pdf"));
        _almacen.Archivos.Should().HaveCount(1);

        // Ni la persona ni FLIT 2.0 reciben la prefactura: el soporte es de la caja y del administrador del OT.
        var visibles = await db.Set<Adjunto>().Where(a => a.SolicitudId == cobro.SolicitudId && a.Tipo != Flit.PersonaNatural.Api.Adjuntos.AdjuntosService.TipoPrefacturaCaja).CountAsync(TestContext.Current.CancellationToken);
        visibles.Should().Be(0);
    }

    [Theory]
    [InlineData("png")]
    [InlineData("jpg")]
    public async Task HU14457_LaPrefacturaJpgOPngSeAcepta(string tipo)
    {
        await IniciarAsync();
        var codigo = await CrearAsync();

        var r = await Confirmar(codigo, Ana, "transferencia", prefactura: ($"soporte.{tipo}", tipo == "png" ? Archivos.Png() : Archivos.Jpeg()));

        r.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task HU14457_LaPrefacturaSeValidaPorLaFirmaDelArchivo_NoPorLaExtension()
    {
        await IniciarAsync();
        var codigo = await CrearAsync();

        var ejecutable = await Confirmar(codigo, Ana, "efectivo", prefactura: ("prefactura.pdf", Archivos.Ejecutable()));
        var webp = await Confirmar(codigo, Ana, "efectivo", prefactura: ("prefactura.webp", Archivos.Webp()));
        var vacia = await Confirmar(codigo, Ana, "efectivo", prefactura: ("prefactura.pdf", Array.Empty<byte>()));

        foreach (var r in new[] { ejecutable, webp, vacia })
        {
            r.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await PortalHost.CodigoAsync(r)).Should().Be("prefactura_invalida");
        }

        await using var db = _host.NuevaDb();
        (await db.Set<CobroSolicitud>().CountAsync(c => c.Estado == "pagado", TestContext.Current.CancellationToken)).Should().Be(0);
        _almacen.Archivos.Should().BeEmpty();
    }

    [Fact]
    public async Task HU14457_UnaPrefacturaDeMasDe5MB_SeRechaza_ConLoConfigurado()
    {
        await IniciarAsync(ajustes: ("Caja:PrefacturaMaxBytes", "2048"));
        var codigo = await CrearAsync();

        var grande = await Confirmar(codigo, Ana, "efectivo", prefactura: ("prefactura.pdf", Archivos.Pdf(4096)));
        var justa = await Confirmar(codigo, Ana, "efectivo", prefactura: ("prefactura.pdf", Archivos.Pdf(1000)));

        grande.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await PortalHost.CodigoAsync(grande)).Should().Be("prefactura_invalida");
        justa.StatusCode.Should().Be(HttpStatusCode.OK);
        new CajaOptions().PrefacturaMaxBytes.Should().Be(5 * 1024 * 1024);
    }

    [Fact]
    public async Task HU14457_SiElTotalCambio_NoSeCobra_ResponderTotalCambioConElNuevoValor_YLaPrefacturaNoQuedaHuerfana()
    {
        await IniciarAsync();
        var codigo = await CrearAsync();
        _host.Medios.Derechos = 200_000;

        var r = await Confirmar(codigo, Ana, "efectivo", totalVisto: 366_000, prefactura: ("p.pdf", Archivos.Pdf()));

        r.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var json = (await PortalHost.JsonAsync(r)).RootElement;
        json.GetProperty("code").GetString().Should().Be("total_cambio");
        json.GetProperty("message").GetString().Should().Be("El total cambió; revise el nuevo valor.");
        json.GetProperty("totalActualCop").GetInt64().Should().Be(381_000);
        await using var db = _host.NuevaDb();
        (await db.Set<CobroSolicitud>().CountAsync(c => c.Estado == "pagado", TestContext.Current.CancellationToken)).Should().Be(0);
        (await db.Set<Adjunto>().CountAsync(a => a.Tipo == "prefactura_caja", TestContext.Current.CancellationToken)).Should().Be(0);
        (await db.Set<SolicitudTramite>().SingleAsync(TestContext.Current.CancellationToken)).Estado.Should().Be("pendiente_pago");

        // Con el total nuevo a la vista, la caja confirma y se cobra el valor del servidor.
        var ok = await Confirmar(codigo, Ana, "efectivo", totalVisto: 381_000);
        (await PortalHost.JsonAsync(ok)).RootElement.GetProperty("valorCop").GetInt64().Should().Be(381_000);
    }

    [Fact]
    public async Task HU14457_ElValorQueEnviaElCliente_NoSeUsaComoValorDelCobro()
    {
        await IniciarAsync();
        var codigo = await CrearAsync();

        using var form = Formulario("efectivo", totalVisto: null);
        form.Add(new StringContent("1"), "valorCop");
        form.Add(new StringContent("1"), "totalCop");
        var r = await Enviar(HttpMethod.Post, $"/tramites/{codigo}/confirmar", Ana, contenido: form, llave: "k-valor");

        r.StatusCode.Should().Be(HttpStatusCode.OK);
        (await PortalHost.JsonAsync(r)).RootElement.GetProperty("valorCop").GetInt64().Should().Be(366_000);
    }

    [Fact]
    public async Task HU14457_ConfirmarUnTramiteDeOtroOrganismo_Responde403_YNoSeCobra()
    {
        await IniciarAsync();
        var codigo = await CrearAsync(organismo: OrganismoB);

        var r = await Confirmar(codigo, Ana, "efectivo");

        r.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await PortalHost.CodigoAsync(r)).Should().Be("otro_organismo");
        await using var db = _host.NuevaDb();
        (await db.Set<CobroSolicitud>().CountAsync(c => c.Estado == "pagado", TestContext.Current.CancellationToken)).Should().Be(0);
    }

    [Fact]
    public async Task HU14457_ConfirmarUnIdInexistente_Responde404()
    {
        await IniciarAsync();

        var r = await Confirmar("PN-2026-999999", Ana, "efectivo");

        r.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await PortalHost.CodigoAsync(r)).Should().Be("no_encontrado");
    }

    [Fact]
    public async Task HU14457_ConfirmarConElCodigoVencido_Responde409CobroVencido()
    {
        await IniciarAsync();
        var codigo = await CrearAsync();

        _host.Reloj.Avanzar(TimeSpan.FromDays(1));
        var r = await Confirmar(codigo, Ana, "efectivo");

        r.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await PortalHost.CodigoAsync(r)).Should().Be("cobro_vencido");
    }

    [Fact]
    public async Task HU14457_ConfirmarSinElPagoEnCajaHabilitado_Responde409NoHabilitado()
    {
        await IniciarAsync();
        var codigo = await CrearAsync(habilitado: false);

        var r = await Confirmar(codigo, Ana, "efectivo");

        r.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await PortalHost.CodigoAsync(r)).Should().Be("no_habilitado");
    }

    [Fact]
    public async Task HU14457_RepetirLaMismaConfirmacion_DevuelveElMismoResultadoYNoCobraDosVeces()
    {
        await IniciarAsync();
        var codigo = await CrearAsync();

        var primera = await Confirmar(codigo, Ana, "efectivo", llave: "clave-1");
        var repetida = await Confirmar(codigo, Ana, "efectivo", llave: "clave-1");
        var otraClave = await Confirmar(codigo, Ana, "efectivo", llave: "clave-2");
        var otraCaja = await Confirmar(codigo, Beto, "efectivo", llave: "clave-1");

        primera.StatusCode.Should().Be(HttpStatusCode.OK);
        repetida.StatusCode.Should().Be(HttpStatusCode.OK);
        (await PortalHost.JsonAsync(repetida)).RootElement.GetProperty("pagadoEn").GetDateTimeOffset()
            .Should().Be((await PortalHost.JsonAsync(primera)).RootElement.GetProperty("pagadoEn").GetDateTimeOffset());
        otraClave.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await PortalHost.CodigoAsync(otraClave)).Should().Be("ya_pagado");
        otraCaja.StatusCode.Should().Be(HttpStatusCode.Conflict, "la clave solo vale para la misma caja");
        await using var db = _host.NuevaDb();
        (await db.Set<CobroSolicitud>().CountAsync(c => c.Estado == "pagado", TestContext.Current.CancellationToken)).Should().Be(1);
    }

    [Fact]
    public async Task HU14457_DosCajasQueConfirmanAlMismoTiempo_SoloUnaCobra_LaOtraRecibeYaPagado_SinPrefacturaHuerfana()
    {
        await IniciarAsync();
        var codigo = await CrearAsync();

        var respuestas = await Task.WhenAll(Enumerable.Range(0, 6).Select(i =>
            Confirmar(codigo, i % 2 == 0 ? Ana : Beto, "efectivo", llave: $"k-{i}", prefactura: ("p.pdf", Archivos.Pdf(i)))));

        respuestas.Count(r => r.StatusCode == HttpStatusCode.OK).Should().Be(1);
        foreach (var perdedora in respuestas.Where(r => r.StatusCode != HttpStatusCode.OK))
        {
            perdedora.StatusCode.Should().Be(HttpStatusCode.Conflict);
            (await PortalHost.CodigoAsync(perdedora)).Should().Be("ya_pagado");
        }

        await using var db = _host.NuevaDb();
        (await db.Set<CobroSolicitud>().CountAsync(c => c.Estado == "pagado", TestContext.Current.CancellationToken)).Should().Be(1);
        (await db.Set<Adjunto>().CountAsync(a => a.Tipo == "prefactura_caja", TestContext.Current.CancellationToken)).Should().Be(1, "solo queda la prefactura del cobro ganador");
    }

    // ── HU #14459: cobros del día ────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task HU14459_LaListaDeHoy_TraeSoloLosCobrosDeSuOrganismo_ConValorMedioHoraYUsuario()
    {
        await IniciarAsync();
        var a1 = await CrearAsync();
        var a2 = await CrearAsync();
        var b1 = await CrearAsync(organismo: OrganismoB);
        _host.Reloj.Avanzar(TimeSpan.FromHours(2)); // 12:00 en Colombia
        (await Confirmar(a1, Ana, "efectivo")).StatusCode.Should().Be(HttpStatusCode.OK);
        _host.Reloj.Avanzar(TimeSpan.FromMinutes(30));
        (await Confirmar(a2, Beto, "otro", "Nequi")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await Confirmar(b1, Carla, "datafono")).StatusCode.Should().Be(HttpStatusCode.OK);

        var json = (await PortalHost.JsonAsync(await Enviar(HttpMethod.Get, "/cobros/hoy", Ana))).RootElement;

        json.GetProperty("fecha").GetString().Should().Be("2026-10-27");
        json.GetProperty("totalDiaCop").GetInt64().Should().Be(732_000);
        json.GetProperty("total").GetInt32().Should().Be(2);
        json.GetProperty("pagina").GetInt32().Should().Be(1);
        json.GetProperty("tamano").GetInt32().Should().Be(10);
        var items = json.GetProperty("items");
        items.GetArrayLength().Should().Be(2);
        items[0].GetProperty("codigo").GetString().Should().Be(a2, "el más reciente primero");
        items[0].GetProperty("valorCop").GetInt64().Should().Be(366_000);
        items[0].GetProperty("medio").GetString().Should().Be("otro");
        items[0].GetProperty("medioDetalle").GetString().Should().Be("Nequi");
        items[0].GetProperty("hora").GetString().Should().Be("12:30");
        items[0].GetProperty("usuario").GetString().Should().Be("Beto Caja");
        items[0].GetProperty("estado").GetString().Should().Be("pagado");
        items[1].GetProperty("usuario").GetString().Should().Be("Ana Caja");
        items.EnumerateArray().Select(i => i.GetProperty("codigo").GetString()).Should().NotContain(b1);
    }

    [Fact]
    public async Task HU14459_ElDiaEsElDeColombia_AyerNoSale_YLaPaginacionRespetaElTope()
    {
        await IniciarAsync();
        var ayer = await CrearAsync();
        (await Confirmar(ayer, Ana, "efectivo")).StatusCode.Should().Be(HttpStatusCode.OK);
        _host.Reloj.Avanzar(TimeSpan.FromDays(1)); // 28 de octubre, 10:00 en Colombia
        var hoy = new List<string>();
        for (var i = 0; i < 3; i++)
        {
            var c = await CrearAsync();
            hoy.Add(c);
            (await Confirmar(c, Ana, "efectivo")).StatusCode.Should().Be(HttpStatusCode.OK);
            _host.Reloj.Avanzar(TimeSpan.FromMinutes(1));
        }

        var pagina1 = (await PortalHost.JsonAsync(await Enviar(HttpMethod.Get, "/cobros/hoy?pagina=1&tamano=2", Ana))).RootElement;
        var pagina2 = (await PortalHost.JsonAsync(await Enviar(HttpMethod.Get, "/cobros/hoy?pagina=2&tamano=2", Ana))).RootElement;
        var enorme = (await PortalHost.JsonAsync(await Enviar(HttpMethod.Get, "/cobros/hoy?tamano=5000", Ana))).RootElement;

        pagina1.GetProperty("fecha").GetString().Should().Be("2026-10-28");
        pagina1.GetProperty("total").GetInt32().Should().Be(3);
        pagina1.GetProperty("totalDiaCop").GetInt64().Should().Be(3 * 366_000);
        pagina1.GetProperty("items").GetArrayLength().Should().Be(2);
        pagina2.GetProperty("items").GetArrayLength().Should().Be(1);
        pagina2.GetProperty("items")[0].GetProperty("codigo").GetString().Should().Be(hoy[0]);
        enorme.GetProperty("tamano").GetInt32().Should().Be(50);
    }

    [Fact]
    public async Task HU14459_ElDiaCambiaALaMedianocheDeColombia_NoDeUtc()
    {
        await IniciarAsync();
        _host.Reloj.Avanzar(TimeSpan.FromHours(13)); // 23:00 en Colombia del 27, ya 04:00 UTC del 28
        var codigo = await CrearAsync();
        (await Confirmar(codigo, Ana, "efectivo")).StatusCode.Should().Be(HttpStatusCode.OK);

        (await PortalHost.JsonAsync(await Enviar(HttpMethod.Get, "/cobros/hoy", Ana))).RootElement.GetProperty("total").GetInt32().Should().Be(1);
        _host.Reloj.Avanzar(TimeSpan.FromHours(2)); // 01:00 en Colombia del 28
        (await PortalHost.JsonAsync(await Enviar(HttpMethod.Get, "/cobros/hoy", Ana))).RootElement.GetProperty("total").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task HU14459_UnCobroAnulado_SaleMarcado_YNoSumaAlTotalDelDia()
    {
        await IniciarAsync();
        var a1 = await CrearAsync();
        var a2 = await CrearAsync();
        (await Confirmar(a1, Ana, "efectivo")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await Confirmar(a2, Ana, "efectivo")).StatusCode.Should().Be(HttpStatusCode.OK);
        var cobro = await CobroIdAsync(a1);

        await AnularComoAdminAsync(cobro);
        var json = (await PortalHost.JsonAsync(await Enviar(HttpMethod.Get, "/cobros/hoy", Ana))).RootElement;

        json.GetProperty("total").GetInt32().Should().Be(2);
        json.GetProperty("totalDiaCop").GetInt64().Should().Be(366_000, "el anulado no suma");
        json.GetProperty("items").EnumerateArray().Select(i => (i.GetProperty("codigo").GetString(), i.GetProperty("estado").GetString()))
            .Should().BeEquivalentTo([(a1, "anulado"), (a2, "pagado")]);
    }

    [Fact]
    public async Task HU14459_UnTramiteAnulado_NoSeCobraHastaQueLaPersonaGenereElCodigoOtraVez()
    {
        await IniciarAsync();
        var codigo = await CrearAsync();
        (await Confirmar(codigo, Ana, "efectivo")).StatusCode.Should().Be(HttpStatusCode.OK);
        var cobro = await CobroIdAsync(codigo);
        await AnularComoAdminAsync(cobro);

        // Anulado: la solicitud está en pendiente_pago pero sin cobro habilitado hasta que la persona lo genere otra vez.
        var r = await Enviar(HttpMethod.Get, $"/tramites/{codigo}", Ana);

        r.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await PortalHost.CodigoAsync(r)).Should().Be("no_habilitado");
    }

    // ── HU #14461 (lado BFF): CajaAdminService por gRPC ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task HU14461_Grpc_ListarCobros_SoloDelOrganismoPedido_ConAnulableDentroDeLaVentana()
    {
        await IniciarAsync();
        var a1 = await CrearAsync();
        var b1 = await CrearAsync(organismo: OrganismoB);
        (await Confirmar(a1, Ana, "efectivo")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await Confirmar(b1, Carla, "efectivo")).StatusCode.Should().Be(HttpStatusCode.OK);

        var r = await Cliente().ListarCobrosAsync(new ListarCobrosRequest { OrganismoId = OrganismoA.ToString() }, CabecerasServicio(), cancellationToken: TestContext.Current.CancellationToken);

        r.Total.Should().Be(1);
        r.TotalDiaCop.Should().Be(366_000);
        var item = r.Items.Single();
        (item.Codigo, item.Estado, item.Medio, item.PagadoPorNombre, item.Anulable).Should().Be((a1, "pagado", "efectivo", "Ana Caja", true));
        item.MotivoNoAnulable.Should().BeEmpty();

        _host.Reloj.Avanzar(TimeSpan.FromMinutes(15));
        var vencida = await Cliente().ListarCobrosAsync(new ListarCobrosRequest { OrganismoId = OrganismoA.ToString(), Fecha = "2026-10-27" }, CabecerasServicio(), cancellationToken: TestContext.Current.CancellationToken);
        vencida.Items.Single().Anulable.Should().BeFalse();
        vencida.Items.Single().MotivoNoAnulable.Should().Be("El trámite ya está en FLIT 2.0 y su cobro no se puede anular.");
    }

    [Fact]
    public async Task HU14461_Grpc_AnularCobro_DejaElCobroAnulado_ConMotivoYActor()
    {
        await IniciarAsync();
        var codigo = await CrearAsync();
        (await Confirmar(codigo, Ana, "efectivo")).StatusCode.Should().Be(HttpStatusCode.OK);
        var cobro = await CobroIdAsync(codigo);
        var actor = Guid.NewGuid();

        var r = await Cliente().AnularCobroAsync(
            new AnularCobroRequest { OrganismoId = OrganismoA.ToString(), CobroId = cobro.ToString(), Motivo = "El cliente se arrepintió del trámite", ActorUserId = actor.ToString(), ActorNombre = "Admin OT", RowVersion = await RowVersionAsync(cobro) },
            CabecerasServicio(), cancellationToken: TestContext.Current.CancellationToken);

        r.Estado.Should().Be("anulado");
        await using var db = _host.NuevaDb();
        var c = await db.Set<CobroSolicitud>().SingleAsync(x => x.Id == cobro, TestContext.Current.CancellationToken);
        (c.AnuladoPorId, c.AnuladoPorNombre, c.AnulacionMotivo).Should().Be((actor, "Admin OT", "El cliente se arrepintió del trámite"));
        (await db.Set<SolicitudTramite>().SingleAsync(TestContext.Current.CancellationToken)).Estado.Should().Be("pendiente_pago");
        (await db.Set<EventoCaja>().AnyAsync(e => e.Accion == "anular" && e.UsuarioId == actor, TestContext.Current.CancellationToken)).Should().BeTrue();
    }

    [Fact]
    public async Task HU14461_Grpc_AnularFueraDeVentana_EsFailedPreconditionCobroNoAnulable()
    {
        await IniciarAsync();
        var codigo = await CrearAsync();
        (await Confirmar(codigo, Ana, "efectivo")).StatusCode.Should().Be(HttpStatusCode.OK);
        var cobro = await CobroIdAsync(codigo);
        _host.Reloj.Avanzar(TimeSpan.FromMinutes(16));

        var ex = await Fallo(cobro, "Se cobró dos veces el mismo trámite");

        ex.StatusCode.Should().Be(StatusCode.FailedPrecondition);
        Flit.Platform.Sdk.Grpc.PlatformRpcErrors.Reason(ex).Should().Be("cobro_no_anulable");
        await using var db = _host.NuevaDb();
        (await db.Set<CobroSolicitud>().SingleAsync(x => x.Id == cobro, TestContext.Current.CancellationToken)).Estado.Should().Be("pagado");
    }

    [Fact]
    public async Task HU14461_Grpc_ElCobroDeOtroOrganismo_EsNotFound_YElMotivoEsObligatorio()
    {
        await IniciarAsync();
        var codigo = await CrearAsync();
        (await Confirmar(codigo, Ana, "efectivo")).StatusCode.Should().Be(HttpStatusCode.OK);
        var cobro = await CobroIdAsync(codigo);

        var ajeno = await FalloEn(OrganismoB, cobro, "Motivo suficientemente largo");
        var sinMotivo = await Fallo(cobro, "");
        var corto = await Fallo(cobro, "corto");

        ajeno.StatusCode.Should().Be(StatusCode.NotFound);
        Flit.Platform.Sdk.Grpc.PlatformRpcErrors.Reason(ajeno).Should().Be("cobro_no_encontrado");
        sinMotivo.StatusCode.Should().Be(StatusCode.InvalidArgument);
        corto.StatusCode.Should().Be(StatusCode.InvalidArgument);
    }

    [Fact]
    public async Task HU14461_Grpc_SoloSvcTramitesConElScope_UnaCajaNuncaPasa()
    {
        await IniciarAsync();
        var peticion = new ListarCobrosRequest { OrganismoId = OrganismoA.ToString() };

        async Task<StatusCode> Codigo(Metadata cabeceras)
        {
            var ex = await FluentActions.Awaiting(() => Cliente().ListarCobrosAsync(peticion, cabeceras, cancellationToken: TestContext.Current.CancellationToken).ResponseAsync).Should().ThrowAsync<RpcException>();
            return ex.Which.StatusCode;
        }

        (await Codigo(CabecerasServicio(sub: null))).Should().Be(StatusCode.Unauthenticated);
        (await Codigo(CabecerasServicio(sub: "svc-notificaciones"))).Should().Be(StatusCode.PermissionDenied);
        (await Codigo(CabecerasServicio(scope: "platform.personanatural.leer"))).Should().Be(StatusCode.PermissionDenied);
        (await Codigo(CabecerasServicio(aud: "tramites"))).Should().Be(StatusCode.PermissionDenied);
        // Un token de usuario de caja (aud=caja, sub = id del usuario) nunca pasa por los endpoints de servicio.
        (await Codigo(CabecerasServicio(sub: Ana.UsuarioId.ToString(), aud: "caja"))).Should().Be(StatusCode.Unauthenticated);
    }

    [Fact]
    public async Task HU14461_Grpc_ConLaCajaApagada_ElServicioNoEstaMapeado()
    {
        await IniciarAsync(habilitado: false);

        var ex = await FluentActions.Awaiting(() => Cliente().ListarCobrosAsync(new ListarCobrosRequest { OrganismoId = OrganismoA.ToString() }, CabecerasServicio(), cancellationToken: TestContext.Current.CancellationToken).ResponseAsync)
            .Should().ThrowAsync<RpcException>();

        ex.Which.StatusCode.Should().Be(StatusCode.Unimplemented);
    }

    // ── Revisiones de seguridad y de código de la Feature #14453 ─────────────────────────────────────────────────────

    [Fact]
    public async Task Sesion_SinAuthTime_AunqueTengaUnIatReciente_Responde401SinSesion_FailClosed()
    {
        await IniciarAsync();

        var r = await Enviar(HttpMethod.Get, "/sesion", Ana, soloIat: true);

        r.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await PortalHost.CodigoAsync(r)).Should().Be("sin_sesion");
        _resolver.Llamadas.Should().Be(0);
    }

    [Fact]
    public async Task Sesion_ElIatRenovadoPorUnRefreshNoEstiraElTope_MandaElAuthTime()
    {
        await IniciarAsync();
        var inicio = _host.Reloj.GetUtcNow().AddHours(-13).ToUnixTimeSeconds();

        // iat reciente (como el de un refresh) pero la sesión empezó hace 13 h.
        var vencida = await Enviar(HttpMethod.Get, "/sesion", Ana, soloIat: true, extra: [("x-test-claim", $"auth_time={inicio}")]);
        var vigente = await Enviar(HttpMethod.Get, "/sesion", Ana, soloIat: true, extra: [("x-test-claim", $"auth_time={_host.Reloj.GetUtcNow().AddHours(-11).ToUnixTimeSeconds()}")]);

        vencida.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        vigente.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData("no-es-numero")]
    [InlineData("0")]
    [InlineData("-5")]
    public async Task Sesion_ConUnAuthTimeInvalido_Responde401(string valor)
    {
        await IniciarAsync();

        var r = await Enviar(HttpMethod.Get, "/sesion", Ana, soloIat: true, extra: [("x-test-claim", $"auth_time={valor}")]);

        r.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Sesion_ConUnAuthTimeEnElFuturoLejano_Responde401()
    {
        await IniciarAsync();

        var r = await Enviar(HttpMethod.Get, "/sesion", Ana, soloIat: true, extra: [("x-test-claim", $"auth_time={_host.Reloj.GetUtcNow().AddHours(2).ToUnixTimeSeconds()}")]);

        r.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Confirmar_ConCodigosQueNoExisten_ComparteElTopeDeBusquedasFallidas_Responde429()
    {
        await IniciarAsync();
        for (var i = 0; i < 10; i++)
        {
            var r = await Confirmar($"PN-2026-9000{i:D2}", Ana, "efectivo");
            r.StatusCode.Should().Be(HttpStatusCode.NotFound);
            (await PortalHost.CodigoAsync(r)).Should().Be("no_encontrado");
        }

        var bloqueada = await Confirmar("PN-2026-900099", Ana, "efectivo");

        bloqueada.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        (await PortalHost.CodigoAsync(bloqueada)).Should().Be("demasiados_intentos");
        int.Parse(bloqueada.Headers.GetValues("Retry-After").Single()).Should().BeInRange(1, 300);
        // El tope es el mismo que el de GET: un trámite real tampoco se encuentra mientras dure el bloqueo.
        var existente = await CrearAsync();
        (await Enviar(HttpMethod.Get, $"/tramites/{existente}", Ana)).StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        (await Confirmar(existente, Ana, "efectivo")).StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        (await Confirmar(existente, Beto, "efectivo")).StatusCode.Should().Be(HttpStatusCode.OK, "el tope es por usuario");
    }

    [Fact]
    public async Task Confirmar_ConTramitesDeOtroOrganismo_TambienCuentaContraElTope()
    {
        await IniciarAsync();
        var ajeno = await CrearAsync(organismo: OrganismoB);
        for (var i = 0; i < 10; i++)
            (await Confirmar(ajeno, Ana, "efectivo")).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        (await Confirmar(ajeno, Ana, "efectivo")).StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task Confirmar_LosCobrosLegitimos_NoConsumenElTope()
    {
        await IniciarAsync();
        var pagado = await CrearAsync();
        (await Confirmar(pagado, Ana, "efectivo", llave: "k-ok")).StatusCode.Should().Be(HttpStatusCode.OK);

        for (var i = 0; i < 15; i++)
            (await Confirmar(pagado, Ana, "efectivo", llave: $"k-otra-{i}")).StatusCode.Should().Be(HttpStatusCode.Conflict);

        await using var db = _host.NuevaDb();
        (await db.Set<EventoCaja>().CountAsync(e => e.Accion == "busqueda_fallida", TestContext.Current.CancellationToken)).Should().Be(0, "ningún cupo queda ocupado");
    }

    [Fact]
    public async Task Tope_TreintaBusquedasEnParalelo_NoSuperanElTopeDe10_ConElBloqueoPorUsuario()
    {
        await IniciarAsync();

        var respuestas = await Task.WhenAll(Enumerable.Range(0, 30).Select(i => Enviar(HttpMethod.Get, $"/tramites/PN-2026-9{i:D5}", Ana)));

        respuestas.Count(r => r.StatusCode == HttpStatusCode.NotFound).Should().Be(10, "el tope se aplica aunque lleguen todas a la vez");
        respuestas.Count(r => r.StatusCode == HttpStatusCode.TooManyRequests).Should().Be(20);
        await using var db = _host.NuevaDb();
        (await db.Set<EventoCaja>().CountAsync(e => e.UsuarioId == Ana.UsuarioId && e.Accion == "busqueda_fallida", TestContext.Current.CancellationToken)).Should().Be(10);
    }

    [Fact]
    public async Task Tope_PorIp_VariosUsuariosDeLaMismaIpLlegaAlTopeDeLaIp_YOtraIpSigueLibre()
    {
        await IniciarAsync(ajustes: ("Caja:BusquedasFallidasMaxPorIp", "12"));
        var usuarios = Enumerable.Range(0, 5).Select(i => new Cajero(Guid.NewGuid(), $"U{i}", OrganismoA, "AB1", "Tránsito de Prueba")).ToList();

        // 12 búsquedas sin resultado desde la misma IP repartidas en 4 usuarios (3 cada uno): ninguno llega a su tope de 10.
        for (var i = 0; i < 12; i++)
        {
            var u = usuarios[i % 4];
            await _host.EnScopeAsync(sp => sp.GetRequiredService<CajaTopeBusquedas>().ReservarAsync(u, "203.0.113.9", TestContext.Current.CancellationToken));
        }

        var ex = await FluentActions.Awaiting(() => _host.EnScopeAsync(sp => sp.GetRequiredService<CajaTopeBusquedas>().ReservarAsync(usuarios[4], "203.0.113.9", TestContext.Current.CancellationToken)))
            .Should().ThrowAsync<Flit.PersonaNatural.Api.Solicitudes.ReglaDeNegocioException>();
        ex.Which.Estado.Should().Be(429);
        ex.Which.Codigo.Should().Be("demasiados_intentos");
        await _host.EnScopeAsync(sp => sp.GetRequiredService<CajaTopeBusquedas>().ReservarAsync(usuarios[4], "198.51.100.7", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Confirmar_UnaPrefacturaQueDesbordaElCuerpo_Responde413_SinLlegarAlAlmacen()
    {
        await IniciarAsync(ajustes: ("Caja:PrefacturaMaxBytes", "2048"));
        var codigo = await CrearAsync();

        var r = await Confirmar(codigo, Ana, "efectivo", prefactura: ("grande.pdf", Archivos.Pdf(3 * 1024 * 1024)));

        r.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge);
        (await PortalHost.CodigoAsync(r)).Should().Be("prefactura_invalida");
        _almacen.Archivos.Should().BeEmpty();
        await using var db = _host.NuevaDb();
        (await db.Set<CobroSolicitud>().CountAsync(c => c.Estado == "pagado", TestContext.Current.CancellationToken)).Should().Be(0);
    }

    [Fact]
    public async Task Confirmar_ConElCobroVencidoONoHabilitado_NoGuardaLaPrefactura()
    {
        await IniciarAsync();
        var vencido = await CrearAsync();
        var sinCobro = await CrearAsync(habilitado: false);
        _host.Reloj.Avanzar(TimeSpan.FromDays(1));

        var r1 = await Confirmar(vencido, Ana, "efectivo", prefactura: ("p.pdf", Archivos.Pdf(10)));
        var r2 = await Confirmar(sinCobro, Ana, "efectivo", prefactura: ("p.pdf", Archivos.Pdf(10)));

        r1.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await PortalHost.CodigoAsync(r1)).Should().Be("cobro_vencido");
        r2.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await PortalHost.CodigoAsync(r2)).Should().Be("no_habilitado");
        _almacen.Archivos.Should().BeEmpty("la validación va antes de guardar el archivo");
        await using var db = _host.NuevaDb();
        (await db.Set<Adjunto>().CountAsync(TestContext.Current.CancellationToken)).Should().Be(0);
    }

    [Fact]
    public async Task Retencion_BarreLasPrefacturasSinCobro_YRespetaLasLigadasYLasRecientes()
    {
        await IniciarAsync();
        var conCobro = await CrearAsync();
        (await Confirmar(conCobro, Ana, "efectivo", prefactura: ("p.pdf", Archivos.Pdf(10)))).StatusCode.Should().Be(HttpStatusCode.OK);
        var otro = await CrearAsync();
        Guid solicitud;
        await using (var db = _host.NuevaDb())
            solicitud = await db.Set<SolicitudTramite>().Where(s => s.CodigoPublico == otro).Select(s => s.Id).SingleAsync(TestContext.Current.CancellationToken);
        var vieja = await _host.EnScopeAsync(sp => sp.GetRequiredService<AdjuntosService>().GuardarPrefacturaAsync(solicitud, "vieja.pdf", Archivos.Pdf(20), 5 * 1024 * 1024, TestContext.Current.CancellationToken));
        _host.Reloj.Avanzar(TimeSpan.FromHours(25));
        var reciente = await _host.EnScopeAsync(sp => sp.GetRequiredService<AdjuntosService>().GuardarPrefacturaAsync(solicitud, "nueva.pdf", Archivos.Pdf(30), 5 * 1024 * 1024, TestContext.Current.CancellationToken));

        var barridas = await _host.EnScopeAsync(sp => sp.GetRequiredService<Flit.PersonaNatural.Api.Pii.RetencionService>().PurgarPrefacturasHuerfanasAsync(TestContext.Current.CancellationToken));

        barridas.Should().Be(1);
        await using var db2 = _host.NuevaDb();
        var ids = await db2.Set<Adjunto>().Where(a => a.Tipo == "prefactura_caja").Select(a => a.Id).ToListAsync(TestContext.Current.CancellationToken);
        ids.Should().HaveCount(2).And.NotContain(vieja).And.Contain(reciente);
        _almacen.Eliminados.Should().HaveCount(1);
        (await _host.EnScopeAsync(sp => sp.GetRequiredService<Flit.PersonaNatural.Api.Pii.RetencionService>().PurgarPrefacturasHuerfanasAsync(TestContext.Current.CancellationToken))).Should().Be(0, "es idempotente");
    }

    [Fact]
    public async Task Idempotencia_LaMismaClaveConOtroMedioOOtroTotalVisto_Responde409_YLaMismaPeticionDevuelveElResultado()
    {
        await IniciarAsync();
        var codigo = await CrearAsync();
        var original = await Confirmar(codigo, Ana, "efectivo", llave: "k-igual");
        original.StatusCode.Should().Be(HttpStatusCode.OK);

        var repetida = await Confirmar(codigo, Ana, "efectivo", llave: "k-igual");
        var otroMedio = await Confirmar(codigo, Ana, "datafono", llave: "k-igual");
        var otroTotal = await Confirmar(codigo, Ana, "efectivo", totalVisto: 100_000, llave: "k-igual");
        var sinTotal = await Confirmar(codigo, Ana, "efectivo", totalVisto: null, llave: "k-igual");

        repetida.StatusCode.Should().Be(HttpStatusCode.OK);
        (await PortalHost.JsonAsync(repetida)).RootElement.GetProperty("valorCop").GetInt64().Should().Be(366_000);
        sinTotal.StatusCode.Should().Be(HttpStatusCode.OK, "sin total visto no hay nada que contradecir");
        foreach (var r in new[] { otroMedio, otroTotal })
        {
            r.StatusCode.Should().Be(HttpStatusCode.Conflict);
            (await PortalHost.CodigoAsync(r)).Should().Be("idempotency_key_reutilizada");
        }

        await using var db = _host.NuevaDb();
        (await db.Set<CobroSolicitud>().CountAsync(c => c.Estado == "pagado", TestContext.Current.CancellationToken)).Should().Be(1);
    }

    [Fact]
    public async Task HU14461_Grpc_AnularSinRowVersion_EsInvalidArgument_YNoCambiaNada()
    {
        await IniciarAsync();
        var codigo = await CrearAsync();
        (await Confirmar(codigo, Ana, "efectivo")).StatusCode.Should().Be(HttpStatusCode.OK);
        var cobro = await CobroIdAsync(codigo);

        var ex = await FalloEn(OrganismoA, cobro, "Se cobró dos veces el mismo trámite", rowVersion: 0);

        ex.StatusCode.Should().Be(StatusCode.InvalidArgument);
        await using var db = _host.NuevaDb();
        (await db.Set<CobroSolicitud>().SingleAsync(x => x.Id == cobro, TestContext.Current.CancellationToken)).Estado.Should().Be("pagado");
    }

    private async Task<long> RowVersionAsync(Guid cobro)
    {
        await using var db = _host.NuevaDb();
        return await db.Set<CobroSolicitud>().Where(c => c.Id == cobro).Select(c => c.RowVersion).SingleAsync(TestContext.Current.CancellationToken);
    }

    private async Task AnularComoAdminAsync(Guid cobro)
    {
        var version = await RowVersionAsync(cobro);
        await _host.EnScopeAsync(sp => sp.GetRequiredService<CajaAnulacionService>().AnularAsync(OrganismoA, cobro, "Cobro duplicado por el cajero", Guid.NewGuid(), "Admin OT", version, TestContext.Current.CancellationToken));
    }

    // ── Soporte ──────────────────────────────────────────────────────────────────────────────────────────────────────

    private static Metadata CabecerasServicio(string? sub = "svc-tramites", string? aud = "personanatural", string? scope = "platform.personanatural.caja")
    {
        var m = new Metadata { { "x-flit-tenant-id", PlatformTenants.PersonaNatural.ToString() } };
        if (sub is not null)
            m.Add("x-test-sub", sub);
        if (aud is not null)
            m.Add("x-test-aud", aud);
        if (scope is not null)
            m.Add("x-test-scope", scope);
        return m;
    }

    private CajaAdminService.CajaAdminServiceClient Cliente() =>
        new(GrpcChannel.ForAddress("http://localhost:5995", new GrpcChannelOptions { HttpHandler = _host.App.GetTestServer().CreateHandler() }));

    private Task<RpcException> Fallo(Guid cobro, string motivo) => FalloEn(OrganismoA, cobro, motivo);

    private async Task<RpcException> FalloEn(Guid organismo, Guid cobro, string motivo, long rowVersion = 1)
    {
        var llamada = () => Cliente().AnularCobroAsync(
            new AnularCobroRequest { OrganismoId = organismo.ToString(), CobroId = cobro.ToString(), Motivo = motivo, ActorUserId = Guid.NewGuid().ToString(), ActorNombre = "Admin OT", RowVersion = rowVersion },
            CabecerasServicio(), cancellationToken: TestContext.Current.CancellationToken).ResponseAsync;
        return (await FluentActions.Awaiting(llamada).Should().ThrowAsync<RpcException>()).Which;
    }

    private async Task<Guid> CobroIdAsync(string codigo)
    {
        await using var db = _host.NuevaDb();
        return await db.Set<CobroSolicitud>().Where(c => c.Codigo == codigo && c.Estado == "pagado").Select(c => c.Id).SingleAsync(TestContext.Current.CancellationToken);
    }

    private Task<HttpResponseMessage> Confirmar(
        string codigo, Cajero usuario, string? medio, string? detalle = null, long? totalVisto = 366_000, string? llave = null, (string Nombre, byte[] Bytes)? prefactura = null)
    {
        var form = Formulario(medio, detalle, totalVisto, prefactura);
        return Enviar(HttpMethod.Post, $"/tramites/{codigo}/confirmar", usuario, contenido: form, llave: llave ?? $"k-{Guid.NewGuid():N}");
    }

    private static MultipartFormDataContent Formulario(string? medio, string? detalle = null, long? totalVisto = 366_000, (string Nombre, byte[] Bytes)? prefactura = null)
    {
        var form = new MultipartFormDataContent();
        if (medio is not null)
            form.Add(new StringContent(medio), "medio");
        if (detalle is not null)
            form.Add(new StringContent(detalle), "medioDetalle");
        if (totalVisto is { } t)
            form.Add(new StringContent(t.ToString(System.Globalization.CultureInfo.InvariantCulture)), "totalVistoCop");
        if (prefactura is { } p)
        {
            var archivo = new ByteArrayContent(p.Bytes);
            archivo.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            form.Add(archivo, "prefactura", p.Nombre);
        }

        return form;
    }

    /// <summary>Una petición de caja con el principal de prueba (producto, rol, audiencia, empresa y <c>iat</c> de hace un minuto).</summary>
    private async Task<HttpResponseMessage> Enviar(
        HttpMethod metodo, string ruta, Cajero usuario, HttpContent? contenido = null, string? llave = null, string producto = "caja", string rol = "ot_caja",
        string audiencia = "caja", string? sub = null, DateTimeOffset? iat = null, (string Nombre, string Valor)[]? extra = null, bool soloIat = false)
    {
        using var peticion = new HttpRequestMessage(metodo, Ruta + ruta) { Content = contenido };
        peticion.Headers.Add("x-test-sub", sub ?? usuario.UsuarioId.ToString());
        peticion.Headers.Add("x-test-aud", audiencia);
        peticion.Headers.Add("x-test-claim", $"product={producto}");
        peticion.Headers.Add("x-test-claim", $"role={rol}");
        peticion.Headers.Add("x-test-claim", $"tenant_id={Empresa}");
        // Inicio de sesión: auth_time (el BFF NO acepta iat, que se renueva en cada refresh). soloIat simula un token que solo trae iat.
        var inicio = (iat ?? _host.Reloj.GetUtcNow().AddMinutes(-1)).ToUnixTimeSeconds();
        peticion.Headers.Add("x-test-claim", $"iat={inicio}");
        if (!soloIat)
            peticion.Headers.Add("x-test-claim", $"auth_time={inicio}");
        if (llave is not null && llave.Length > 0)
            peticion.Headers.Add("Idempotency-Key", llave);
        foreach (var (nombre, valor) in extra ?? [])
            peticion.Headers.Add(nombre, valor);
        return await _host.Http.SendAsync(peticion, TestContext.Current.CancellationToken);
    }

    /// <summary>Una solicitud del punto lista para cobrar en caja (o en otro estado) con su cobro habilitado; devuelve su ID <c>PN-AAAA-NNNNNN</c>.</summary>
    private async Task<string> CrearAsync(Guid? organismo = null, string estado = "pendiente_pago", bool habilitado = true)
    {
        var id = Guid.CreateVersion7();
        var ahora = _host.Reloj.GetUtcNow();
        var codigo = $"PN-2026-{++_secuencia + 100:D6}";
        var org = organismo ?? OrganismoA;
        await using var db = _host.NuevaDb();
        db.Add(new SolicitudTramite
        {
            Id = id,
            TenantId = PlatformTenants.PersonaNatural,
            CodigoPublico = codigo,
            TipoTramiteCodigo = "TRASPASO_STANDARD",
            DefinicionVersion = 1,
            DefinicionSnapshot = DefinicionesService.Serializar(DefinicionesDePrueba.Traspaso()),
            OrganismoId = org,
            OrganismoNombre = org == OrganismoA ? "Tránsito de Prueba" : "Otro Tránsito",
            Placa = "ABC123",
            Estado = estado,
            CedulaSolicitanteHash = new byte[32],
            FieldValues = "{\"km\":\"85000\"}",
            Datos = "{\"canal\":\"pn_punto\",\"comercial\":{\"valor\":45000000}}",
            IdempotencyKey = $"k-caja-{_secuencia}",
            CorrelationId = Guid.CreateVersion7(),
            CreatedAt = ahora,
            UpdatedAt = ahora,
        });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        if (habilitado)
        {
            db.Add(new CobroSolicitud
            {
                Id = Guid.CreateVersion7(), SolicitudId = id, OrganismoId = org, Codigo = codigo, Estado = "habilitado", HabilitadoEn = ahora,
                VenceEn = Flit.PersonaNatural.Api.Pagos.VigenciaDelCobro.FinDelDia(ahora), CreatedAt = ahora, UpdatedAt = ahora,
            });
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        return codigo;
    }
}
