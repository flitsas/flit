using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Flit.PersonaNatural.Api;
using Flit.PersonaNatural.Api.Abuso;
using Flit.PersonaNatural.Api.Consultas;
using Flit.PersonaNatural.Api.Persistence;
using Flit.PersonaNatural.Api.Pii;
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
/// HU #14041 (Feature #14022, Épica #14021) — endpoints de consulta del BFF: captcha, topes, dato mínimo, detalle ampliado
/// solo con identidad validada y medición de consumo, sobre el host real, una base migrada y un core-consultas falso (base
/// efímera propia; sin motor se omite en local y falla en CI).
/// </summary>
public sealed class ConsultasEndpointsTests : IAsyncLifetime
{
    private const string Vehiculo = $"{SesionEndpoints.Base}/consultas/vehiculo";
    private const string Persona = $"{SesionEndpoints.Base}/consultas/persona";
    private const string DocumentoPropietario = "1098765432";

    private readonly RelojFalso _reloj = new(new DateTimeOffset(2026, 10, 27, 15, 0, 0, TimeSpan.Zero));
    private readonly ConsultasRuntFalso _runt = new();
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

    // ── AC1: consulta pública mínima ─────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ConCaptchaValido_ElVehiculoSeConsultaYSoloSaleElDatoMinimo()
    {
        if (!await IniciarAsync()) return;
        _runt.Vehiculo = () => ResultadosFalsos.VehiculoCompleto(prenda: true);

        var respuesta = await PostAsync(Vehiculo, VehiculoBody());

        respuesta.StatusCode.Should().Be(HttpStatusCode.OK);
        var texto = await respuesta.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var json = JsonDocument.Parse(texto).RootElement;
        json.GetProperty("existe").GetBoolean().Should().BeTrue();
        json.GetProperty("placa").GetString().Should().Be("ABC123");
        json.GetProperty("clase").GetString().Should().Be("AUTOMOVIL");
        json.GetProperty("tienePrenda").GetBoolean().Should().BeTrue();
        json.GetProperty("tieneRestricciones").GetBoolean().Should().BeTrue();
        texto.Should().NotContainAny(DocumentoPropietario, "PROPIETARIO", "CALLE FICTICIA", "3000000000", "BANCO FICTICIO", "9BWZZZ377VT004251", "MOTOR-FICTICIO", "owner");
        _runt.LlamadasVehiculo.Should().ContainSingle().Which.Should().Be(("ABC123", (string?)null, new DocumentoConsulta("CC", DocumentoPropietario), (string?)null));
    }

    [Fact]
    public async Task ElVehiculoTambienSeConsultaPorVin()
    {
        if (!await IniciarAsync()) return;
        _runt.Vehiculo = ResultadosFalsos.VehiculoInexistente;

        var respuesta = await PostAsync(Vehiculo, new { vin = "9bwzzz377vt004251", consentimiento = true });

        respuesta.StatusCode.Should().Be(HttpStatusCode.OK);
        (await Json(respuesta)).GetProperty("existe").GetBoolean().Should().BeFalse("placa o VIN inexistente responde existe=false sin error");
        _runt.LlamadasVehiculo.Should().ContainSingle().Which.Vin.Should().Be("9BWZZZ377VT004251");
    }

    [Fact]
    public async Task ElConductorSeConsultaPorDocumento_YSoloSaleExisteEstadoYVigencia()
    {
        if (!await IniciarAsync()) return;
        _runt.Conductor = () => ResultadosFalsos.Conductor();

        var respuesta = await PostAsync(Persona, new { documento = new { tipo = "cc", numero = "1.098.765.432" }, consentimiento = true });

        respuesta.StatusCode.Should().Be(HttpStatusCode.OK);
        var texto = await respuesta.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        JsonDocument.Parse(texto).RootElement.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo("existe", "estadoLicencia", "vigencia");
        texto.Should().NotContainAny("NOMBRE-FICTICIO", "1990", DocumentoPropietario, "PYS-FICTICIO");
        _runt.LlamadasConductor.Should().ContainSingle().Which.Documento.Should().Be(new DocumentoConsulta("CC", DocumentoPropietario));
    }

    [Fact]
    public async Task SinCaptchaNoSeLlamaAFlit2_NiSeCuenta()
    {
        if (!await IniciarAsync()) return;

        var sin = await _http.PostAsJsonAsync(Vehiculo, VehiculoBody(), TestContext.Current.CancellationToken);
        var malo = await PostAsync(Persona, new { documento = new { tipo = "CC", numero = DocumentoPropietario }, consentimiento = true }, "token-falso");

        sin.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await CodigoAsync(sin)).Should().Be("captcha_required");
        (await CodigoAsync(malo)).Should().Be("captcha_required");
        _runt.LlamadasVehiculo.Should().BeEmpty();
        _runt.LlamadasConductor.Should().BeEmpty();
        (await ConsumoAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task SinConsentimientoODatosMalFormados_Responde400_SinLlamarNiGastarTope()
    {
        if (!await IniciarAsync(("Abuso:LimiteCedulaPorHora", "1"))) return;

        var sinConsentimiento = await PostAsync(Vehiculo, new { placa = "ABC123", propietario = new { tipo = "CC", numero = DocumentoPropietario }, consentimiento = false });
        var placaMala = await PostAsync(Vehiculo, new { placa = "12-AB", propietario = new { tipo = "CC", numero = DocumentoPropietario }, consentimiento = true });
        var sinPropietario = await PostAsync(Vehiculo, new { placa = "ABC123", consentimiento = true });
        var documentoMalo = await PostAsync(Persona, new { documento = new { tipo = "NIT", numero = "900123456" }, consentimiento = true });
        var vinMalo = await PostAsync(Vehiculo, new { vin = "CORTO", consentimiento = true });

        (await CodigoAsync(sinConsentimiento)).Should().Be("consentimiento_requerido");
        foreach (var r in new[] { placaMala, sinPropietario, documentoMalo, vinMalo })
        {
            r.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await CodigoAsync(r)).Should().Be("datos_invalidos");
        }

        _runt.LlamadasVehiculo.Should().BeEmpty();
        _runt.LlamadasConductor.Should().BeEmpty();
        (await PostAsync(Vehiculo, VehiculoBody())).StatusCode.Should().Be(HttpStatusCode.OK, "los rechazos por formato no gastaron el tope de 1 por documento");
    }

    // ── AC2: tope por ciudadano y global ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ElDocumentoQueSuperaSuTopePorHora_Recibe429ConElTiempoDeEspera_YNoSeLlamaOtraVez()
    {
        if (!await IniciarAsync(("Abuso:LimiteCedulaPorHora", "2"))) return;

        (await PostAsync(Vehiculo, VehiculoBody())).StatusCode.Should().Be(HttpStatusCode.OK);
        (await PostAsync(Persona, new { documento = new { tipo = "CC", numero = DocumentoPropietario }, consentimiento = true })).StatusCode
            .Should().Be(HttpStatusCode.OK, "el tope es por documento, sin importar el tipo de consulta");
        var excedida = await PostAsync(Vehiculo, VehiculoBody());
        var otro = await PostAsync(Persona, new { documento = new { tipo = "CC", numero = "79555111" }, consentimiento = true });

        excedida.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        (await CodigoAsync(excedida)).Should().Be("cedula_rate_limited");
        excedida.Headers.RetryAfter.Should().NotBeNull();
        otro.StatusCode.Should().Be(HttpStatusCode.OK);
        _runt.LlamadasVehiculo.Should().HaveCount(1);
    }

    [Fact]
    public async Task AlAlcanzarElTopeGlobal_LasConsultasSePausanSinLlamarAFlit2()
    {
        if (!await IniciarAsync(("Abuso:TopeGlobalDiario", "1"))) return;

        (await PostAsync(Vehiculo, VehiculoBody())).StatusCode.Should().Be(HttpStatusCode.OK);
        var pausada = await PostAsync(Persona, new { documento = new { tipo = "CC", numero = "79555111" }, consentimiento = true });

        pausada.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await CodigoAsync(pausada)).Should().Be("consultas_no_disponibles");
        _runt.LlamadasConductor.Should().BeEmpty();
    }

    // ── AC5 de B1/B2: proveedor caído ────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ConElProveedorCaido_Responde503ConEspera_SinReintentarEnBucle()
    {
        if (!await IniciarAsync()) return;
        _runt.Vehiculo = ResultadosFalsos.ProveedorCaido;
        _runt.Conductor = ResultadosFalsos.ProveedorCaido;

        var vehiculo = await PostAsync(Vehiculo, VehiculoBody());
        _runt.Falla = new ProveedorNoDisponibleException("core-consultas respondió Unavailable.");
        var persona = await PostAsync(Persona, new { documento = new { tipo = "CC", numero = DocumentoPropietario }, consentimiento = true });

        foreach (var r in new[] { vehiculo, persona })
        {
            r.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
            (await CodigoAsync(r)).Should().Be("proveedor_no_disponible");
            r.Headers.RetryAfter.Should().NotBeNull();
        }

        _runt.LlamadasVehiculo.Should().HaveCount(1);
        _runt.LlamadasConductor.Should().HaveCount(1);
    }

    // ── AC5: medición visible ────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task LaMedicionCuentaLasConsultasPorTipoYPorDia_ConTodoYLosFallidos()
    {
        if (!await IniciarAsync()) return;
        _runt.Vehiculo = ResultadosFalsos.VehiculoInexistente;
        _runt.Conductor = ResultadosFalsos.ConductorInexistente;

        await PostAsync(Vehiculo, VehiculoBody());
        await PostAsync(Vehiculo, new { vin = "9BWZZZ377VT004251", consentimiento = true });
        await PostAsync(Persona, new { documento = new { tipo = "CC", numero = "79555111" }, consentimiento = true });
        await PostAsync(Vehiculo, new { placa = "ZZZ", consentimiento = true }); // rechazada por formato: no cuenta
        _reloj.Avanzar(TimeSpan.FromDays(1));
        _runt.Falla = new ProveedorNoDisponibleException("sin respuesta");
        await PostAsync(Persona, new { documento = new { tipo = "CC", numero = "79555222" }, consentimiento = true }); // llegó a core-consultas y falló: cuenta

        var consumo = await ConsumoAsync();

        var hoy = new DateOnly(2026, 10, 27);
        consumo.Should().BeEquivalentTo(new[]
        {
            new ConsumoDia(hoy, MedicionConsultasService.TipoPersona, 1),
            new ConsumoDia(hoy, MedicionConsultasService.TipoVehiculo, 2),
            new ConsumoDia(hoy.AddDays(1), MedicionConsultasService.TipoPersona, 1),
        }, o => o.WithStrictOrdering());
        using var scope = _app!.Services.CreateScope();
        (await scope.ServiceProvider.GetRequiredService<MedicionConsultasService>().ConsultarAsync(hoy.AddDays(1), hoy.AddDays(1), TestContext.Current.CancellationToken))
            .Should().ContainSingle("el rango filtra por día");
    }

    [Fact]
    public async Task LaMedicionNoSeExponeEnNingunEndpointPublico()
    {
        if (!await IniciarAsync()) return;

        foreach (var ruta in new[] { "/consultas/consumo", "/consultas/medicion", "/admin/consumo", "/consumo" })
            (await _http.GetAsync($"{SesionEndpoints.Base}{ruta}", TestContext.Current.CancellationToken)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── AC3 y AC4: detalle ampliado ──────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ConIdentidadValidada_ElDetalleDelVehiculoDelTramiteSeEntrega()
    {
        if (!await IniciarAsync()) return;
        var (solicitud, token) = await SembrarAsync(("vendedor", "aprobada", null));
        _runt.Vehiculo = () => ResultadosFalsos.VehiculoCompleto(prenda: true);

        var respuesta = await DetalleAsync(solicitud, token);

        respuesta.StatusCode.Should().Be(HttpStatusCode.OK);
        var texto = await respuesta.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var json = JsonDocument.Parse(texto).RootElement;
        json.GetProperty("vehiculo").GetProperty("existe").GetBoolean().Should().BeTrue();
        json.GetProperty("vin").GetString().Should().Be("9BWZZZ377VT004251");
        json.GetProperty("numeroMotor").GetString().Should().Be("MOTOR-FICTICIO-1");
        json.GetProperty("color").GetString().Should().Be("ROJO");
        json.GetProperty("acreedor").GetString().Should().Be("BANCO FICTICIO S.A.");
        texto.Should().NotContainAny(DocumentoPropietario, "PROPIETARIO", "CALLE FICTICIA", "3000000000", "soat");
        var llamada = _runt.LlamadasVehiculo.Should().ContainSingle().Subject;
        llamada.Placa.Should().Be("ABC123");
        llamada.Propietario.Should().Be(new DocumentoConsulta("CC", DocumentoPropietario), "el documento sale del actor validado, descifrado en el BFF");
        llamada.Referencia.Should().Be(solicitud.ToString(), "la consulta queda trazada a la solicitud");
        (await ConsumoAsync()).Should().ContainSingle().Which.Tipo.Should().Be(MedicionConsultasService.TipoVehiculoDetalle);
        await using var db = NuevaDb();
        (await db.Set<AccesoPiiLog>().CountAsync(a => a.Accion == PersonaService.AccionLectura && a.Actor == ConsultaDetalleService.ActorAuditoria, TestContext.Current.CancellationToken))
            .Should().Be(1, "la lectura del documento del actor queda auditada");
    }

    [Theory]
    [InlineData("pendiente", false, "identidad pendiente")]
    [InlineData("sin_solicitar", false, "sin solicitar")]
    [InlineData("rechazada", false, "rechazada")]
    [InlineData("vencida", false, "vencida")]
    public async Task SinIdentidadValidada_Responde403IdentityRequired_YNoConsultaAFlit2(string estado, bool vencida, string porque)
    {
        if (!await IniciarAsync()) return;
        var (solicitud, token) = await SembrarAsync(("vendedor", estado, vencida ? _reloj.GetUtcNow().AddMinutes(-1) : null));

        var respuesta = await DetalleAsync(solicitud, token);

        respuesta.StatusCode.Should().Be(HttpStatusCode.Forbidden, porque);
        (await CodigoAsync(respuesta)).Should().Be("identity_required");
        _runt.LlamadasVehiculo.Should().BeEmpty("sin identidad no se consulta a FLIT 2");
        (await ConsumoAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task LaIdentidadValidadaDeOtraParte_NoSirveParaLaParteQueSePide()
    {
        if (!await IniciarAsync()) return;
        var (solicitud, token) = await SembrarAsync(("vendedor", "pendiente", null), ("comprador", "aprobada", null));

        var pideVendedor = await DetalleAsync(solicitud, token, "vendedor");
        var pideComprador = await DetalleAsync(solicitud, token, "comprador");

        pideVendedor.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        pideComprador.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ElDetalleExigeLaSesionDeEsaSolicitud()
    {
        if (!await IniciarAsync()) return;
        var (solicitud, token) = await SembrarAsync(("vendedor", "aprobada", null));
        var (otra, _) = await SembrarAsync(("vendedor", "aprobada", null));

        var sinToken = await DetalleAsync(solicitud, null);
        var tokenAjeno = await DetalleAsync(otra, token);

        sinToken.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await CodigoAsync(sinToken)).Should().Be("session_invalid");
        tokenAjeno.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await CodigoAsync(tokenAjeno)).Should().Be("session_scope_mismatch");
        _runt.LlamadasVehiculo.Should().BeEmpty();
    }

    [Fact]
    public async Task ElDetalleConProveedorCaido_Responde503_YUnVinNoNecesitaElPropietario()
    {
        if (!await IniciarAsync()) return;
        var (solicitud, token) = await SembrarAsync(("vendedor", "aprobada", null));
        _runt.Vehiculo = ResultadosFalsos.ProveedorCaido;

        var caido = await DetalleAsync(solicitud, token);
        await using (var db = NuevaDb())
        {
            var s = await db.Set<SolicitudTramite>().SingleAsync(TestContext.Current.CancellationToken);
            s.Placa = null;
            s.Vin = "9BWZZZ377VT004251";
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        _runt.Vehiculo = ResultadosFalsos.VehiculoInexistente;
        var porVin = await DetalleAsync(solicitud, token);

        caido.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await CodigoAsync(caido)).Should().Be("proveedor_no_disponible");
        porVin.StatusCode.Should().Be(HttpStatusCode.OK);
        _runt.LlamadasVehiculo.Last().Should().Be((null, "9BWZZZ377VT004251", null, solicitud.ToString()));
    }

    [Fact]
    public async Task ElDetalleConUnaPlacaIlegibleYSinVin_Responde409()
    {
        if (!await IniciarAsync()) return;
        var (solicitud, token) = await SembrarAsync(("vendedor", "aprobada", null));
        await using (var db = NuevaDb())
        {
            (await db.Set<SolicitudTramite>().SingleAsync(TestContext.Current.CancellationToken)).Placa = "???";
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var respuesta = await DetalleAsync(solicitud, token);

        respuesta.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await CodigoAsync(respuesta)).Should().Be("vehiculo_no_definido");
    }

    // ── Infraestructura de la prueba ─────────────────────────────────────────────────────────────────────────────────

    private static object VehiculoBody() =>
        new { placa = "abc-123", propietario = new { tipo = "CC", numero = DocumentoPropietario }, consentimiento = true };

    private async Task<bool> IniciarAsync(params (string Clave, string Valor)[] ajustes)
    {
        var server = Environment.GetEnvironmentVariable("ConnectionStrings__Core") ?? $"Host=127.0.0.1;Port=5432;Username={Environment.UserName}";
        _database = new NpgsqlConnectionStringBuilder(server) { Database = $"flit_pncon_{Guid.NewGuid():N}"[..30] }.ConnectionString;
        try
        {
            await using var admin = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(server) { Database = "postgres" }.ConnectionString);
            await admin.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE DATABASE \"{new NpgsqlConnectionStringBuilder(_database).Database}\"", admin);
            await create.ExecuteNonQueryAsync();
        }
        catch (NpgsqlException ex) when (!string.Equals(Environment.GetEnvironmentVariable("CI"), "true", StringComparison.OrdinalIgnoreCase))
        {
            _skip = $"Postgres no alcanzable: {ex.Message}";
            Assert.Skip(_skip);
            return false;
        }

        var settings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:Servicio"] = _database,
            ["Platform:ServiceClient:ClientSecret"] = "secreto-de-prueba",
            ["Platform:ServiceClient:TokenEndpoint"] = "http://core-identity/connect/token",
            ["Platform:Auth:JwksUri"] = "http://core-identity/.well-known/jwks.json",
            ["Platform:Auth:Issuers:0"] = "https://hub.prueba/",
            ["Platform:Messaging:ConnectionString"] = "amqp://flit:x@127.0.0.1:1/",
            [ServicioSettings.GrpcPortKey] = "5995",
            ["Pii:ClaveHmac"] = "clave-de-prueba-de-al-menos-32-caracteres!",
            ["Captcha:SecretKey"] = "secreto-de-captcha",
            ["Pii:Cifrado:Llaves:1"] = "AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8=",
        };
        foreach (var (clave, valor) in ajustes)
            settings[clave] = valor;

        _app = Program.Build([], b =>
        {
            b.WebHost.UseTestServer();
            b.Configuration.AddInMemoryCollection(settings);
            b.Services.AddSingleton<TimeProvider>(_reloj);
            b.Services.AddSingleton<IProveedorCaptcha, CaptchaFalso>();
            b.Services.AddSingleton<IConsultasRunt>(_runt);
        });
        await _app.StartAsync();
        await Program.MigrateAsync(_app);
        _http = _app.GetTestClient();
        return true;
    }

    private async Task<HttpResponseMessage> PostAsync(string ruta, object cuerpo, string captcha = CaptchaFalso.TokenValido)
    {
        using var peticion = new HttpRequestMessage(HttpMethod.Post, ruta) { Content = JsonContent.Create(cuerpo) };
        peticion.Headers.Add(AbusoEndpoints.CabeceraCaptcha, captcha);
        return await _http.SendAsync(peticion, TestContext.Current.CancellationToken);
    }

    private async Task<HttpResponseMessage> DetalleAsync(Guid solicitud, string? token, string? parte = null)
    {
        using var peticion = new HttpRequestMessage(HttpMethod.Post, $"{SesionEndpoints.Base}/solicitudes/{solicitud}/consultas/vehiculo-detalle")
        {
            Content = JsonContent.Create(new { parte }),
        };
        if (token is not null)
            peticion.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await _http.SendAsync(peticion, TestContext.Current.CancellationToken);
    }

    /// <summary>Solicitud con placa ABC123, sesión abierta y personas con el estado de identidad pedido.</summary>
    private async Task<(Guid Solicitud, string Token)> SembrarAsync(params (string Parte, string Estado, DateTimeOffset? VigenteHasta)[] personas)
    {
        using var scope = _app!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PersonaNaturalDb>();
        var solicitud = new SolicitudTramite
        {
            Id = Guid.CreateVersion7(),
            TenantId = Guid.NewGuid(),
            CodigoPublico = $"PN-2026-{Guid.NewGuid():N}"[..20],
            TipoTramiteCodigo = "TRASPASO_STANDARD",
            DefinicionVersion = 1,
            OrganismoId = Guid.NewGuid(),
            OrganismoNombre = "Organismo de prueba",
            Placa = "ABC123",
            CedulaSolicitanteHash = scope.ServiceProvider.GetRequiredService<PiiHasher>().HashCedula("1020456789"),
            CorrelationId = Guid.NewGuid(),
        };
        db.Add(solicitud);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var servicio = scope.ServiceProvider.GetRequiredService<PersonaService>();
        foreach (var (parte, estado, vigente) in personas)
        {
            var id = await servicio.GuardarAsync(solicitud.Id, new DatosPersona(parte, 1, "CC", DocumentoPropietario, "NOMBRE FICTICIO", null, null, null), TestContext.Current.CancellationToken);
            var persona = await db.Set<PersonaSolicitante>().SingleAsync(p => p.Id == id, TestContext.Current.CancellationToken);
            persona.ValidacionEstado = estado;
            persona.ValidacionVigenteHasta = vigente;
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var sesion = await scope.ServiceProvider.GetRequiredService<SesionService>().IniciarAsync(solicitud.Id, null, TestContext.Current.CancellationToken);
        return (solicitud.Id, sesion.Token);
    }

    private async Task<IReadOnlyList<ConsumoDia>> ConsumoAsync()
    {
        using var scope = _app!.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<MedicionConsultasService>()
            .ConsultarAsync(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), TestContext.Current.CancellationToken);
    }

    private PersonaNaturalDb NuevaDb()
    {
        var options = new DbContextOptionsBuilder<PersonaNaturalDb>();
        PersonaNaturalDb.Configure(options, _database);
        return new PersonaNaturalDb(options.Options);
    }

    private static async Task<JsonElement> Json(HttpResponseMessage respuesta) =>
        await respuesta.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);

    private static async Task<string?> CodigoAsync(HttpResponseMessage respuesta)
    {
        var cuerpo = await Json(respuesta);
        return cuerpo.TryGetProperty("code", out var codigo) ? codigo.GetString() : null;
    }

    private sealed class RelojFalso(DateTimeOffset inicio) : TimeProvider
    {
        private DateTimeOffset _ahora = inicio;

        public override DateTimeOffset GetUtcNow() => _ahora;

        public void Avanzar(TimeSpan tiempo) => _ahora += tiempo;
    }
}
