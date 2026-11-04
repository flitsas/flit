using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using Flit.PersonaNatural.Api;
using Flit.PersonaNatural.Api.Abuso;
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
using Microsoft.Extensions.Logging;
using Npgsql;
using Xunit;

namespace Flit.PersonaNatural.Tests;

/// <summary>
/// HU #14037 (Feature #14022, Épica #14021) — datos personales cifrados, sin PII en logs, retención de borradores
/// abandonados, auditoría de lectura y llaves rotables. Solo datos ficticios.
/// </summary>
public sealed class PiiTests : IAsyncLifetime
{
    private const string Llave1 = "AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8=";
    private const string Llave2 = "Hx4dHBsaGRgXFhUUExIREA8ODQwLCgkIBwYFBAMCAQA=";
    private const string Cedula = "1020456789";
    private const string Nombre = "Maria Ficticia Prueba";
    private const string Correo = "ficticia.prueba@example.test";
    private const string Celular = "3001234567";
    private const string Direccion = "Calle 99 # 88-77 Apto 666";

    private readonly RelojFalso _reloj = new(new DateTimeOffset(2026, 10, 27, 15, 0, 0, TimeSpan.Zero));
    private readonly RegistroDeLogs _logs = new();
    private string _database = string.Empty;
    private WebApplication? _app;
    private HttpClient _http = null!;

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        if (_app is not null)
            await _app.DisposeAsync();
        if (_database.Length > 0)
        {
            NpgsqlConnection.ClearAllPools();
            await using var db = NuevaDb();
            await db.Database.EnsureDeletedAsync();
        }
    }

    // ── AC1: cifrado en reposo ───────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task LosDatosPersonales_QuedanCifradosEnLaBase_ConHuellaHmacDelDocumento()
    {
        await IniciarAsync();
        var solicitud = await SembrarSolicitudAsync(_reloj.GetUtcNow());

        await EnScopeAsync(s => s.GuardarAsync(solicitud, Persona(), TestContext.Current.CancellationToken));

        await using var db = NuevaDb();
        var fila = await db.Set<PersonaSolicitante>().AsNoTracking().SingleAsync(TestContext.Current.CancellationToken);
        foreach (var claro in new[] { Cedula, Nombre, Correo, Celular, Direccion })
        {
            var bytes = Encoding.UTF8.GetBytes(claro);
            foreach (var columna in new[] { fila.DocumentoCifrado, fila.NombreCifrado, fila.CorreoCifrado, fila.CelularCifrado, fila.DireccionCifrada })
                columna.AsSpan().IndexOf(bytes).Should().Be(-1, "ninguna columna guarda el dato en claro");
        }

        fila.DocumentoCifrado[0].Should().Be(1, "lleva la versión de la llave con la que se cifró");
        fila.DocumentoHash.Should().Equal(Hasher().HashCedula("1.020.456.789"), "la huella permite buscar sin descifrar");
    }

    [Fact]
    public async Task AlLeer_SeDescifra_YSeVuelveAGuardarSinDuplicar()
    {
        await IniciarAsync();
        var solicitud = await SembrarSolicitudAsync(_reloj.GetUtcNow());
        var id1 = await EnScopeAsync(s => s.GuardarAsync(solicitud, Persona(), TestContext.Current.CancellationToken));
        var id2 = await EnScopeAsync(s => s.GuardarAsync(solicitud, Persona() with { Nombre = "Otro Nombre Ficticio" }, TestContext.Current.CancellationToken));

        var personas = await EnScopeAsync(s => s.LeerAsync(solicitud, "operador:prueba", TestContext.Current.CancellationToken));

        id2.Should().Be(id1, "misma parte y posición: se reemplaza");
        var persona = personas.Should().ContainSingle().Subject;
        persona.Documento.Should().Be(Cedula);
        persona.Nombre.Should().Be("Otro Nombre Ficticio");
        persona.Correo.Should().Be(Correo);
        persona.Celular.Should().Be(Celular);
        persona.Direccion.Should().Be(Direccion);
    }

    // ── AC4: auditoría de acceso ─────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CadaLectura_QuedaAuditadaConActorAccionSolicitudYHora_SinElDato()
    {
        await IniciarAsync();
        var solicitud = await SembrarSolicitudAsync(_reloj.GetUtcNow());
        await EnScopeAsync(s => s.GuardarAsync(solicitud, Persona(), TestContext.Current.CancellationToken));

        _reloj.Avanzar(TimeSpan.FromMinutes(5));
        await EnScopeAsync(s => s.LeerAsync(solicitud, "operador:ana", TestContext.Current.CancellationToken));

        await using var db = NuevaDb();
        var auditoria = await db.Set<AccesoPiiLog>().AsNoTracking().SingleAsync(a => a.Accion == PersonaService.AccionLectura, TestContext.Current.CancellationToken);
        auditoria.Actor.Should().Be("operador:ana");
        auditoria.SolicitudId.Should().Be(solicitud);
        auditoria.OcurrioEn.Should().Be(_reloj.GetUtcNow());
        $"{auditoria.Actor}{auditoria.Accion}".Should().NotContainAny(Cedula, Nombre, Correo);

        var leer = async () => await EnScopeAsync(s => s.LeerAsync(solicitud, " ", TestContext.Current.CancellationToken));
        await leer.Should().ThrowAsync<ArgumentException>("una lectura sin actor no se permite");
    }

    // ── AC2: sin PII en logs ─────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task NingunLog_ContieneDatosPersonales_AunConNivelTrace()
    {
        await IniciarAsync(("Logging:LogLevel:Default", "Trace"));
        var solicitud = await SembrarSolicitudAsync(_reloj.GetUtcNow().AddDays(-60));
        await EnScopeAsync(s => s.GuardarAsync(solicitud, Persona(), TestContext.Current.CancellationToken));
        await EnScopeAsync(s => s.LeerAsync(solicitud, "operador:ana", TestContext.Current.CancellationToken));
        using (var scope = _app!.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<RetencionService>().AnonimizarAbandonadosAsync(TestContext.Current.CancellationToken);
        await _http.PostAsJsonAsync($"{SesionEndpoints.Base}/sesiones/reanudar", new ReanudarRequest("PN-X", Cedula), TestContext.Current.CancellationToken);

        _logs.Mensajes.Should().NotBeEmpty("la prueba solo vale si el servicio sí escribió logs");
        _logs.Mensajes.Should().NotContain(m => m.Contains(Cedula) || m.Contains(Nombre) || m.Contains(Correo) || m.Contains(Celular) || m.Contains(Direccion));
    }

    // ── AC3: retención ───────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task UnBorradorAbandonado_SeAnonimiza_YDejaTraza_ElRecienteNoSeToca()
    {
        await IniciarAsync();
        var viejo = await SembrarSolicitudAsync(_reloj.GetUtcNow().AddDays(-31));
        var reciente = await SembrarSolicitudAsync(_reloj.GetUtcNow().AddDays(-5));
        var enCola = await SembrarSolicitudAsync(_reloj.GetUtcNow().AddDays(-90), EstadoSolicitud.EnCola);
        foreach (var id in new[] { viejo, reciente, enCola })
            await EnScopeAsync(s => s.GuardarAsync(id, Persona(), TestContext.Current.CancellationToken));
        using (var scope = _app!.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<SesionService>().IniciarAsync(viejo, null, TestContext.Current.CancellationToken);

        var anonimizados = await EnRetencionAsync(r => r.AnonimizarAbandonadosAsync(TestContext.Current.CancellationToken));

        anonimizados.Should().Be(1);
        await using var db = NuevaDb();
        var persona = await db.Set<PersonaSolicitante>().AsNoTracking().SingleAsync(p => p.SolicitudId == viejo, TestContext.Current.CancellationToken);
        persona.DocumentoCifrado.Should().BeEmpty();
        persona.DocumentoHash.Should().BeEmpty();
        persona.NombreCifrado.Should().BeNull();
        persona.CorreoCifrado.Should().BeNull();
        persona.CelularCifrado.Should().BeNull();
        persona.DireccionCifrada.Should().BeNull();
        var solicitud = await db.Set<SolicitudTramite>().AsNoTracking().SingleAsync(s => s.Id == viejo, TestContext.Current.CancellationToken);
        solicitud.Estado.Should().Be(EstadoSolicitud.Cancelada, "ya no se puede reanudar");
        solicitud.CedulaSolicitanteHash.Should().BeEmpty();
        (await db.Set<SesionTramite>().SingleAsync(s => s.SolicitudId == viejo, TestContext.Current.CancellationToken)).Revocada.Should().BeTrue();
        (await db.Set<AccesoPiiLog>().SingleAsync(a => a.Accion == RetencionService.AccionAnonimizada, TestContext.Current.CancellationToken))
            .Should().Match<AccesoPiiLog>(a => a.SolicitudId == viejo && a.Actor == RetencionService.ActorRetencion);

        (await db.Set<PersonaSolicitante>().AsNoTracking().SingleAsync(p => p.SolicitudId == reciente, TestContext.Current.CancellationToken)).NombreCifrado.Should().NotBeNull();
        (await db.Set<PersonaSolicitante>().AsNoTracking().SingleAsync(p => p.SolicitudId == enCola, TestContext.Current.CancellationToken)).NombreCifrado
            .Should().NotBeNull("solo los borradores se anonimizan: una solicitud en cola es un trámite en curso");
    }

    [Fact]
    public async Task LaRetencion_EsIdempotente_Configurable_YSePuedeApagar()
    {
        await IniciarAsync();
        var reciente = await SembrarSolicitudAsync(_reloj.GetUtcNow().AddDays(-5));
        await EnScopeAsync(s => s.GuardarAsync(reciente, Persona(), TestContext.Current.CancellationToken));

        (await EnRetencionAsync(r => r.AnonimizarAbandonadosAsync(TestContext.Current.CancellationToken))).Should().Be(0, "30 días por defecto");

        await using (var db = NuevaDb())
            await db.Set<ConfiguracionRetencion>().ExecuteUpdateAsync(set => set.SetProperty(c => c.DiasRetencionBorrador, 3), TestContext.Current.CancellationToken);
        (await EnRetencionAsync(r => r.AnonimizarAbandonadosAsync(TestContext.Current.CancellationToken))).Should().Be(1, "el plazo sale de la base y aplica sin redeploy");
        (await EnRetencionAsync(r => r.AnonimizarAbandonadosAsync(TestContext.Current.CancellationToken))).Should().Be(0, "una solicitud ya anonimizada no vuelve a entrar");

        var otra = await SembrarSolicitudAsync(_reloj.GetUtcNow().AddDays(-40));
        await using (var db = NuevaDb())
            await db.Set<ConfiguracionRetencion>().ExecuteUpdateAsync(set => set.SetProperty(c => c.Habilitado, false), TestContext.Current.CancellationToken);
        (await EnRetencionAsync(r => r.AnonimizarAbandonadosAsync(TestContext.Current.CancellationToken))).Should().Be(0, "apagada no hace nada");
        otra.Should().NotBeEmpty();
    }

    [Fact]
    public async Task ElBorradorAnonimizado_NoSePuedeReanudar()
    {
        await IniciarAsync();
        var viejo = await SembrarSolicitudAsync(_reloj.GetUtcNow().AddDays(-31), codigo: "PN-2026-000777");
        await EnRetencionAsync(r => r.AnonimizarAbandonadosAsync(TestContext.Current.CancellationToken));

        using var peticion = new HttpRequestMessage(HttpMethod.Post, $"{SesionEndpoints.Base}/sesiones/reanudar") { Content = JsonContent.Create(new ReanudarRequest("PN-2026-000777", Cedula)) };
        peticion.Headers.Add(AbusoEndpoints.CabeceraCaptcha, CaptchaFalso.TokenValido);
        var respuesta = await _http.SendAsync(peticion, TestContext.Current.CancellationToken);

        respuesta.StatusCode.Should().Be(HttpStatusCode.NotFound);
        viejo.Should().NotBeEmpty();
    }

    [Fact]
    public async Task ElJobPeriodico_AnonimizaSoloAlArrancar_SinIntervencion()
    {
        Guid viejo = default;
        await IniciarAsync(("Retencion:ArranqueSegundos", "0"), antesDeArrancar: async () => viejo = await SembrarSolicitudAsync(_reloj.GetUtcNow().AddDays(-31)));

        var limite = DateTime.UtcNow.AddSeconds(15);
        EstadoSolicitudLeida estado;
        do
        {
            await Task.Delay(200, TestContext.Current.CancellationToken);
            await using var db = NuevaDb();
            estado = new((await db.Set<SolicitudTramite>().AsNoTracking().SingleAsync(s => s.Id == viejo, TestContext.Current.CancellationToken)).Estado);
        }
        while (estado.Valor != EstadoSolicitud.Cancelada && DateTime.UtcNow < limite);

        estado.Valor.Should().Be(EstadoSolicitud.Cancelada);
    }

    // ── AC5: llaves rotables y configuración ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ConUnaLlaveNueva_LoNuevoUsaLaNueva_YLoAnteriorSigueLegible()
    {
        var vieja = Cifrado(("1", Llave1));
        var antes = vieja.Cifrar(Correo, "persona.correo");

        var rotada = Cifrado(("1", Llave1), ("2", Llave2));
        var despues = rotada.Cifrar(Correo, "persona.correo");

        rotada.VersionActiva.Should().Be(2);
        antes![0].Should().Be(1);
        despues![0].Should().Be(2, "los datos nuevos usan la llave nueva");
        rotada.Descifrar(antes, "persona.correo").Should().Be(Correo, "los anteriores siguen legibles");
        rotada.Descifrar(despues, "persona.correo").Should().Be(Correo);
        Cifrado(("2", Llave2)).Invoking(c => c.Descifrar(antes, "persona.correo")).Should().Throw<CryptographicException>("sin la llave vieja no se abre");
    }

    [Fact]
    public void LaVersionActivaSePuedeFijarPorConfiguracion()
    {
        var c = Cifrado(new[] { ("1", Llave1), ("2", Llave2) }, activa: "1");

        c.VersionActiva.Should().Be(1, "permite desplegar la llave nueva antes de empezar a usarla");
    }

    [Fact]
    public void ElCifrado_EsAleatorio_Autenticado_YLigadoALaColumna()
    {
        var c = Cifrado(("1", Llave1));

        var a = c.Cifrar(Cedula, "persona.documento")!;
        var b = c.Cifrar(Cedula, "persona.documento")!;

        a.Should().NotEqual(b, "cada valor lleva su propio nonce");
        c.Descifrar(a, "persona.documento").Should().Be(Cedula);
        c.Invoking(x => x.Descifrar(a, "persona.correo")).Should().Throw<CryptographicException>("copiado a otra columna no se abre");
        var alterado = (byte[])a.Clone();
        alterado[^1] ^= 0xFF;
        c.Invoking(x => x.Descifrar(alterado, "persona.documento")).Should().Throw<CryptographicException>("si lo tocan, se detecta");
        c.Cifrar(null, "persona.correo").Should().BeNull();
        c.Descifrar(null, "persona.correo").Should().BeNull();
    }

    [Theory]
    [InlineData(null, "*Pii__Cifrado__Llaves__1*PERSONANATURAL_PII_AES_KEY*")]
    [InlineData("no-es-base64!!", "*base64 de exactamente 32 bytes*")]
    [InlineData("AAEC", "*base64 de exactamente 32 bytes*")]
    public void UnaLlaveFaltanteOMalFormada_ImpideArrancar(string? llave, string mensaje)
    {
        var datos = new Dictionary<string, string?>();
        if (llave is not null)
            datos["Pii:Cifrado:Llaves:1"] = llave;

        var act = () => new CifradoPii(new ConfigurationBuilder().AddInMemoryCollection(datos).Build());

        act.Should().Throw<InvalidOperationException>().WithMessage(mensaje);
    }

    [Fact]
    public void UnaVersionActivaSinLlave_ImpideArrancar()
    {
        var act = () => Cifrado(new[] { ("1", Llave1) }, activa: "9");

        act.Should().Throw<InvalidOperationException>().WithMessage("*VersionActiva*sin llave*");
    }

    // ── Infraestructura de la prueba ─────────────────────────────────────────────────────────────────────────────────

    private static DatosPersona Persona() => new("comprador", 1, "CC", Cedula, Nombre, Correo, Celular, Direccion);

    private static CifradoPii Cifrado(params (string Version, string Llave)[] llaves) => Cifrado(llaves, null);

    private static CifradoPii Cifrado((string Version, string Llave)[] llaves, string? activa)
    {
        var datos = llaves.ToDictionary(l => $"Pii:Cifrado:Llaves:{l.Version}", l => (string?)l.Llave);
        if (activa is not null)
            datos[CifradoPii.ClaveVersionActiva] = activa;
        return new CifradoPii(new ConfigurationBuilder().AddInMemoryCollection(datos).Build());
    }

    private PiiHasher Hasher() => _app!.Services.GetRequiredService<PiiHasher>();

    private async Task IniciarAsync(params (string Clave, string Valor)[] ajustes) => await IniciarAsync(ajustes, null);

    private async Task IniciarAsync((string Clave, string Valor) ajuste, Func<Task> antesDeArrancar) => await IniciarAsync([ajuste], antesDeArrancar);

    private async Task IniciarAsync((string Clave, string Valor)[] ajustes, Func<Task>? antesDeArrancar)
    {
        var server = Environment.GetEnvironmentVariable("ConnectionStrings__Core") ?? $"Host=127.0.0.1;Port=5432;Username={Environment.UserName}";
        _database = new NpgsqlConnectionStringBuilder(server) { Database = $"flit_pnpii_{Guid.NewGuid():N}"[..30] }.ConnectionString;
        try
        {
            await using var admin = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(server) { Database = "postgres" }.ConnectionString);
            await admin.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE DATABASE \"{new NpgsqlConnectionStringBuilder(_database).Database}\"", admin);
            await create.ExecuteNonQueryAsync();
        }
        catch (NpgsqlException ex) when (!string.Equals(Environment.GetEnvironmentVariable("CI"), "true", StringComparison.OrdinalIgnoreCase))
        {
            _database = string.Empty;
            Assert.Skip($"Postgres no alcanzable: {ex.Message}");
        }

        var settings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:Servicio"] = _database,
            ["Platform:ServiceClient:ClientSecret"] = "secreto-de-prueba",
            ["Platform:ServiceClient:TokenEndpoint"] = "http://core-identity/connect/token",
            ["Platform:Auth:JwksUri"] = "http://core-identity/.well-known/jwks.json",
            ["Platform:Auth:Issuers:0"] = "https://hub.prueba/",
            ["Platform:Messaging:ConnectionString"] = "amqp://flit:x@127.0.0.1:1/",
            [ServicioSettings.GrpcPortKey] = "5996",
            ["Pii:ClaveHmac"] = "clave-de-prueba-de-al-menos-32-caracteres!",
            ["Pii:Cifrado:Llaves:1"] = Llave1,
            ["Captcha:SecretKey"] = "secreto-de-captcha",
        };
        foreach (var (clave, valor) in ajustes)
            settings[clave] = valor;

        _app = Program.Build([], b =>
        {
            b.WebHost.UseTestServer();
            b.Configuration.AddInMemoryCollection(settings);
            b.Services.AddSingleton<TimeProvider>(_reloj);
            b.Services.AddSingleton<IProveedorCaptcha, CaptchaFalso>();
            b.Logging.AddProvider(_logs);
        });
        await Program.MigrateAsync(_app);
        if (antesDeArrancar is not null)
            await antesDeArrancar();
        await _app.StartAsync();
        _http = _app.GetTestClient();
    }

    private async Task<Guid> SembrarSolicitudAsync(DateTimeOffset ultimaActividad, string estado = EstadoSolicitud.Borrador, string? codigo = null)
    {
        await using var db = NuevaDb();
        var id = Guid.CreateVersion7();
        db.Add(new SolicitudTramite
        {
            Id = id,
            TenantId = PlatformTenantsFicticio,
            CodigoPublico = codigo ?? $"PN-{Guid.NewGuid():N}"[..24],
            TipoTramiteCodigo = "TRASPASO_STANDARD",
            DefinicionVersion = 1,
            OrganismoId = Guid.NewGuid(),
            OrganismoNombre = "Organismo de prueba",
            Placa = "ABC123",
            Estado = estado,
            CedulaSolicitanteHash = Hasher().HashCedula(Cedula),
            CorrelationId = Guid.NewGuid(),
            CreatedAt = ultimaActividad,
            UpdatedAt = ultimaActividad,
        });
        await db.SaveChangesAsync();
        return id;
    }

    private static readonly Guid PlatformTenantsFicticio = Guid.Parse("00000000-0000-0000-0000-0000000f1171");

    private async Task<T> EnScopeAsync<T>(Func<PersonaService, Task<T>> accion)
    {
        using var scope = _app!.Services.CreateScope();
        return await accion(scope.ServiceProvider.GetRequiredService<PersonaService>());
    }

    private async Task<T> EnRetencionAsync<T>(Func<RetencionService, Task<T>> accion)
    {
        using var scope = _app!.Services.CreateScope();
        return await accion(scope.ServiceProvider.GetRequiredService<RetencionService>());
    }

    private PersonaNaturalDb NuevaDb()
    {
        var options = new DbContextOptionsBuilder<PersonaNaturalDb>();
        PersonaNaturalDb.Configure(options, _database);
        return new PersonaNaturalDb(options.Options);
    }

    private readonly record struct EstadoSolicitudLeida(string Valor);

    private sealed class RegistroDeLogs : ILoggerProvider
    {
        private readonly System.Collections.Concurrent.ConcurrentQueue<string> _mensajes = new();

        public IReadOnlyCollection<string> Mensajes => _mensajes.ToArray();

        public ILogger CreateLogger(string categoryName) => new Registro(_mensajes);

        public void Dispose()
        {
        }

        private sealed class Registro(System.Collections.Concurrent.ConcurrentQueue<string> destino) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
                destino.Enqueue(formatter(state, exception) + (exception?.ToString() ?? string.Empty) + string.Join(';', (state as IEnumerable<KeyValuePair<string, object?>>)?.Select(p => $"{p.Key}={p.Value}") ?? []));
        }
    }

    private sealed class RelojFalso(DateTimeOffset inicio) : TimeProvider
    {
        private DateTimeOffset _ahora = inicio;

        public override DateTimeOffset GetUtcNow() => _ahora;

        public void Avanzar(TimeSpan tiempo) => _ahora += tiempo;
    }
}
