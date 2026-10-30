using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Flit.PersonaNatural.Api;
using Flit.PersonaNatural.Api.Abuso;
using Flit.PersonaNatural.Api.Persistence;
using Flit.PersonaNatural.Api.Radicados;
using Flit.PersonaNatural.Api.Sesiones;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace Flit.PersonaNatural.Tests;

/// <summary>
/// HU #14042 (Feature #14023, Épica #14021, ADR-0075) — consulta pública de radicado con cédula y macro-estados. La tabla de
/// macro-estados se prueba sin base; el endpoint, sobre el host real y una base efímera propia (sin motor se omite en local
/// y falla en CI).
/// </summary>
public sealed class RadicadosTests : IAsyncLifetime
{
    private const string ClaveHmac = "clave-de-prueba-de-al-menos-32-caracteres!";
    private const string Cedula = "1020456789";
    private const string CedulaComprador = "52987654";
    private const string CorreoParte = "comprador.ficticio@ejemplo.test";
    private const string Radicado = "PN-2026-000123";

    private static readonly string[] UnoPorMacro = ["borrador", "asignado", "rechazado", "aprobado"];

    private readonly RelojFalso _reloj = new(new DateTimeOffset(2026, 10, 27, 15, 0, 0, TimeSpan.Zero));
    private string _database = string.Empty;
    private string? _skip;
    private WebApplication? _app;
    private HttpClient _http = null!;

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        if (_app is not null)
            await _app.DisposeAsync();
        if (_skip is null && _database.Length > 0)
        {
            NpgsqlConnection.ClearAllPools();
            await using var db = NuevaDb();
            await db.Database.EnsureDeletedAsync();
        }
    }

    // ── AC2: tabla de macro-estados (sin base) ───────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("borrador", MacroEstado.PendienteValidacion)]
    [InlineData("preparado", MacroEstado.PendienteValidacion)]
    [InlineData("preasignacion", MacroEstado.EnRevisionOt)]
    [InlineData("asignado", MacroEstado.EnRevisionOt)]
    [InlineData("entregado", MacroEstado.EnRevisionOt)]
    [InlineData("rechazado", MacroEstado.Subsanacion)]
    [InlineData("aprobado", MacroEstado.Finalizado)]
    [InlineData("anulado", MacroEstado.Finalizado)]
    [InlineData("revocado", MacroEstado.Finalizado)]
    [InlineData("APROBADO", MacroEstado.Finalizado)]
    public void CadaEstadoDeFlit2_SeMapeaAUnMacroEstado(string estadoTramite, MacroEstado esperado) =>
        MacroEstados.Resolver(EstadoSolicitud.Creada, estadoTramite).Macro.Should().Be(esperado);

    [Fact]
    public void SinEstadoDeFlit2_TodoEstadoDeLaSolicitudEsPendienteDeValidacion()
    {
        foreach (var estado in EstadoSolicitud.Todos)
            MacroEstados.Resolver(estado, null).Macro.Should().Be(MacroEstado.PendienteValidacion, estado);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", "")]
    [InlineData("estado_nuevo_de_flit", null)]
    [InlineData("creada", "estado_nuevo_de_flit")]
    public void UnEstadoDesconocido_CaeEnPendienteDeValidacion_SinLanzar(string? estadoSolicitud, string? estadoTramite)
    {
        var info = MacroEstados.Resolver(estadoSolicitud, estadoTramite);

        info.Macro.Should().Be(MacroEstado.PendienteValidacion);
        info.Etiqueta.Should().Be("Pendiente de Validación");
    }

    [Fact]
    public void LosCuatroMacroEstados_TienenLasEtiquetasDelFront_YTextoPropio()
    {
        MacroEstados.Etiquetas.Should().Equal("Pendiente de Validación", "En Revisión OT", "Subsanación", "Finalizado");
        MacroEstados.Version.Should().NotBeNullOrWhiteSpace();
        var todos = UnoPorMacro.Select(e => MacroEstados.Resolver(EstadoSolicitud.Creada, e));
        todos.Select(t => t.Mensaje).Should().OnlyHaveUniqueItems().And.OnlyContain(m => !string.IsNullOrWhiteSpace(m));
        todos.Select(t => t.Codigo).Should().Equal("pendiente_validacion", "en_revision_ot", "subsanacion", "finalizado");
    }

    // ── AC1: consulta exitosa ────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ConRadicadoYCedulaDelPrincipal_VePlacaTipoOrganismoMacroEstadoYTracking()
    {
        if (!await IniciarAsync()) return;
        var id = await SembrarAsync(estado: EstadoSolicitud.Creada, estadoTramite: "asignado", partes: [(Cedula, "VENDEDOR"), (CedulaComprador, "COMPRADOR")]);
        await SembrarHistorialAsync(id, (EstadoSolicitud.EnCola, 1), (EstadoSolicitud.Creada, 2), ("asignado", 3));

        var respuesta = await ConsultarAsync(Radicado.ToLowerInvariant(), "1.020.456.789");

        respuesta.StatusCode.Should().Be(HttpStatusCode.OK);
        var cuerpo = await LeerAsync(respuesta);
        cuerpo.GetProperty("radicado").GetString().Should().Be(Radicado);
        cuerpo.GetProperty("placa").GetString().Should().Be("ABC123");
        cuerpo.GetProperty("tipoTramite").GetString().Should().Be("TRASPASO_STANDARD");
        cuerpo.GetProperty("organismo").GetString().Should().Be("Organismo de prueba");
        cuerpo.GetProperty("soloLectura").GetBoolean().Should().BeFalse();
        var macro = cuerpo.GetProperty("macroEstado");
        macro.GetProperty("etiqueta").GetString().Should().Be("En Revisión OT");
        macro.GetProperty("codigo").GetString().Should().Be("en_revision_ot");
        cuerpo.GetProperty("mensaje").GetString().Should().NotBeNullOrWhiteSpace();

        var pasos = cuerpo.GetProperty("pasos").EnumerateArray().ToList();
        pasos.Select(p => p.GetProperty("estado").GetString()).Should().Equal("done", "done", "done", "current", "pending");
        pasos.Select(p => p.GetProperty("clave").GetString()).Should().Equal("borrador", "validacion_identidad", "enviado_ot", "en_revision", "resolucion");
        pasos[0].GetProperty("fecha").GetDateTimeOffset().Should().Be(_reloj.GetUtcNow().AddHours(-10));
        pasos[1].GetProperty("fecha").GetDateTimeOffset().Should().Be(_reloj.GetUtcNow().AddHours(1));
        pasos[3].GetProperty("fecha").GetDateTimeOffset().Should().Be(_reloj.GetUtcNow().AddHours(3));
        pasos[4].GetProperty("fecha").ValueKind.Should().Be(JsonValueKind.Null, "un paso pendiente no tiene fecha");
        cuerpo.TryGetProperty("subsanacion", out var subsanacion).Should().BeTrue();
        subsanacion.ValueKind.Should().Be(JsonValueKind.Null);

        await using var db = NuevaDb();
        (await db.Set<AccesoPiiLog>().CountAsync(a => a.Accion == RadicadoConsultaService.AccionConsulta && a.SolicitudId == id, TestContext.Current.CancellationToken))
            .Should().Be(1, "cada consulta exitosa queda auditada");
    }

    [Fact]
    public async Task ElRadicadoTambienSeBuscaPorElNumeroDeFlit2()
    {
        if (!await IniciarAsync()) return;
        await SembrarAsync(estado: EstadoSolicitud.Creada, estadoTramite: "aprobado", referencia: "TR-2026-004821");

        var respuesta = await ConsultarAsync("tr-2026-004821", Cedula);

        respuesta.StatusCode.Should().Be(HttpStatusCode.OK);
        var cuerpo = await LeerAsync(respuesta);
        cuerpo.GetProperty("macroEstado").GetProperty("etiqueta").GetString().Should().Be("Finalizado");
        cuerpo.GetProperty("numeroReferencia").GetString().Should().Be("TR-2026-004821");
        cuerpo.GetProperty("pasos").EnumerateArray().Select(p => p.GetProperty("estado").GetString()).Should().OnlyContain(e => e == "done");
    }

    [Fact]
    public async Task LaPlacaEsOpcional_PeroSiSeEnviaDebeSerLaDelTramite()
    {
        if (!await IniciarAsync()) return;
        await SembrarAsync();

        (await ConsultarAsync(Radicado, Cedula, placa: "abc-123")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await ConsultarAsync(Radicado, Cedula, placa: "ZZZ999")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task LaRespuesta_NuncaTrae_DocumentosCorreosTelefonosNiLaCedulaDeLasPartes()
    {
        if (!await IniciarAsync()) return;
        await SembrarAsync(estado: EstadoSolicitud.Creada, estadoTramite: "rechazado", subsanacion: "Falta el paz y salvo.",
            partes: [(Cedula, "VENDEDOR"), (CedulaComprador, "COMPRADOR")]);

        var texto = await (await ConsultarAsync(Radicado, Cedula)).Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        texto.Should().NotContain(Cedula).And.NotContain(CedulaComprador).And.NotContain(CorreoParte).And.NotContain("3001234567");
        texto.ToLowerInvariant().Should().NotContain("correo").And.NotContain("celular").And.NotContain("documento").And.NotContain("cedula");
    }

    // ── AC2/AC3: estados y subsanación ───────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(EstadoSolicitud.Borrador, null, "Pendiente de Validación", "current,pending,pending,pending,pending")]
    [InlineData(EstadoSolicitud.PendienteValidacion, null, "Pendiente de Validación", "done,current,pending,pending,pending")]
    [InlineData(EstadoSolicitud.EnCola, null, "Pendiente de Validación", "done,done,current,pending,pending")]
    [InlineData(EstadoSolicitud.RechazadaCreacion, null, "Pendiente de Validación", "done,done,rejected,pending,pending")]
    [InlineData(EstadoSolicitud.Creada, "preparado", "Pendiente de Validación", "done,done,current,pending,pending")]
    [InlineData(EstadoSolicitud.Creada, "preasignacion", "En Revisión OT", "done,done,done,current,pending")]
    [InlineData(EstadoSolicitud.Creada, "aprobado", "Finalizado", "done,done,done,done,done")]
    [InlineData(EstadoSolicitud.Creada, "revocado", "Finalizado", "done,done,done,done,rejected")]
    public async Task ElEstadoInternoSeMuestraComoMacroEstado(string estado, string? estadoTramite, string etiqueta, string pasosEsperados)
    {
        if (!await IniciarAsync()) return;
        await SembrarAsync(estado: estado, estadoTramite: estadoTramite);

        var cuerpo = await LeerAsync(await ConsultarAsync(Radicado, Cedula));

        cuerpo.GetProperty("macroEstado").GetProperty("etiqueta").GetString().Should().Be(etiqueta);
        string.Join(',', cuerpo.GetProperty("pasos").EnumerateArray().Select(p => p.GetProperty("estado").GetString())).Should().Be(pasosEsperados);
    }

    [Fact]
    public async Task UnTramiteEnSubsanacion_MuestraElMotivoYLaAccionParaCorregir()
    {
        if (!await IniciarAsync()) return;
        await SembrarAsync(estado: EstadoSolicitud.Creada, estadoTramite: "rechazado", subsanacion: "El paz y salvo de impuestos está vencido.");

        var cuerpo = await LeerAsync(await ConsultarAsync(Radicado, Cedula));

        cuerpo.GetProperty("macroEstado").GetProperty("etiqueta").GetString().Should().Be("Subsanación");
        var subsanacion = cuerpo.GetProperty("subsanacion");
        subsanacion.GetProperty("motivo").GetString().Should().Be("El paz y salvo de impuestos está vencido.");
        subsanacion.GetProperty("accionCorregir").GetBoolean().Should().BeTrue();
        var pasos = cuerpo.GetProperty("pasos").EnumerateArray().ToList();
        pasos[4].GetProperty("titulo").GetString().Should().Be("Rechazado");
        pasos[4].GetProperty("estado").GetString().Should().Be("rejected");
    }

    [Fact]
    public async Task UnRechazoSinSubsanacionActiva_NoOfreceLaAccionDeCorregir()
    {
        if (!await IniciarAsync()) return;
        await SembrarAsync(estado: EstadoSolicitud.Creada, estadoTramite: "rechazado");

        var subsanacion = (await LeerAsync(await ConsultarAsync(Radicado, Cedula))).GetProperty("subsanacion");

        subsanacion.GetProperty("accionCorregir").GetBoolean().Should().BeFalse();
    }

    // ── AC4: datos que no coinciden ──────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RadicadoInexistenteYCedulaDistinta_RespondenExactamenteLoMismo()
    {
        if (!await IniciarAsync()) return;
        await SembrarAsync();

        var inexistente = await ConsultarAsync("PN-NO-EXISTE", Cedula);
        var cedulaMala = await ConsultarAsync(Radicado, "999999");
        var placaMala = await ConsultarAsync(Radicado, Cedula, placa: "ZZZ999");

        foreach (var r in new[] { inexistente, cedulaMala, placaMala })
            r.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var cuerpos = new[] { inexistente, cedulaMala, placaMala }
            .Select(r => r.Content.ReadAsStringAsync(TestContext.Current.CancellationToken).Result).ToList();
        cuerpos.Distinct().Should().HaveCount(1, "no se puede saber cuál dato falló");
        cuerpos[0].Should().Contain(SesionEndpoints.MensajeNoEncontrada).And.Contain(SesionEndpoints.CodigoNoEncontrada);
        inexistente.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
    }

    [Fact]
    public async Task UnTramiteCancelado_NoSeConsulta()
    {
        if (!await IniciarAsync()) return;
        await SembrarAsync(estado: EstadoSolicitud.Cancelada);

        (await ConsultarAsync(Radicado, Cedula)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData(null, null, "ABC123")]
    [InlineData("PN-1", "123", "")]
    [InlineData("PN-1234567890123456789012345678901234567890123", "123", "ABC123")]
    public async Task SinPlacaOSinRadicadoNiCedula_Responde400(string? radicado, string? cedula, string? placa)
    {
        if (!await IniciarAsync()) return;

        (await ConsultarAsync(radicado, cedula, placa)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ConPlacaYRadicado_SinCedula_ConsultaElTramite()
    {
        if (!await IniciarAsync()) return;
        await SembrarAsync(estado: EstadoSolicitud.Creada, estadoTramite: "asignado");

        var respuesta = await ConsultarAsync(Radicado, cedula: null, placa: "ABC123");

        respuesta.StatusCode.Should().Be(HttpStatusCode.OK);
        var cuerpo = await LeerAsync(respuesta);
        cuerpo.GetProperty("radicado").GetString().Should().Be(Radicado);
        cuerpo.GetProperty("soloLectura").GetBoolean().Should().BeTrue("sin cédula no se afirma el solicitante");
    }

    [Fact]
    public async Task ConPlacaYCedula_SinRadicado_ConsultaElTramite()
    {
        if (!await IniciarAsync()) return;
        await SembrarAsync(estado: EstadoSolicitud.Creada, estadoTramite: "asignado");

        var respuesta = await ConsultarAsync(radicado: null, Cedula, placa: "ABC123");

        respuesta.StatusCode.Should().Be(HttpStatusCode.OK);
        var cuerpo = await LeerAsync(respuesta);
        cuerpo.GetProperty("radicado").GetString().Should().Be(Radicado);
        cuerpo.GetProperty("soloLectura").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task SinCaptchaValido_Responde400_YNoCuentaComoIntento()
    {
        if (!await IniciarAsync()) return;
        await SembrarAsync();

        var respuesta = await ConsultarAsync(Radicado, Cedula, captcha: "token-falso");

        respuesta.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        await using var db = NuevaDb();
        (await db.Set<IntentoReanudar>().CountAsync(TestContext.Current.CancellationToken)).Should().Be(0);
        (await db.Set<AccesoPiiLog>().CountAsync(TestContext.Current.CancellationToken)).Should().Be(0);
    }

    // ── AC5: cédula de otra parte ────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task UnaParteQueNoEsElPrincipal_VeElTramite_PeroEnSoloLectura()
    {
        if (!await IniciarAsync()) return;
        await SembrarAsync(partes: [(Cedula, "VENDEDOR"), (CedulaComprador, "COMPRADOR")]);

        var principal = await LeerAsync(await ConsultarAsync(Radicado, Cedula));
        var parte = await ConsultarAsync(Radicado, CedulaComprador);

        parte.StatusCode.Should().Be(HttpStatusCode.OK);
        var cuerpo = await LeerAsync(parte);
        cuerpo.GetProperty("soloLectura").GetBoolean().Should().BeTrue();
        principal.GetProperty("soloLectura").GetBoolean().Should().BeFalse();
        cuerpo.GetProperty("macroEstado").GetProperty("codigo").GetString().Should().Be(principal.GetProperty("macroEstado").GetProperty("codigo").GetString());
    }

    [Fact]
    public async Task UnaCedulaAjenaAlTramite_NoLoVe_AunqueExistaOtraParte()
    {
        if (!await IniciarAsync()) return;
        await SembrarAsync(partes: [(Cedula, "VENDEDOR"), (CedulaComprador, "COMPRADOR")]);

        (await ConsultarAsync(Radicado, "71555444")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── AC6: límites ─────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task TrasCincoFallosSobreUnRadicado_Responde429ConRetryAfter_YSeLiberaAlPasarLaVentana()
    {
        if (!await IniciarAsync()) return;
        await SembrarAsync();

        for (var i = 0; i < 5; i++)
            (await ConsultarAsync(Radicado, $"mal{i}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        var sexto = await ConsultarAsync(Radicado, "mal5");

        sexto.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        (await CodigoAsync(sexto)).Should().Be(SesionEndpoints.CodigoBloqueado);
        sexto.Headers.RetryAfter!.Delta!.Value.TotalSeconds.Should().BeInRange(1, 15 * 60);
        (await ConsultarAsync(Radicado, Cedula)).StatusCode.Should().Be(HttpStatusCode.TooManyRequests, "mientras dure el bloqueo no se valida nada, ni los datos correctos");

        _reloj.Avanzar(TimeSpan.FromMinutes(16));
        (await ConsultarAsync(Radicado, Cedula)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task UnRadicadoQueNoExiste_TambienSeBloquea_ParaNoDelatarCualesExisten()
    {
        if (!await IniciarAsync()) return;

        for (var i = 0; i < 5; i++)
            (await ConsultarAsync("PN-FANTASMA", $"c{i}")).StatusCode.Should().Be(HttpStatusCode.NotFound);

        (await ConsultarAsync("PN-FANTASMA", "c5")).StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task ElBloqueoSeComparteConReanudar_PorLaMismaClaveDeRadicado()
    {
        if (!await IniciarAsync()) return;
        await SembrarAsync();

        for (var i = 0; i < 5; i++)
            await ConsultarAsync(Radicado, $"mal{i}");

        using var peticion = new HttpRequestMessage(HttpMethod.Post, $"{SesionEndpoints.Base}/sesiones/reanudar") { Content = JsonContent.Create(new ReanudarRequest(Radicado, Cedula)) };
        peticion.Headers.Add(AbusoEndpoints.CabeceraCaptcha, CaptchaFalso.TokenValido);
        (await _http.SendAsync(peticion, TestContext.Current.CancellationToken)).StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task LaIPQueSuperaElLimite_Recibe429()
    {
        if (!await IniciarAsync(("Abuso:LimiteIpPorMinuto", "2"))) return;
        await SembrarAsync();

        (await ConsultarAsync(Radicado, Cedula)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await ConsultarAsync(Radicado, Cedula)).StatusCode.Should().Be(HttpStatusCode.OK);
        var tercera = await ConsultarAsync(Radicado, Cedula);

        tercera.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        (await CodigoAsync(tercera)).Should().Be("rate_limited");
    }

    // ── Apoyo ────────────────────────────────────────────────────────────────────────────────────────────────────────

    private async Task<bool> IniciarAsync(params (string Clave, string Valor)[] extra)
    {
        var server = Environment.GetEnvironmentVariable("ConnectionStrings__Core") ?? $"Host=127.0.0.1;Port=5432;Username={Environment.UserName}";
        _database = new NpgsqlConnectionStringBuilder(server) { Database = $"flit_pnrad_{Guid.NewGuid():N}"[..30] }.ConnectionString;
        try
        {
            await using var admin = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(server) { Database = "postgres" }.ConnectionString);
            await admin.OpenAsync(TestContext.Current.CancellationToken);
            await using var create = new NpgsqlCommand($"CREATE DATABASE \"{new NpgsqlConnectionStringBuilder(_database).Database}\"", admin);
            await create.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }
        catch (NpgsqlException ex) when (!string.Equals(Environment.GetEnvironmentVariable("CI"), "true", StringComparison.OrdinalIgnoreCase))
        {
            _skip = $"Postgres no alcanzable: {ex.Message}";
            Assert.Skip(_skip);
            return false;
        }

        var settings = Settings();
        foreach (var (clave, valor) in extra)
            settings[clave] = valor;
        _app = Program.Build([], b =>
        {
            b.WebHost.UseTestServer();
            b.Configuration.AddInMemoryCollection(settings);
            b.Services.AddSingleton<TimeProvider>(_reloj);
            b.Services.AddSingleton<IProveedorCaptcha, CaptchaFalso>();
        });
        await _app.StartAsync(TestContext.Current.CancellationToken);
        await Program.MigrateAsync(_app);
        _http = _app.GetTestClient();
        return true;
    }

    private Dictionary<string, string?> Settings() => new()
    {
        ["ConnectionStrings:Servicio"] = _database,
        ["Platform:ServiceClient:ClientSecret"] = "secreto-de-prueba",
        ["Platform:ServiceClient:TokenEndpoint"] = "http://core-identity/connect/token",
        ["Platform:Auth:JwksUri"] = "http://core-identity/.well-known/jwks.json",
        ["Platform:Auth:Issuers:0"] = "https://hub.prueba/",
        ["Platform:Messaging:ConnectionString"] = "amqp://flit:x@127.0.0.1:1/",
        [ServicioSettings.GrpcPortKey] = "5996",
        ["Pii:ClaveHmac"] = ClaveHmac,
        ["Captcha:SecretKey"] = "secreto-de-captcha",
        ["Pii:Cifrado:Llaves:1"] = "AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8=",
    };

    private PersonaNaturalDb NuevaDb()
    {
        var options = new DbContextOptionsBuilder<PersonaNaturalDb>();
        PersonaNaturalDb.Configure(options, _database);
        return new PersonaNaturalDb(options.Options);
    }

    private static PiiHasher NuevoHasher() =>
        new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Pii:ClaveHmac"] = ClaveHmac }).Build());

    private async Task<Guid> SembrarAsync(
        string estado = EstadoSolicitud.Borrador, string? estadoTramite = null, string? subsanacion = null, string? referencia = null,
        (string Cedula, string Parte)[]? partes = null)
    {
        var hasher = NuevoHasher();
        await using var db = NuevaDb();
        var solicitud = new SolicitudTramite
        {
            Id = Guid.CreateVersion7(),
            TenantId = Guid.NewGuid(),
            CodigoPublico = Radicado,
            TipoTramiteCodigo = "TRASPASO_STANDARD",
            DefinicionVersion = 1,
            OrganismoId = Guid.NewGuid(),
            OrganismoNombre = "Organismo de prueba",
            Placa = "ABC123",
            Estado = estado,
            CedulaSolicitanteHash = hasher.HashCedula(Cedula),
            ReferenceNumber = referencia,
            EstadoTramite = estadoTramite,
            SubsanacionActiva = subsanacion is not null,
            SubsanacionMotivo = subsanacion,
            CorrelationId = Guid.NewGuid(),
            CreatedAt = _reloj.GetUtcNow().AddHours(-10),
            UpdatedAt = _reloj.GetUtcNow().AddHours(-10),
        };
        db.Add(solicitud);
        short ordinal = 1;
        foreach (var (cedula, parte) in partes ?? [])
        {
            db.Add(new PersonaSolicitante
            {
                Id = Guid.CreateVersion7(),
                SolicitudId = solicitud.Id,
                Parte = parte,
                Ordinal = ordinal++,
                TipoDocumento = "CC",
                DocumentoCifrado = [1, 2, 3],
                DocumentoHash = hasher.HashCedula(cedula),
                CorreoCifrado = System.Text.Encoding.UTF8.GetBytes(CorreoParte),
            });
        }

        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return solicitud.Id;
    }

    private async Task SembrarHistorialAsync(Guid solicitudId, params (string Hasta, int HorasDespues)[] cambios)
    {
        await using var db = NuevaDb();
        foreach (var (hasta, horas) in cambios)
            db.Add(new HistorialEstado { Id = Guid.CreateVersion7(), SolicitudId = solicitudId, Hasta = hasta, Origen = "prueba", OcurrioEn = _reloj.GetUtcNow().AddHours(horas) });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private async Task<HttpResponseMessage> ConsultarAsync(string? radicado, string? cedula, string? placa = "ABC123", string captcha = CaptchaFalso.TokenValido)
    {
        using var peticion = new HttpRequestMessage(HttpMethod.Post, $"{SesionEndpoints.Base}/radicados/consulta")
        {
            Content = JsonContent.Create(new ConsultaRadicadoRequest(radicado, cedula, placa)),
        };
        peticion.Headers.Add(AbusoEndpoints.CabeceraCaptcha, captcha);
        return await _http.SendAsync(peticion, TestContext.Current.CancellationToken);
    }

    private static Task<JsonElement> LeerAsync(HttpResponseMessage respuesta) => respuesta.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);

    private static async Task<string?> CodigoAsync(HttpResponseMessage respuesta)
    {
        var cuerpo = await LeerAsync(respuesta);
        return cuerpo.TryGetProperty("code", out var codigo) ? codigo.GetString() : null;
    }

    /// <summary>Reloj que solo avanza cuando la prueba lo manda.</summary>
    private sealed class RelojFalso(DateTimeOffset inicio) : TimeProvider
    {
        private DateTimeOffset _ahora = inicio;

        public override DateTimeOffset GetUtcNow() => _ahora;

        public void Avanzar(TimeSpan tiempo) => _ahora += tiempo;
    }
}
