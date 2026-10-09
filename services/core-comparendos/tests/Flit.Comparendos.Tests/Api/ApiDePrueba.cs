using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Flit.Comparendos.Api;
using Flit.Comparendos.Api.Persistence;
using Flit.Platform.Sdk.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using Xunit;

namespace Flit.Comparendos.Tests.Api;

/// <summary>Quién llama en una prueba: empresa, permisos y si es SuperAdmin. Se arma un token de producto con eso.</summary>
public sealed record UsuarioDePrueba(Guid Empresa, IReadOnlyList<string> Permisos, bool SuperAdmin = false, string Email = "operador@empresa.test")
{
    public Guid Id { get; init; } = Guid.NewGuid();
}

/// <summary>Reloj fijo que las pruebas mueven a voluntad (hoy en Colombia = la fecha dada a las 9:00).</summary>
public sealed class RelojDePrueba : TimeProvider
{
    public DateTimeOffset Ahora { get; set; } = new(2026, 10, 9, 14, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => Ahora;

    public void Hoy(DateOnly dia) => Ahora = new DateTimeOffset(dia.ToDateTime(new TimeOnly(9, 0)), TimeSpan.FromHours(-5)).ToUniversalTime();
}

/// <summary>
/// La API de Comparendos de verdad (Program.Build) sobre un Postgres real con una base efímera, tokens de producto
/// firmados con una llave propia servida como JWKS, y los reemplazos que pida cada clase de prueba. Sin Postgres la
/// prueba se omite en local y falla en CI (mismo criterio que los demás servicios).
/// </summary>
public class ApiDePrueba : IAsyncLifetime
{
    public const string Issuer = "https://hub.prueba/";
    private const int GrpcPort = 5998;
    private readonly RsaSecurityKey _llave = new(RSA.Create(2048)) { KeyId = "prueba-comparendos" };
    private string _database = string.Empty;

    public string? Omitir { get; private set; }
    public WebApplication App { get; private set; } = null!;
    public RelojDePrueba Reloj { get; } = new();

    /// <summary>Reemplazos de servicios que agregan las clases derivadas (fuentes simuladas, correo, etc.).</summary>
    protected virtual void Reemplazar(IServiceCollection services)
    {
    }

    protected virtual IDictionary<string, string?> Configuracion() => new Dictionary<string, string?>();

    public async ValueTask InitializeAsync()
    {
        var server = Environment.GetEnvironmentVariable("ConnectionStrings__Core") ?? $"Host=127.0.0.1;Port=5432;Username={Environment.UserName}";
        _database = new NpgsqlConnectionStringBuilder(server) { Database = $"flit_cmp_{Guid.NewGuid():N}"[..30] }.ConnectionString;
        try
        {
            await using var admin = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(server) { Database = "postgres" }.ConnectionString);
            await admin.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE DATABASE \"{new NpgsqlConnectionStringBuilder(_database).Database}\"", admin);
            await create.ExecuteNonQueryAsync();
        }
        catch (NpgsqlException ex) when (!string.Equals(Environment.GetEnvironmentVariable("CI"), "true", StringComparison.OrdinalIgnoreCase))
        {
            Omitir = $"Postgres no alcanzable: {ex.Message}";
            return;
        }

        var settings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:Servicio"] = _database,
            ["Platform:ServiceClient:ClientSecret"] = "secreto",
            ["Platform:ServiceClient:TokenEndpoint"] = "http://core-identity/connect/token",
            ["Platform:Auth:JwksUri"] = "http://core-identity/.well-known/jwks.json",
            ["Platform:Auth:Issuers:0"] = Issuer,
            ["Platform:Messaging:ConnectionString"] = "amqp://flit:x@127.0.0.1:1/",
            ["Comparendos:Plataforma:RegistrarManifiesto"] = "false",
            ["Comparendos:Trabajos:Habilitados"] = "false",
            [ServicioSettings.GrpcPortKey] = GrpcPort.ToString(System.Globalization.CultureInfo.InvariantCulture),
        };
        foreach (var (k, v) in Configuracion())
            settings[k] = v;

        App = Program.Build([], b =>
        {
            b.WebHost.UseTestServer();
            b.Configuration.AddInMemoryCollection(settings);
            b.Services.AddHttpClient(PlatformAuthenticationExtensions.JwksHttpClientName).ConfigurePrimaryHttpMessageHandler(() => new Jwks(_llave));
        }, s =>
        {
            s.AddSingleton<TimeProvider>(Reloj);
            Reemplazar(s);
        });
        await Program.MigrateAsync(App);
        await App.StartAsync();
    }

    public async ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        if (Omitir is not null)
            return;

        await App.DisposeAsync();
        NpgsqlConnection.ClearAllPools();
        var server = new NpgsqlConnectionStringBuilder(_database) { Database = "postgres" }.ConnectionString;
        await using var admin = new NpgsqlConnection(server);
        await admin.OpenAsync();
        await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{new NpgsqlConnectionStringBuilder(_database).Database}\" WITH (FORCE)", admin);
        await drop.ExecuteNonQueryAsync();
    }

    public HttpClient Cliente(UsuarioDePrueba usuario)
    {
        ArgumentNullException.ThrowIfNull(usuario);
        var client = App.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token(usuario));
        return client;
    }

    public HttpClient Anonimo() => App.GetTestClient();

    public async Task EnDbAsync(Func<ComparendosDb, Task> accion)
    {
        ArgumentNullException.ThrowIfNull(accion);
        await using var scope = App.Services.CreateAsyncScope();
        await accion(scope.ServiceProvider.GetRequiredService<ComparendosDb>());
    }

    public async Task<T> EnDbAsync<T>(Func<ComparendosDb, Task<T>> consulta)
    {
        ArgumentNullException.ThrowIfNull(consulta);
        await using var scope = App.Services.CreateAsyncScope();
        return await consulta(scope.ServiceProvider.GetRequiredService<ComparendosDb>());
    }

    public string Token(UsuarioDePrueba usuario)
    {
        ArgumentNullException.ThrowIfNull(usuario);
        var claims = new List<Claim>
        {
            new("sub", usuario.Id.ToString()),
            new("email", usuario.Email),
            new("tenant_id", usuario.Empresa.ToString()),
            new("company_name", "Empresa de prueba"),
            new("role_code", usuario.SuperAdmin ? "SuperAdmin" : "admin_comparendos"),
            new("permissions", JsonSerializer.Serialize(usuario.Permisos), JsonClaimValueTypes.JsonArray),
        };
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = "comparendos",
            Subject = new ClaimsIdentity(claims),
            Expires = DateTime.UtcNow.AddMinutes(30),
            SigningCredentials = new SigningCredentials(_llave, SecurityAlgorithms.RsaSha256),
        });
    }

    private sealed class Jwks(RsaSecurityKey key) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var jwk = JsonWebKeyConverter.ConvertFromRSASecurityKey(new RsaSecurityKey(key.Rsa.ExportParameters(false)) { KeyId = key.KeyId });
            jwk.Use = "sig";
            jwk.Alg = SecurityAlgorithms.RsaSha256;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new { keys = new[] { jwk } })) });
        }
    }
}
