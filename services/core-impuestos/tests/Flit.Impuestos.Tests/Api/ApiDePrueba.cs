using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Flit.Impuestos.Api;
using Flit.Impuestos.Api.Persistence;
using Flit.Impuestos.Api.Plataforma;
using Flit.Impuestos.Tests.Seguridad;
using Flit.Platform.Sdk.Authentication;
using Flit.Platform.Sdk.Tenancy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Npgsql;

namespace Flit.Impuestos.Tests.Api;

/// <summary>
/// La API de Impuestos de verdad (<c>Program.Build</c>) con tokens de producto firmados con una llave propia servida como
/// JWKS. Con base: una base efímera en el Postgres de <c>ConnectionStrings__Core</c> (en local la 5433), migrada como al
/// arrancar; sin Postgres se omite en local y falla en CI. Sin base: la cadena apunta a un puerto cerrado, para probar lo que
/// no lee la base. La suscripción es un doble (<see cref="ProductosHabilitadosFijo"/>) que la prueba enciende o apaga.
/// </summary>
/// <example>
/// <code>
/// await using var api = await ApiDePrueba.ConBaseAsync();
/// Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
/// var respuesta = await api.Cliente(api.Token(permisos: [Permisos.LiquidacionesVer])).GetAsync(Program.Prefijo + "/sesion");
/// </code>
/// </example>
public sealed class ApiDePrueba : IAsyncDisposable
{
    public const string Issuer = "https://hub.prueba/";
    public const int GrpcPort = 5996;

    /// <summary>Postgres inalcanzable: puerto 1 en loopback, con tiempo de espera corto.</summary>
    public const string BaseInalcanzable = "Host=127.0.0.1;Port=1;Database=impuestos_ausente;Username=nadie;Password=nada;Timeout=2";

    private readonly RsaSecurityKey _llave = new(RSA.Create(2048)) { KeyId = "prueba-impuestos" };
    private readonly Action<WebApplicationBuilder>? _configurar;
    private bool _baseCreada;

    private ApiDePrueba(string baseDeDatos, Action<WebApplicationBuilder>? configurar)
    {
        BaseDeDatos = baseDeDatos;
        _configurar = configurar;
    }

    /// <summary>Suscripción de prueba: toda compañía con Impuestos activo salvo que la prueba la apague.</summary>
    public ProductosHabilitadosFijo Productos { get; } = new();

    /// <summary>Motivo para omitir la prueba (sin Postgres en local); null si la API está arriba.</summary>
    public string? Omitir { get; private set; }

    public WebApplication App { get; private set; } = null!;

    public string BaseDeDatos { get; }

    /// <summary>Crea una base efímera, arranca la API y aplica sus migraciones como al arrancar.</summary>
    public static async Task<ApiDePrueba> ConBaseAsync(Action<WebApplicationBuilder>? configurar = null)
    {
        var enCi = string.Equals(Environment.GetEnvironmentVariable("CI"), "true", StringComparison.OrdinalIgnoreCase);
        var server = Environment.GetEnvironmentVariable("ConnectionStrings__Core");
        if (server is null && enCi)
            throw new InvalidOperationException("CI=true sin ConnectionStrings__Core: en CI las pruebas con base no se omiten.");
        server ??= $"Host=127.0.0.1;Port=5432;Username={Environment.UserName}";
        var api = new ApiDePrueba(new NpgsqlConnectionStringBuilder(server) { Database = $"flit_imp_{Guid.NewGuid():N}"[..30] }.ConnectionString, configurar);
        try
        {
            await using var admin = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(server) { Database = "postgres" }.ConnectionString);
            await admin.OpenAsync();
            await using var crear = new NpgsqlCommand($"CREATE DATABASE \"{new NpgsqlConnectionStringBuilder(api.BaseDeDatos).Database}\"", admin);
            await crear.ExecuteNonQueryAsync();
            api._baseCreada = true;
        }
        catch (NpgsqlException ex) when (!enCi)
        {
            api.Omitir = $"Postgres no alcanzable: {ex.Message}";
            return api;
        }

        api.App = api.Construir();
        await Program.MigrateAsync(api.App);
        await api.App.StartAsync();
        return api;
    }

    /// <summary>Arranca la API con la base inalcanzable y sin migrar.</summary>
    public static async Task<ApiDePrueba> SinBaseAsync(Action<WebApplicationBuilder>? configurar = null)
    {
        var api = new ApiDePrueba(BaseInalcanzable, configurar);
        api.App = api.Construir();
        await api.App.StartAsync();
        return api;
    }

    /// <summary>La configuración mínima con la que arranca el servicio (sin secretos reales).</summary>
    public static Dictionary<string, string?> Configuracion(string baseDeDatos) => new()
    {
        ["ConnectionStrings:Servicio"] = baseDeDatos,
        ["Servicio:MigrateOnStartup"] = "true",
        ["Platform:ServiceClient:ClientSecret"] = "secreto-de-prueba",
        ["Platform:ServiceClient:TokenEndpoint"] = "http://core-identity/connect/token",
        ["Platform:Auth:JwksUri"] = "http://core-identity/.well-known/jwks.json",
        ["Platform:Auth:Issuers:0"] = Issuer,
        ["Platform:Messaging:ConnectionString"] = "amqp://flit:x@127.0.0.1:1/",
        [ServicioSettings.GrpcPortKey] = GrpcPort.ToString(System.Globalization.CultureInfo.InvariantCulture),
        [CertificadoDePrueba.ClaveRuta] = CertificadoDePrueba.Ruta,
        [CertificadoDePrueba.ClaveClave] = CertificadoDePrueba.Clave,
    };

    public HttpClient Anonimo() => App.GetTestClient();

    public HttpClient Cliente(string token)
    {
        var client = App.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    /// <summary>
    /// Token de producto firmado con la llave del JWKS de prueba (usuario y empresa ficticios si no se dan): un <c>role_code</c>
    /// por rol, <c>permissions</c> como arreglo JSON, <c>tenant_type</c> y <c>is_super_admin</c> si se dan.
    /// </summary>
    public string Token(
        string audiencia = ServicioSettings.Codigo,
        Guid? empresa = null,
        IReadOnlyList<string>? permisos = null,
        IReadOnlyList<string>? roles = null,
        string? tipoEmpresa = null,
        bool superAdmin = false,
        Guid? usuario = null)
    {
        var claims = new List<Claim>
        {
            new("sub", (usuario ?? Guid.NewGuid()).ToString()),
            new("email", "operador@empresa.test"),
            new("tenant_id", (empresa ?? Guid.NewGuid()).ToString()),
            new("company_name", "Empresa de prueba"),
            new("permissions", JsonSerializer.Serialize(permisos ?? []), JsonClaimValueTypes.JsonArray),
        };
        claims.AddRange((roles ?? ["gestor_impuestos"]).Select(r => new Claim("role_code", r)));
        if (tipoEmpresa is not null)
            claims.Add(new Claim("tenant_type", tipoEmpresa));
        if (superAdmin)
            claims.Add(new Claim("role_code", "SuperAdmin"));
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = audiencia,
            Subject = new ClaimsIdentity(claims),
            Expires = DateTime.UtcNow.AddMinutes(30),
            SigningCredentials = new SigningCredentials(_llave, SecurityAlgorithms.RsaSha256),
        });
    }

    /// <summary>Contexto de la base efímera con la empresa fijada, para sembrar filas de una compañía.</summary>
    public ImpuestosDb Contexto(Guid? empresa) =>
        new(new DbContextOptionsBuilder<ImpuestosDb>().UseNpgsql(BaseDeDatos).UseSnakeCaseNamingConvention().Options, new EmpresaFija(empresa));

    public async ValueTask DisposeAsync()
    {
        if (App is not null)
            await App.DisposeAsync();
        if (!_baseCreada)
            return;

        NpgsqlConnection.ClearAllPools();
        await using var admin = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(BaseDeDatos) { Database = "postgres" }.ConnectionString);
        await admin.OpenAsync();
        await using var borrar = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{new NpgsqlConnectionStringBuilder(BaseDeDatos).Database}\" WITH (FORCE)", admin);
        await borrar.ExecuteNonQueryAsync();
    }

    private WebApplication Construir() => Program.Build([], b =>
    {
        b.WebHost.UseTestServer();
        b.Configuration.AddInMemoryCollection(Configuracion(BaseDeDatos));
        b.Services.AddHttpClient(PlatformAuthenticationExtensions.JwksHttpClientName).ConfigurePrimaryHttpMessageHandler(() => new Jwks(_llave));
        b.Services.AddSingleton<IProductosHabilitados>(Productos);
        _configurar?.Invoke(b);
    });

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

/// <summary>Empresa fija para un contexto de pruebas (siembra y lectura fuera de una petición).</summary>
public sealed class EmpresaFija(Guid? empresa) : IPlatformTenantAccessor
{
    public PlatformTenantContext Current { get; } = new(empresa, false, false);
}

/// <summary>
/// Doble de la suscripción: la respuesta la fija la prueba. Por defecto toda compañía tiene Impuestos activo;
/// <see cref="Apagar"/> suspende una y <see cref="Encender"/> la reactiva. Cuenta las consultas por compañía para probar que el
/// guard pregunta en cada petición.
/// </summary>
public sealed class ProductosHabilitadosFijo(bool todas = true) : IProductosHabilitados
{
    private readonly ConcurrentDictionary<Guid, bool> _porEmpresa = new();
    private readonly ConcurrentDictionary<Guid, int> _consultas = new();

    public bool Todas { get; set; } = todas;

    public void Encender(Guid empresa) => _porEmpresa[empresa] = true;

    public void Apagar(Guid empresa) => _porEmpresa[empresa] = false;

    public int Consultas(Guid empresa) => _consultas.TryGetValue(empresa, out var n) ? n : 0;

    public int ConsultasTotales => _consultas.Values.Sum();

    public Task<bool> ImpuestosHabilitadoAsync(Guid tenant, CancellationToken ct)
    {
        _consultas.AddOrUpdate(tenant, 1, (_, n) => n + 1);
        return Task.FromResult(_porEmpresa.TryGetValue(tenant, out var propia) ? propia : Todas);
    }
}
