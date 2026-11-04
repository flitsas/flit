using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Flit.PersonaNatural.Api;
using Flit.PersonaNatural.Api.Abuso;
using Flit.PersonaNatural.Api.Cotizaciones;
using Flit.PersonaNatural.Api.Identidad;
using Flit.PersonaNatural.Api.Persistence;
using Flit.PersonaNatural.Api.Pii;
using Flit.PersonaNatural.Api.Sesiones;
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
/// Fachada de identidad de prueba: emula la idempotencia de core-api (una validación por solicitud, parte y documento; una
/// rechazada o vencida se reemplaza), lleva la cuenta de lo pedido y puede «caerse» o perder una respuesta ya creada.
/// </summary>
internal sealed class FachadaIdentidadFalsa : IFachadaIdentidad
{
    public bool Caida;
    public bool PerderLaProximaRespuesta;
    public int Llamadas;
    public int ValidacionesCreadas;
    public int Reenvios;
    public List<string?> CorreosDeReenvio { get; } = [];
    public List<(Guid Solicitud, string Parte, string Documento, string Correo)> Pedidos { get; } = [];
    public Dictionary<Guid, string> Estados { get; } = [];
    private readonly Dictionary<(Guid Solicitud, string Parte, string Documento), Guid> _creadas = [];

    public Task<ValidacionDeFlit2> SolicitarAsync(Guid solicitudId, string parte, PersonaParaValidar persona, CancellationToken ct)
    {
        Llamadas++;
        if (Caida)
            throw Caido();
        Pedidos.Add((solicitudId, parte, persona.Documento, persona.Correo));
        var clave = (solicitudId, parte, persona.Documento);
        if (!_creadas.TryGetValue(clave, out var id) || Estados[id] is EstadosIdentidad.Rechazada or EstadosIdentidad.Vencida)
        {
            id = Guid.NewGuid();
            _creadas[clave] = id;
            Estados[id] = EstadosIdentidad.Pendiente;
            ValidacionesCreadas++;
        }

        if (PerderLaProximaRespuesta)
        {
            PerderLaProximaRespuesta = false;
            throw Caido();
        }

        return Task.FromResult(new ValidacionDeFlit2(id, Estados[id]));
    }

    public Task<string> ReenviarAsync(Guid validacionId, string? correoNuevo, CancellationToken ct)
    {
        Llamadas++;
        if (Caida)
            throw Caido();
        Reenvios++;
        CorreosDeReenvio.Add(correoNuevo);
        Estados[validacionId] = EstadosIdentidad.Pendiente;
        return Task.FromResult(EstadosIdentidad.Pendiente);
    }

    public Task<string> ObtenerEstadoAsync(Guid validacionId, CancellationToken ct)
    {
        if (Caida)
            throw Caido();
        return Estados.TryGetValue(validacionId, out var estado) ? Task.FromResult(estado) : throw new FachadaException("validacion_no_encontrada", "No existe", 404);
    }

    private static FachadaException Caido() => new(FachadaIdentidadGrpc.ServicioNoDisponible, "FLIT 2 no responde", 503);
}

/// <summary>Definición congelada configurable: la forma del diseño §6.2 (<c>actores[]</c> con <c>rol</c>, <c>min</c> y <c>biometria</c>).</summary>
internal sealed class ReglasFalsas : IReglasDeIdentidad
{
    public static readonly string SnapshotTraspaso = """
        {"codigo":"TRASPASO_STANDARD","actores":[
          {"rol":"vendedor","min":1,"max":1,"biometria":true,"firma":true,"personaNaturalPermitida":true},
          {"rol":"comprador","min":1,"max":1,"biometria":true,"firma":true,"personaNaturalPermitida":true},
          {"rol":"testigo","min":0,"max":1,"biometria":false,"firma":false,"personaNaturalPermitida":true}]}
        """;

    public Task<ReglasDeLaSolicitud?> LeerAsync(Guid solicitudId, CancellationToken ct) =>
        Task.FromResult<ReglasDeLaSolicitud?>(ReglasDeIdentidadDesdeSnapshot.Interpretar(SnapshotTraspaso));
}

internal sealed class RelojDeIdentidad(DateTimeOffset inicio) : TimeProvider
{
    private DateTimeOffset _ahora = inicio;

    public override DateTimeOffset GetUtcNow() => _ahora;

    public void Avanzar(TimeSpan tiempo) => _ahora += tiempo;
}

/// <summary>
/// Host real con base efímera propia y la fachada, el reloj y el captcha falsos, más lo necesario para sembrar solicitudes con
/// sus personas y abrir su sesión. Sin motor Postgres se omite en local y falla en CI (como el resto de las pruebas del servicio).
/// </summary>
public abstract class IdentidadBase : IAsyncLifetime
{
    protected const string Base = "/api/v1/persona-natural";
    protected const string Cedula = "1012345678";
    protected const string CedulaOtra = "52987654";
    protected const string Correo = "ciudadano.secreto@example.org";
    protected const string Nombre = "NOMBRE FICTICIO SECRETO";
    protected const string Celular = "3001234567";

    internal readonly FachadaIdentidadFalsa Fachada = new();
    internal readonly RelojDeIdentidad Reloj = new(new DateTimeOffset(2026, 10, 27, 15, 0, 0, TimeSpan.Zero));
    internal readonly RegistroDeLogs Logs = new();
    private string _database = string.Empty;
    private string? _skip;
    protected WebApplication App { get; private set; } = null!;
    protected HttpClient Http { get; private set; } = null!;

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        if (App is not null)
            await App.DisposeAsync();
        if (_skip is null && _database.Length > 0)
        {
            NpgsqlConnection.ClearAllPools();
            var options = new DbContextOptionsBuilder<PersonaNaturalDb>();
            PersonaNaturalDb.Configure(options, _database);
            await using var db = new PersonaNaturalDb(options.Options);
            await db.Database.EnsureDeletedAsync();
        }
    }

    protected async Task<bool> IniciarAsync(bool reglasFalsas = false)
    {
        var server = Environment.GetEnvironmentVariable("ConnectionStrings__Core") ?? $"Host=127.0.0.1;Port=5432;Username={Environment.UserName}";
        _database = new NpgsqlConnectionStringBuilder(server) { Database = $"flit_pnid_{Guid.NewGuid():N}"[..30] }.ConnectionString;
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
            ["Abuso:LimiteIpPorMinuto"] = "1000",
            // El job de reintento no corre solo en las pruebas: se invoca el servicio directo.
            ["Identidad:ArranqueSegundos"] = "3600",
            ["Identidad:ReintentoSegundos"] = "1",
        };

        App = Program.Build([], b =>
        {
            b.WebHost.UseTestServer();
            b.Configuration.AddInMemoryCollection(settings);
            b.Services.AddSingleton<TimeProvider>(Reloj);
            b.Services.AddSingleton<IProveedorCaptcha, CaptchaFalso>();
            b.Services.AddSingleton<IFachadaIdentidad>(Fachada);
            if (reglasFalsas)
                b.Services.AddScoped<IReglasDeIdentidad, ReglasFalsas>();
            b.Logging.AddProvider(Logs);
        });
        await App.StartAsync();
        await Program.MigrateAsync(App);
        Http = App.GetTestClient();
        return true;
    }

    /// <summary>Solicitud de traspaso con vendedor y comprador (con identidad) y su sesión abierta.</summary>
    protected async Task<(Guid Solicitud, string Token)> SembrarAsync(
        string estado = EstadoSolicitud.Borrador,
        string cedulaVendedor = Cedula,
        string cedulaComprador = CedulaOtra,
        string tipoDocumento = "CC",
        bool conPersonas = true,
        string? snapshot = null)
    {
        var ct = TestContext.Current.CancellationToken;
        using var scope = App.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PersonaNaturalDb>();
        var solicitud = new SolicitudTramite
        {
            Id = Guid.CreateVersion7(),
            TenantId = Guid.NewGuid(),
            CodigoPublico = $"PN-2026-{Guid.NewGuid():N}"[..20],
            TipoTramiteCodigo = "TRASPASO_STANDARD",
            DefinicionVersion = 1,
            DefinicionSnapshot = snapshot ?? ReglasFalsas.SnapshotTraspaso,
            OrganismoId = Guid.NewGuid(),
            OrganismoNombre = "Organismo de prueba",
            Placa = "ABC123",
            Estado = estado,
            CedulaSolicitanteHash = scope.ServiceProvider.GetRequiredService<PiiHasher>().HashCedula(cedulaComprador),
            CorrelationId = Guid.NewGuid(),
        };
        db.Add(solicitud);
        await db.SaveChangesAsync(ct);

        if (conPersonas)
        {
            var personas = scope.ServiceProvider.GetRequiredService<PersonaService>();
            await personas.GuardarAsync(solicitud.Id, new DatosPersona("vendedor", 1, tipoDocumento, cedulaVendedor, Nombre, Correo, Celular, null), ct);
            await personas.GuardarAsync(solicitud.Id, new DatosPersona("comprador", 1, "CC", cedulaComprador, Nombre, Correo, Celular, null), ct);
        }

        var sesion = await scope.ServiceProvider.GetRequiredService<SesionService>().IniciarAsync(solicitud.Id, null, ct);
        return (solicitud.Id, sesion.Token);
    }

    protected async Task<HttpResponseMessage> PostAsync(string ruta, string? token, object? cuerpo = null)
    {
        using var peticion = new HttpRequestMessage(HttpMethod.Post, ruta);
        if (cuerpo is not null)
            peticion.Content = JsonContent.Create(cuerpo);
        if (token is not null)
            peticion.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await Http.SendAsync(peticion, TestContext.Current.CancellationToken);
    }

    protected async Task<HttpResponseMessage> GetAsync(string ruta, string? token)
    {
        using var peticion = new HttpRequestMessage(HttpMethod.Get, ruta);
        if (token is not null)
            peticion.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await Http.SendAsync(peticion, TestContext.Current.CancellationToken);
    }

    protected Task<HttpResponseMessage> SolicitarAsync(Guid solicitud, string token, string parte, object? cuerpo = null) =>
        PostAsync($"{Base}/solicitudes/{solicitud}/identidad/{parte}", token, cuerpo);

    protected Task<HttpResponseMessage> ReenviarAsync(Guid solicitud, string token, string parte, object? cuerpo = null) =>
        PostAsync($"{Base}/solicitudes/{solicitud}/identidad/{parte}/reenviar", token, cuerpo);

    protected static async Task<JsonElement> Json(HttpResponseMessage respuesta) =>
        await respuesta.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);

    protected static async Task<string?> CodigoAsync(HttpResponseMessage respuesta)
    {
        var cuerpo = await Json(respuesta);
        return cuerpo.TryGetProperty("code", out var codigo) ? codigo.GetString() : null;
    }

    protected async Task<PersonaSolicitante> PersonaAsync(Guid solicitud, string parte)
    {
        using var scope = App.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PersonaNaturalDb>();
        return await db.Set<PersonaSolicitante>().AsNoTracking().SingleAsync(p => p.SolicitudId == solicitud && p.Parte == parte, TestContext.Current.CancellationToken);
    }

    /// <summary>Deja la persona en el estado pedido, como lo haría el espejo.</summary>
    protected async Task PonerEstadoAsync(Guid solicitud, string parte, string estado, Guid? validacionId = null, short? reenvios = null)
    {
        using var scope = App.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PersonaNaturalDb>();
        var persona = await db.Set<PersonaSolicitante>().SingleAsync(p => p.SolicitudId == solicitud && p.Parte == parte, TestContext.Current.CancellationToken);
        persona.ValidacionEstado = estado;
        persona.ValidacionId = validacionId ?? (estado == EstadosIdentidad.SinSolicitar ? null : Guid.NewGuid());
        if (reenvios is { } r)
            persona.Reenvios = r;
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    protected async Task<T> EnScopeAsync<T>(Func<IServiceProvider, Task<T>> accion)
    {
        using var scope = App.Services.CreateScope();
        return await accion(scope.ServiceProvider);
    }

    protected async Task<List<AuditoriaIdentidad>> AuditoriaAsync(Guid solicitud)
    {
        using var scope = App.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PersonaNaturalDb>();
        return await db.Set<AuditoriaIdentidad>().AsNoTracking().Where(a => a.SolicitudId == solicitud).OrderBy(a => a.Id).ToListAsync(TestContext.Current.CancellationToken);
    }
}
