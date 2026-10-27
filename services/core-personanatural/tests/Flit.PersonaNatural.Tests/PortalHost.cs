using System.Net.Http.Json;
using System.Text.Json;
using Flit.PersonaNatural.Api;
using Flit.PersonaNatural.Api.Abuso;
using Flit.PersonaNatural.Api.Cotizaciones;
using Flit.PersonaNatural.Api.Definiciones;
using Flit.PersonaNatural.Api.Persistence;
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

/// <summary>Reloj de pruebas que se mueve a mano.</summary>
internal sealed class RelojDePrueba(DateTimeOffset inicio) : TimeProvider
{
    private DateTimeOffset _ahora = inicio;

    public override DateTimeOffset GetUtcNow() => _ahora;

    public void Avanzar(TimeSpan tiempo) => _ahora += tiempo;
}

/// <summary>Fachada de definiciones de prueba: devuelve la definición que se le asigne y cuenta las llamadas.</summary>
internal sealed class FachadaDefinicionesFalsa : IFachadaDefiniciones
{
    public Dictionary<string, DefinicionTipoDto> PorCodigo { get; } = new(StringComparer.OrdinalIgnoreCase);
    public int Llamadas;
    public string? UltimoOrganismo;
    public string? ErrorAlPedir;

    public Task<DefinicionTipoDto> ObtenerDefinicionAsync(string codigo, string? organismoId, CancellationToken ct)
    {
        Interlocked.Increment(ref Llamadas);
        UltimoOrganismo = organismoId;
        if (ErrorAlPedir is { } e)
            throw new FachadaException(e, "error de prueba", e == "organismo_no_habilitado" ? 422 : 404);
        return PorCodigo.TryGetValue(codigo, out var d)
            ? Task.FromResult(d)
            : throw new FachadaException("tipo_no_disponible", "no existe", 404);
    }
}

/// <summary>Definiciones de ejemplo con la forma real de lo que entrega core-api (traspaso, un solo titular).</summary>
internal static class DefinicionesDePrueba
{
    public static DefinicionTipoDto Traspaso(int version = 1, string huella = "h1") => new(
        "TRASPASO_STANDARD", "Traspaso", "TRASPASO", version, huella, true, string.Empty,
        [
            new PasoDto("consulta", "Consulta", ["vehicle_query"], []),
            new PasoDto("actores", "Partes", ["actor_form", "biometric"], []),
            new PasoDto("datos", "Datos", ["generic_form"],
            [
                new CampoDto("km", "Kilometraje", "number", true, [], "{}"),
                new CampoDto("uso", "Uso", "select", false, ["particular", "publico"], "{}"),
                new CampoDto("fecha_venta", "Fecha de venta", "date", false, [], "{}"),
                new CampoDto("acepta", "Acepta", "checkbox", false, [], "{}"),
            ]),
            new PasoDto("documentos", "Documentos", ["document_checklist"], []),
        ],
        [
            new ActorRolDto("vendedor", "Vendedor", 1, 1, true, true, true),
            new ActorRolDto("comprador", "Comprador", 1, 4, true, true, true),
        ],
        [
            new DocumentoRequeridoDto("compraventa", "Contrato de compraventa", true, 1, "Suba el contrato firmado.", string.Empty),
            new DocumentoRequeridoDto("soat", "SOAT", true, 2, string.Empty, string.Empty),
            new DocumentoRequeridoDto("rtm", "Revisión técnico-mecánica", false, 3, string.Empty, string.Empty),
            new DocumentoRequeridoDto("poder_tramitador", "Poder del tramitador", true, 4, string.Empty, "Se pide si el trámite lo gestiona un tramitador."),
        ],
        new FlagsTipoDto("PLATE", true, false, true, false, true, false));

    public static DefinicionTipoDto UnTitular(string codigo = "CAMBIO_COLOR", string huella = "t1") => new(
        codigo, "Cambio de color", "OTROS", 1, huella, true, string.Empty,
        [new PasoDto("datos", "Datos", ["generic_form"], [new CampoDto("color_nuevo", "Color nuevo", "text", true, [], "{}")])],
        [new ActorRolDto("comprador", "Titular", 1, 1, true, true, true)],
        [new DocumentoRequeridoDto("tarjeta_propiedad", "Tarjeta de propiedad", true, 1, string.Empty, string.Empty)],
        new FlagsTipoDto("PLATE", false, false, false, false, true, false));

    public static DefinicionTipoDto ConPrenda() => UnTitular("TIPO_PRENDA", "p1") with
    {
        Flags = new FlagsTipoDto("PLATE", false, true, false, true, false, false),
    };
}


/// <summary>
/// Host real del portal sobre una base efímera propia y las fachadas de core-api falsas (sin motor de Postgres: se omite en
/// local y falla en CI, como el resto de pruebas del servicio).
/// </summary>
internal sealed class PortalHost : IAsyncDisposable
{
    public const string ClaveHmac = "clave-de-prueba-de-al-menos-32-caracteres!";
    public const string Organismo = CotizacionesTests.Organismo;
    public const string Cedula = "1020456789";

    private string _database = string.Empty;

    public WebApplication App { get; private set; } = null!;

    public HttpClient Http { get; private set; } = null!;

    public RelojDePrueba Reloj { get; } = new(new DateTimeOffset(2026, 10, 27, 15, 0, 0, TimeSpan.Zero));

    public FachadaDefinicionesFalsa Definiciones { get; } = new();

    public FachadaFalsa Catalogo { get; } = new();

    public RegistroDeLogs Logs { get; } = new();

    /// <summary>Arma el host. <paramref name="servicios"/> reemplaza o suma servicios de prueba antes de construirlo.</summary>
    public static async Task<PortalHost> IniciarAsync(
        string prefijo, Action<IServiceCollection, PortalHost>? servicios = null, params (string Clave, string Valor)[] ajustes)
    {
        var host = new PortalHost();
        var server = Environment.GetEnvironmentVariable("ConnectionStrings__Core") ?? $"Host=127.0.0.1;Port=5432;Username={Environment.UserName}";
        host._database = new NpgsqlConnectionStringBuilder(server) { Database = $"flit_{prefijo}_{Guid.NewGuid():N}"[..30] }.ConnectionString;
        try
        {
            await using var admin = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(server) { Database = "postgres" }.ConnectionString);
            await admin.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE DATABASE \"{new NpgsqlConnectionStringBuilder(host._database).Database}\"", admin);
            await create.ExecuteNonQueryAsync();
        }
        catch (NpgsqlException ex) when (!string.Equals(Environment.GetEnvironmentVariable("CI"), "true", StringComparison.OrdinalIgnoreCase))
        {
            Assert.Skip($"Postgres no alcanzable: {ex.Message}");
            throw;
        }

        var settings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:Servicio"] = host._database,
            ["Platform:ServiceClient:ClientSecret"] = "secreto-de-prueba",
            ["Platform:ServiceClient:TokenEndpoint"] = "http://core-identity/connect/token",
            ["Platform:Auth:JwksUri"] = "http://core-identity/.well-known/jwks.json",
            ["Platform:Auth:Issuers:0"] = "https://hub.prueba/",
            ["Platform:Messaging:ConnectionString"] = "amqp://flit:x@127.0.0.1:1/",
            [ServicioSettings.GrpcPortKey] = "5995",
            ["Pii:ClaveHmac"] = ClaveHmac,
            ["Captcha:SecretKey"] = "secreto-de-captcha",
            ["Pii:Cifrado:Llaves:1"] = "AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8=",
            ["Abuso:LimiteIpPorMinuto"] = "10000",
        };
        foreach (var (clave, valor) in ajustes)
            settings[clave] = valor;

        host.App = Program.Build([], b =>
        {
            b.WebHost.UseTestServer();
            b.Configuration.AddInMemoryCollection(settings);
            b.Services.AddSingleton<TimeProvider>(host.Reloj);
            b.Services.AddSingleton<IProveedorCaptcha, CaptchaFalso>();
            b.Services.AddSingleton<IFachadaTramites>(host.Catalogo);
            b.Services.AddSingleton<IFachadaDefiniciones>(host.Definiciones);
            b.Logging.AddProvider(host.Logs);
            servicios?.Invoke(b.Services, host);
        });
        await host.App.StartAsync();
        await Program.MigrateAsync(host.App);
        host.Http = host.App.GetTestClient();
        return host;
    }

    public PersonaNaturalDb NuevaDb()
    {
        var options = new DbContextOptionsBuilder<PersonaNaturalDb>();
        PersonaNaturalDb.Configure(options, _database);
        return new PersonaNaturalDb(options.Options);
    }

    public async Task<T> EnScopeAsync<T>(Func<IServiceProvider, Task<T>> accion)
    {
        await using var scope = App.Services.CreateAsyncScope();
        return await accion(scope.ServiceProvider);
    }

    /// <summary>Envía una petición JSON con captcha opcional, sesión opcional y cabeceras extra.</summary>
    public async Task<HttpResponseMessage> EnviarAsync(
        HttpMethod metodo, string ruta, object? cuerpo = null, string? token = null, bool captcha = false, params (string Nombre, string Valor)[] cabeceras)
    {
        using var peticion = new HttpRequestMessage(metodo, ruta);
        if (cuerpo is not null)
            peticion.Content = JsonContent.Create(cuerpo);
        if (token is not null)
            peticion.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        if (captcha)
            peticion.Headers.Add(AbusoEndpoints.CabeceraCaptcha, CaptchaFalso.TokenValido);
        foreach (var (nombre, valor) in cabeceras)
            peticion.Headers.Add(nombre, valor);
        return await Http.SendAsync(peticion, TestContext.Current.CancellationToken);
    }

    public static async Task<JsonDocument> JsonAsync(HttpResponseMessage r) =>
        JsonDocument.Parse(await r.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

    public static async Task<string?> CodigoAsync(HttpResponseMessage r)
    {
        using var json = await JsonAsync(r);
        return json.RootElement.TryGetProperty("code", out var c) ? c.GetString() : null;
    }

    public async ValueTask DisposeAsync()
    {
        if (App is not null)
            await App.DisposeAsync();
        NpgsqlConnection.ClearAllPools();
        var options = new DbContextOptionsBuilder<PersonaNaturalDb>();
        PersonaNaturalDb.Configure(options, _database);
        await using var db = new PersonaNaturalDb(options.Options);
        await db.Database.EnsureDeletedAsync();
    }
}
