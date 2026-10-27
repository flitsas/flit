using System.Net;
using System.Text.Json;
using Flit.PersonaNatural.Api.Definiciones;
using Flit.PersonaNatural.Api.Persistence;
using Flit.PersonaNatural.Api.Solicitudes;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Flit.PersonaNatural.Tests;

/// <summary>Fachada de requisitos de prueba: devuelve lo que se le configure y recuerda con qué se la llamó.</summary>
internal sealed class FachadaRequisitosFalsa : IFachadaRequisitos
{
    public DiagnosticoDto Respuesta { get; set; } = new(true, [new RequisitoDto("soat", "SOAT vigente", "cumple")]);
    public (string Tipo, string? Placa, string? Vin, string? Doc)? Ultima;

    public Task<DiagnosticoDto> EvaluarAsync(string tipoCodigo, string organismoId, string? placa, string? vin, string? tipoDocumentoPropietario, string? documentoPropietario, CancellationToken ct)
    {
        Ultima = (tipoCodigo, placa, vin, documentoPropietario);
        return Task.FromResult(Respuesta);
    }
}

/// <summary>
/// HU #14047 (Feature D, Épica #14021) — el borrador del trámite sobre el host real y una base migrada: se abre con la
/// definición del tipo congelada y se guarda por pasos, validando contra esa definición. Base efímera propia; sin motor se
/// omite en local y falla en CI.
/// </summary>
public sealed class SolicitudesTests : IAsyncLifetime
{
    private const string Base = "/api/v1/persona-natural";
    private const string Vendedor = "7001234567";
    private const string Comprador = "7009876543";
    private const string CorreoVendedor = "vendedor.secreto@example.org";

    private readonly FachadaRequisitosFalsa _requisitos = new();
    private PortalHost _host = null!;
    private bool _iniciado;

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        if (_iniciado)
            await _host.DisposeAsync();
    }

    private async Task IniciarAsync(params (string Clave, string Valor)[] ajustes)
    {
        _host = await PortalHost.IniciarAsync("pnsol", (servicios, _) => servicios.AddSingleton<IFachadaRequisitos>(_requisitos), ajustes);
        _iniciado = true;
        _host.Definiciones.PorCodigo["TRASPASO_STANDARD"] = DefinicionesDePrueba.Traspaso();
        _host.Definiciones.PorCodigo["CAMBIO_COLOR"] = DefinicionesDePrueba.UnTitular();
        _host.Definiciones.PorCodigo["TIPO_PRENDA"] = DefinicionesDePrueba.ConPrenda();
    }

    // ── Abrir ────────────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AlAbrir_CreaElBorradorConLaDefinicionCongelada_ElConsentimientoYLaSesion()
    {
        await IniciarAsync();

        var r = await Abrir();

        r.StatusCode.Should().Be(HttpStatusCode.Created);
        var json = (await PortalHost.JsonAsync(r)).RootElement;
        json.GetProperty("estado").GetString().Should().Be("borrador");
        json.GetProperty("codigoPublico").GetString().Should().MatchRegex(@"^PN-2026-\d{6}$");
        json.GetProperty("definicionVersion").GetInt32().Should().Be(1);
        json.GetProperty("token").GetString().Should().NotBeNullOrEmpty();
        await using var db = _host.NuevaDb();
        var fila = await db.Set<SolicitudTramite>().SingleAsync(TestContext.Current.CancellationToken);
        fila.TipoTramiteCodigo.Should().Be("TRASPASO_STANDARD");
        fila.DefinicionSnapshot.Should().Contain("\"huella\": \"h1\"").And.Contain("comprador");
        fila.Placa.Should().Be("ABC123");
        fila.CedulaSolicitanteHash.Should().HaveCount(32);
        fila.OrganismoNombre.Should().Be("Tránsito de Prueba");
        (await db.Set<Consentimiento>().SingleAsync(TestContext.Current.CancellationToken)).TextoVersion.Should().Be("v1");
        (await db.Set<HistorialEstado>().SingleAsync(TestContext.Current.CancellationToken)).Hasta.Should().Be("borrador");
    }

    [Fact]
    public async Task LosConsecutivosDelRadicado_NoSeRepiten()
    {
        await IniciarAsync();

        var a = await Abrir();
        var b = await Abrir();

        (await Campo(a, "codigoPublico")).Should().NotBe(await Campo(b, "codigoPublico"));
    }

    [Fact]
    public async Task AlAbrir_ExigeCaptcha_ConsentimientoYDatosValidos()
    {
        await IniciarAsync();

        var sinCaptcha = await _host.EnviarAsync(HttpMethod.Post, $"{Base}/sesiones", Cuerpo(), captcha: false);
        var sinConsentimiento = await _host.EnviarAsync(HttpMethod.Post, $"{Base}/sesiones", Cuerpo(consentimiento: false), captcha: true);
        var sinPlaca = await _host.EnviarAsync(HttpMethod.Post, $"{Base}/sesiones", Cuerpo(placa: null), captcha: true);
        var cedulaMala = await _host.EnviarAsync(HttpMethod.Post, $"{Base}/sesiones", Cuerpo(cedula: "x"), captcha: true);

        sinCaptcha.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await PortalHost.CodigoAsync(sinCaptcha)).Should().Be("captcha_required");
        (await PortalHost.CodigoAsync(sinConsentimiento)).Should().Be("consentimiento_requerido");
        sinPlaca.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await Errores(sinPlaca)).Should().ContainKey("placa");
        (await Errores(cedulaMala)).Should().ContainKey("cedula");
        await using var db = _host.NuevaDb();
        (await db.Set<SolicitudTramite>().CountAsync(TestContext.Current.CancellationToken)).Should().Be(0);
    }

    [Fact]
    public async Task AlAbrir_UnTipoNoDisponibleOUnOrganismoNoHabilitado_NoCreaNada()
    {
        await IniciarAsync();

        var desconocido = await _host.EnviarAsync(HttpMethod.Post, $"{Base}/sesiones", Cuerpo(tipo: "NO_EXISTE"), captcha: true);
        var otroOrganismo = await _host.EnviarAsync(HttpMethod.Post, $"{Base}/sesiones", Cuerpo(organismo: "0199b7e0-0000-7000-8000-0000000000ff"), captcha: true);

        desconocido.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await PortalHost.CodigoAsync(desconocido)).Should().Be("tipo_no_disponible");
        (await PortalHost.CodigoAsync(otroOrganismo)).Should().Be("organismo_no_habilitado");
        await using var db = _host.NuevaDb();
        (await db.Set<SolicitudTramite>().CountAsync(TestContext.Current.CancellationToken)).Should().Be(0);
    }

    [Fact]
    public async Task ElModoDeEntradaDelTipoDecideSiSePidePlacaOVin()
    {
        await IniciarAsync();
        _host.Definiciones.PorCodigo["POR_VIN"] = DefinicionesDePrueba.UnTitular("POR_VIN") with { Flags = DefinicionesDePrueba.UnTitular().Flags with { EntryMode = "VIN" } };

        var conPlaca = await _host.EnviarAsync(HttpMethod.Post, $"{Base}/sesiones", Cuerpo(tipo: "POR_VIN"), captcha: true);
        var conVin = await _host.EnviarAsync(HttpMethod.Post, $"{Base}/sesiones", Cuerpo(tipo: "POR_VIN", placa: null, vin: "9BWZZZ377VT004251"), captcha: true);

        (await Errores(conPlaca)).Should().ContainKey("vin");
        conVin.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    // ── Leer ─────────────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ElBorradorRecienAbierto_TraLosPasosYLosPendientesDeSuDefinicion()
    {
        await IniciarAsync();
        var (id, token) = await AbrirSesion();

        var r = await _host.EnviarAsync(HttpMethod.Get, $"{Base}/solicitudes/{id}", token: token);

        r.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = (await PortalHost.JsonAsync(r)).RootElement;
        json.GetProperty("completo").GetBoolean().Should().BeFalse();
        var pendientes = json.GetProperty("pendientes").EnumerateArray().Select(p => (p.GetProperty("codigo").GetString(), p.TryGetProperty("referencia", out var x) ? x.GetString() : null)).ToList();
        pendientes.Should().Contain(("campo_requerido", "km")).And.Contain(("actor_requerido", "vendedor")).And.Contain(("actor_requerido", "comprador"))
            .And.Contain(("comercial_requerido", "valorComercial")).And.Contain(("documento_requerido", "compraventa")).And.Contain(("comprobante_pago_requerido", null));
        pendientes.Should().NotContain(p => p.Item2 == "poder_tramitador", "un documento condicional no bloquea: la condición se evalúa en servidor");
        json.GetProperty("pasos").EnumerateArray().Single(p => p.GetProperty("clave").GetString() == "datos").GetProperty("completo").GetBoolean().Should().BeFalse();
        json.GetProperty("pasos").EnumerateArray().Single(p => p.GetProperty("clave").GetString() == "consulta").GetProperty("completo").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task SinSesionOConLaDeOtraSolicitud_NoSeLee()
    {
        await IniciarAsync();
        var (id, _) = await AbrirSesion();
        var (_, tokenAjeno) = await AbrirSesion();

        (await _host.EnviarAsync(HttpMethod.Get, $"{Base}/solicitudes/{id}")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await _host.EnviarAsync(HttpMethod.Get, $"{Base}/solicitudes/{id}", token: tokenAjeno)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ── Actores ──────────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task LosActores_SeGuardanCifrados_YSeRehidratanEnElBorrador()
    {
        await IniciarAsync();
        var (id, token) = await AbrirSesion();

        var r = await Patch(id, token, new { actores = new[] { Actor("vendedor", Vendedor, CorreoVendedor), Actor("comprador", Comprador, "comprador@example.org") } });

        r.StatusCode.Should().Be(HttpStatusCode.OK);
        var rehidratado = (await PortalHost.JsonAsync(await _host.EnviarAsync(HttpMethod.Get, $"{Base}/solicitudes/{id}", token: token))).RootElement;
        var vendedor = rehidratado.GetProperty("actores").EnumerateArray().Single(a => a.GetProperty("rol").GetString() == "vendedor");
        vendedor.GetProperty("numeroDocumento").GetString().Should().Be(Vendedor);
        vendedor.GetProperty("correo").GetString().Should().Be(CorreoVendedor);
        vendedor.GetProperty("validacionEstado").GetString().Should().Be("sin_solicitar");
        rehidratado.GetProperty("pendientes").EnumerateArray().Select(p => p.GetProperty("codigo").GetString()).Should().NotContain("actor_requerido");

        await using var db = _host.NuevaDb();
        var fila = await db.Set<PersonaSolicitante>().FirstAsync(p => p.Parte == "vendedor", TestContext.Current.CancellationToken);
        System.Text.Encoding.UTF8.GetString(fila.DocumentoCifrado).Should().NotContain(Vendedor);
        System.Text.Encoding.UTF8.GetString(fila.CorreoCifrado!).Should().NotContain("secreto");
        lock (_host.Logs.Mensajes)
            _host.Logs.Mensajes.Should().NotContain(m => m.Contains(Vendedor) || m.Contains("secreto"), "ningún dato personal llega a los logs");
    }

    [Fact]
    public async Task UnActorInvalido_Responde422ConUnErrorPorCampo_YNoGuardaNada()
    {
        await IniciarAsync();
        var (id, token) = await AbrirSesion();

        var r = await Patch(id, token, new
        {
            actores = new object[]
            {
                new { rol = "comprador", tipoDocumento = "CC", numeroDocumento = "12ab", nombre = "", correo = "no-es-correo", celular = "123" },
                new { rol = "inventado", tipoDocumento = "CC", numeroDocumento = "123456", nombre = "X", correo = "x@example.org" },
            },
        });

        r.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await PortalHost.CodigoAsync(r)).Should().Be("datos_invalidos");
        var errores = await Errores(r);
        errores.Keys.Should().Contain(["actores[0].numeroDocumento", "actores[0].nombre", "actores[0].correo", "actores[0].celular", "actores[1].rol"]);
        await using var db = _host.NuevaDb();
        (await db.Set<PersonaSolicitante>().CountAsync(TestContext.Current.CancellationToken)).Should().Be(0);
    }

    [Fact]
    public async Task LasCantidadesPorRol_SalenDeLaDefinicion_YUnaPersonaNoPuedeSerDosPartes()
    {
        await IniciarAsync();
        var (id, token) = await AbrirSesion();

        var dosVendedores = await Patch(id, token, new { actores = new[] { Actor("vendedor", "7000000001"), Actor("vendedor", "7000000002") } });
        var cincoCompradores = await Patch(id, token, new { actores = Enumerable.Range(1, 5).Select(i => Actor("comprador", $"70000001{i:D2}")).ToArray() });
        await Patch(id, token, new { actores = new[] { Actor("vendedor", Vendedor) } });
        var mismaPersona = await Patch(id, token, new { actores = new[] { Actor("comprador", Vendedor) } });
        var cuatroCompradores = await Patch(id, token, new { actores = Enumerable.Range(1, 4).Select(i => Actor("comprador", $"70000002{i:D2}")).ToArray() });

        (await Errores(dosVendedores)).Keys.Should().Contain("actores[1].ordinal");
        (await Errores(cincoCompradores)).Keys.Should().Contain("actores[4].ordinal");
        (await Errores(mismaPersona)).Keys.Should().Contain("actores[0].numeroDocumento");
        cuatroCompradores.StatusCode.Should().Be(HttpStatusCode.OK, "el comprador admite hasta 4 copropietarios");
    }

    [Fact]
    public async Task UnRolReemplazaAlAnterior_YCambiarElDocumentoBorraSuValidacionDeIdentidad()
    {
        await IniciarAsync();
        var (id, token) = await AbrirSesion();
        await Patch(id, token, new { actores = new[] { Actor("comprador", Comprador), Actor("comprador", "7000000099") } });
        await using (var db = _host.NuevaDb())
        {
            await db.Set<PersonaSolicitante>().Where(p => p.Parte == "comprador").ExecuteUpdateAsync(
                set => set.SetProperty(p => p.ValidacionEstado, "aprobada").SetProperty(p => p.ValidacionId, Guid.NewGuid()), TestContext.Current.CancellationToken);
        }

        await Patch(id, token, new { actores = new[] { Actor("comprador", Comprador) } });
        var mismaPersona = await EstadoValidacion(id, 1);
        await Patch(id, token, new { actores = new[] { Actor("comprador", "7000000555") } });

        await using var db2 = _host.NuevaDb();
        (await db2.Set<PersonaSolicitante>().CountAsync(p => p.Parte == "comprador", TestContext.Current.CancellationToken)).Should().Be(1, "el segundo copropietario se retiró");
        mismaPersona.Should().Be("aprobada", "la misma persona conserva su validación");
        (await EstadoValidacion(id, 1)).Should().Be("sin_solicitar", "otro documento en la misma posición es otra persona");
    }

    // ── Campos ───────────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task LosCampos_SeValidanContraLaDefinicion_ClaveTipoYOpciones()
    {
        await IniciarAsync();
        var (id, token) = await AbrirSesion();

        var r = await Put(id, token, "campos", new { valores = new Dictionary<string, object?> { ["km"] = "mucho", ["uso"] = "oficial", ["fecha_venta"] = "27/10/2026", ["acepta"] = "tal vez", ["inventado"] = "x" } });

        r.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await Errores(r)).Keys.Should().BeEquivalentTo("valores.km", "valores.uso", "valores.fecha_venta", "valores.acepta", "valores.inventado");
    }

    [Fact]
    public async Task LosCampos_SeMezclan_UnNullBorraUnOpcional_YUnObligatorioNoSePuedeVaciar()
    {
        await IniciarAsync();
        var (id, token) = await AbrirSesion();
        await Put(id, token, "campos", new { valores = new Dictionary<string, object?> { ["km"] = 85000, ["uso"] = "particular", ["acepta"] = true } });

        var r = await Put(id, token, "campos", new { valores = new Dictionary<string, object?> { ["uso"] = null, ["fecha_venta"] = "2026-10-27" } });
        var vaciar = await Put(id, token, "campos", new { valores = new Dictionary<string, object?> { ["km"] = "" } });

        r.StatusCode.Should().Be(HttpStatusCode.OK);
        var valores = (await PortalHost.JsonAsync(r)).RootElement.GetProperty("fieldValues");
        valores.GetProperty("km").GetString().Should().Be("85000");
        valores.GetProperty("acepta").GetString().Should().Be("true");
        valores.GetProperty("fecha_venta").GetString().Should().Be("2026-10-27");
        valores.TryGetProperty("uso", out _).Should().BeFalse();
        (await Errores(vaciar)).Keys.Should().Contain("valores.km");
    }

    // ── Comercial, prenda y requisitos ───────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ElValorComercial_DebeSerMayorQueCero_YSoloSiElTipoLoPide()
    {
        await IniciarAsync();
        var (id, token) = await AbrirSesion();
        var (idColor, tokenColor) = await AbrirSesion("CAMBIO_COLOR");

        var cero = await Put(id, token, "comercial", new { valorComercial = 0 });
        var ok = await Put(id, token, "comercial", new { valorComercial = 45_000_000 });
        var noAplica = await Put(idColor, tokenColor, "comercial", new { valorComercial = 1000 });

        (await Errores(cero)).Keys.Should().Contain("valorComercial");
        ok.StatusCode.Should().Be(HttpStatusCode.OK);
        (await PortalHost.JsonAsync(ok)).RootElement.GetProperty("datos").GetProperty("comercial").GetProperty("valor").GetDecimal().Should().Be(45_000_000);
        (await Errores(noAplica)).Keys.Should().Contain("valorComercial");
    }

    [Fact]
    public async Task UnTipoQueExigePrenda_PideAcreedorYNit()
    {
        await IniciarAsync();
        var (id, token) = await AbrirSesion("TIPO_PRENDA");

        var sin = await Put(id, token, "comercial", new { prenda = new { tienePrenda = true } });
        var sinBloque = await Put(id, token, "comercial", new { });
        var ok = await Put(id, token, "comercial", new { prenda = new { tienePrenda = true, acreedor = "Banco de Prueba", nitAcreedor = "800.123.456-7" } });

        (await Errores(sin)).Keys.Should().Contain(["prenda.acreedor", "prenda.nitAcreedor"]);
        (await Errores(sinBloque)).Keys.Should().Contain("prenda");
        ok.StatusCode.Should().Be(HttpStatusCode.OK);
        var prenda = (await PortalHost.JsonAsync(ok)).RootElement.GetProperty("datos").GetProperty("prenda");
        prenda.GetProperty("nitAcreedor").GetString().Should().Be("8001234567");
    }

    [Fact]
    public async Task LasTransformaciones_SoloSiElTipoLasPermite_YSoloLasQueElPortalCotiza()
    {
        await IniciarAsync();
        var (id, token) = await AbrirSesion();
        var (idColor, tokenColor) = await AbrirSesion("CAMBIO_COLOR");

        var ok = await Put(id, token, "requisitos", new { transformaciones = new List<string> { "cambio_color" }, observaciones = "  Sin novedades  " });
        var invalida = await Put(id, token, "requisitos", new { transformaciones = new List<string> { "BLINDAJE" } });
        var noPermite = await Put(idColor, tokenColor, "requisitos", new { transformaciones = new List<string> { "CAMBIO_COLOR" } });

        ok.StatusCode.Should().Be(HttpStatusCode.OK);
        var req = (await PortalHost.JsonAsync(ok)).RootElement.GetProperty("datos").GetProperty("requisitos");
        req.GetProperty("transformaciones")[0].GetString().Should().Be("CAMBIO_COLOR");
        req.GetProperty("observaciones").GetString().Should().Be("Sin novedades");
        (await Errores(invalida)).Keys.Should().Contain("transformaciones");
        (await Errores(noPermite)).Keys.Should().Contain("transformaciones");
    }

    // ── Concurrencia, estado y definición ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task UnaVersionVieja_Responde409ConLaVigente_YUnaVigenteAvanzaLaVersion()
    {
        await IniciarAsync();
        var (id, token) = await AbrirSesion();
        var v0 = await Version(id, token);

        var primera = await Put(id, token, "campos", new { version = v0, valores = new Dictionary<string, object?> { ["km"] = 1 } });
        var vieja = await Put(id, token, "campos", new { version = v0, valores = new Dictionary<string, object?> { ["km"] = 2 } });

        primera.StatusCode.Should().Be(HttpStatusCode.OK);
        (await PortalHost.JsonAsync(primera)).RootElement.GetProperty("version").GetInt64().Should().Be(v0 + 1);
        vieja.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await PortalHost.CodigoAsync(vieja)).Should().Be("conflicto_version");
        (await PortalHost.JsonAsync(vieja)).RootElement.GetProperty("version").GetInt64().Should().Be(v0 + 1);
        (await Valores(id, token)).GetProperty("km").GetString().Should().Be("1", "la escritura vieja no pisó nada");
    }

    [Fact]
    public async Task UnaSolicitudEnCola_NoAdmiteCambiosNiCancelacion()
    {
        await IniciarAsync();
        var (id, token) = await AbrirSesion();
        await using (var db = _host.NuevaDb())
            await db.Set<SolicitudTramite>().ExecuteUpdateAsync(set => set.SetProperty(s => s.Estado, EstadoSolicitud.EnCola), TestContext.Current.CancellationToken);

        var editar = await Put(id, token, "campos", new { valores = new Dictionary<string, object?> { ["km"] = 1 } });
        var cancelar = await _host.EnviarAsync(HttpMethod.Delete, $"{Base}/solicitudes/{id}", token: token);

        editar.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await PortalHost.CodigoAsync(editar)).Should().Be("estado_no_permite_cambio");
        cancelar.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task SiElTipoDejoDeEstarDisponible_NoSeGuardaNada()
    {
        await IniciarAsync(("Cotizaciones:CacheCatalogoSegundos", "0"));
        var (id, token) = await AbrirSesion();
        _host.Definiciones.PorCodigo.Remove("TRASPASO_STANDARD");

        var r = await Put(id, token, "campos", new { valores = new Dictionary<string, object?> { ["km"] = 1 } });

        r.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await PortalHost.CodigoAsync(r)).Should().Be("tipo_no_disponible");
        (await Valores(id, token)).EnumerateObject().Should().BeEmpty();
    }

    [Fact]
    public async Task SiLaDefinicionCambio_AlRetomarSeConservaLoEscrito_SeDescartaLoQueYaNoExiste_YLoNuevoQuedaPendiente()
    {
        await IniciarAsync(("Cotizaciones:CacheCatalogoSegundos", "0"));
        var (id, token) = await AbrirSesion();
        await Put(id, token, "campos", new { valores = new Dictionary<string, object?> { ["km"] = 85000, ["uso"] = "publico" } });
        var nueva = DefinicionesDePrueba.Traspaso(version: 2, huella: "h2");
        _host.Definiciones.PorCodigo["TRASPASO_STANDARD"] = nueva with
        {
            Pasos = [.. nueva.Pasos.Where(p => p.Clave != "datos"), new PasoDto("datos", "Datos", ["generic_form"],
            [
                new CampoDto("km", "Kilometraje", "number", true, [], "{}"),
                new CampoDto("estado_llantas", "Estado de llantas", "text", true, [], "{}"),
            ])],
        };

        var r = await _host.EnviarAsync(HttpMethod.Get, $"{Base}/solicitudes/{id}", token: token);

        var json = (await PortalHost.JsonAsync(r)).RootElement;
        json.GetProperty("definicionVersion").GetInt32().Should().Be(2);
        json.GetProperty("definicionHuella").GetString().Should().Be("h2");
        json.GetProperty("fieldValues").GetProperty("km").GetString().Should().Be("85000");
        json.GetProperty("fieldValues").TryGetProperty("uso", out _).Should().BeFalse();
        var cambio = json.GetProperty("definicionCambio");
        cambio.GetProperty("camposDescartados").EnumerateArray().Select(x => x.GetString()).Should().Equal("uso");
        cambio.GetProperty("camposNuevos").EnumerateArray().Select(x => x.GetString()).Should().Contain("estado_llantas");
        json.GetProperty("pendientes").EnumerateArray().Select(p => p.TryGetProperty("referencia", out var x) ? x.GetString() : null).Should().Contain("estado_llantas").And.NotContain("km");
        (await _host.EnviarAsync(HttpMethod.Get, $"{Base}/solicitudes/{id}", token: token)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await PortalHost.JsonAsync(await _host.EnviarAsync(HttpMethod.Get, $"{Base}/solicitudes/{id}", token: token))).RootElement.TryGetProperty("definicionCambio", out var otra)
            .Should().BeTrue();
        otra.ValueKind.Should().Be(JsonValueKind.Null, "ya se reconcilió: la segunda lectura no vuelve a avisar");
    }

    // ── Diagnóstico y cancelación ────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ElDiagnostico_UsaLaPlacaDeLaSolicitud_YSoloGuardaEtiquetaYEstado()
    {
        await IniciarAsync();
        var (id, token) = await AbrirSesion();

        var r = await _host.EnviarAsync(HttpMethod.Post, $"{Base}/solicitudes/{id}/diagnostico", new { propietario = new { tipoDocumento = "cc", numeroDocumento = "1.234.567" } }, token);

        r.StatusCode.Should().Be(HttpStatusCode.OK);
        (await PortalHost.JsonAsync(r)).RootElement.GetProperty("requisitos")[0].GetProperty("estado").GetString().Should().Be("cumple");
        _requisitos.Ultima!.Value.Should().Be(("TRASPASO_STANDARD", "ABC123", null, "1234567"));
        await using var db = _host.NuevaDb();
        var datos = (await db.Set<SolicitudTramite>().SingleAsync(TestContext.Current.CancellationToken)).Datos;
        datos.Should().Contain("diagnostico").And.Contain("SOAT vigente").And.NotContain("1234567");
    }

    [Fact]
    public async Task AlCancelar_QuedaCanceladaConSuHistorial_SeBorranLosDatosPersonales_YLaSesionSeRevoca()
    {
        await IniciarAsync();
        var (id, token) = await AbrirSesion();
        await Patch(id, token, new { actores = new[] { Actor("vendedor", Vendedor, CorreoVendedor) } });

        var r = await _host.EnviarAsync(HttpMethod.Delete, $"{Base}/solicitudes/{id}", token: token);

        r.StatusCode.Should().Be(HttpStatusCode.OK);
        (await PortalHost.JsonAsync(r)).RootElement.GetProperty("estado").GetString().Should().Be("cancelada");
        await using var db = _host.NuevaDb();
        (await db.Set<SolicitudTramite>().SingleAsync(TestContext.Current.CancellationToken)).Estado.Should().Be("cancelada");
        (await db.Set<PersonaSolicitante>().CountAsync(TestContext.Current.CancellationToken)).Should().Be(0);
        (await db.Set<HistorialEstado>().OrderBy(h => h.OcurrioEn).Select(h => h.Hasta).ToListAsync(TestContext.Current.CancellationToken)).Should().Contain("cancelada");
        (await db.Set<AccesoPiiLog>().AnyAsync(a => a.Accion == "cancelada_por_el_ciudadano", TestContext.Current.CancellationToken)).Should().BeTrue();
        (await _host.EnviarAsync(HttpMethod.Get, $"{Base}/solicitudes/{id}", token: token)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── Soporte ──────────────────────────────────────────────────────────────────────────────────────────────────────

    private static object Cuerpo(string tipo = "TRASPASO_STANDARD", string organismo = PortalHost.Organismo, string cedula = PortalHost.Cedula, string? placa = "abc-123", string? vin = null, bool consentimiento = true) =>
        new { tipoCodigo = tipo, organismoId = organismo, cedula, placa, vin, consentimiento, consentimientoVersion = "v1" };

    private static object Actor(string rol, string documento, string? correo = null) =>
        new { rol, tipoDocumento = "CC", numeroDocumento = documento, nombre = "Persona de Prueba", correo = correo ?? $"p{documento}@example.org", celular = "3001234567", direccion = "Calle 1 # 2-3" };

    private Task<HttpResponseMessage> Abrir(string tipo = "TRASPASO_STANDARD") => _host.EnviarAsync(HttpMethod.Post, $"{Base}/sesiones", Cuerpo(tipo: tipo), captcha: true);

    private async Task<(Guid Id, string Token)> AbrirSesion(string tipo = "TRASPASO_STANDARD")
    {
        var r = await Abrir(tipo);
        r.StatusCode.Should().Be(HttpStatusCode.Created);
        var json = (await PortalHost.JsonAsync(r)).RootElement;
        return (json.GetProperty("solicitudId").GetGuid(), json.GetProperty("token").GetString()!);
    }

    private Task<HttpResponseMessage> Patch(Guid id, string token, object cuerpo) => _host.EnviarAsync(HttpMethod.Patch, $"{Base}/solicitudes/{id}/actores", cuerpo, token);

    private Task<HttpResponseMessage> Put(Guid id, string token, string recurso, object cuerpo) => _host.EnviarAsync(HttpMethod.Put, $"{Base}/solicitudes/{id}/{recurso}", cuerpo, token);

    private static async Task<string?> Campo(HttpResponseMessage r, string nombre) => (await PortalHost.JsonAsync(r)).RootElement.GetProperty(nombre).GetString();

    private static async Task<Dictionary<string, string[]>> Errores(HttpResponseMessage r)
    {
        using var json = await PortalHost.JsonAsync(r);
        return json.RootElement.GetProperty("errors").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.EnumerateArray().Select(x => x.GetString()!).ToArray());
    }

    private async Task<long> Version(Guid id, string token) =>
        (await PortalHost.JsonAsync(await _host.EnviarAsync(HttpMethod.Get, $"{Base}/solicitudes/{id}", token: token))).RootElement.GetProperty("version").GetInt64();

    private async Task<JsonElement> Valores(Guid id, string token) =>
        (await PortalHost.JsonAsync(await _host.EnviarAsync(HttpMethod.Get, $"{Base}/solicitudes/{id}", token: token))).RootElement.GetProperty("fieldValues").Clone();

    private async Task<string> EstadoValidacion(Guid id, short ordinal)
    {
        await using var db = _host.NuevaDb();
        return await db.Set<PersonaSolicitante>().Where(p => p.SolicitudId == id && p.Parte == "comprador" && p.Ordinal == ordinal).Select(p => p.ValidacionEstado).SingleAsync(TestContext.Current.CancellationToken);
    }
}
