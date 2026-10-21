using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Flit.Diagnostico.Api;
using Flit.Diagnostico.Api.Modulos.Catalogos;
using Flit.Diagnostico.Api.Plataforma;
using Flit.Diagnostico.Domain.Catalogos;
using Flit.Diagnostico.Tests.Catalogos;
using Flit.Diagnostico.Tests.Plataforma;
using Flit.Diagnostico.Tests.Seguridad;
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

namespace Flit.Diagnostico.Tests.Api;

/// <summary>
/// La API de Diagnóstico de verdad (<c>Program.Build</c>) con tokens de producto firmados con una llave propia servida
/// como JWKS (patrón de Comparendos). Con base: un Postgres real con una base efímera, migrada como al arrancar
/// (<c>Servicio:MigrateOnStartup=true</c>); sin Postgres se omite en local y falla en CI, también si a CI le falta
/// <c>ConnectionStrings__Core</c> (HU #13722). Sin base: la cadena apunta a un puerto cerrado, para probar lo que no depende
/// de la base o su ausencia. <c>mapear</c> agrega rutas de prueba antes de arrancar (sondas de fuga, endpoints fantasma).
/// </summary>
/// <example>
/// <code>
/// await using var api = await ApiDePrueba.ConBaseAsync();
/// Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
/// var respuesta = await api.Cliente(api.Token()).GetAsync("/health/ready");
/// </code>
/// </example>
public sealed class ApiDePrueba : IAsyncDisposable
{
    public const string Issuer = "https://hub.prueba/";
    public const int GrpcPort = 5997;

    /// <summary>Postgres inalcanzable: puerto 1 en loopback, con tiempo de espera corto.</summary>
    public const string BaseInalcanzable = "Host=127.0.0.1;Port=1;Database=diagnostico_ausente;Username=nadie;Password=nada;Timeout=2";

    private readonly RsaSecurityKey _llave = new(RSA.Create(2048)) { KeyId = "prueba-diagnostico" };
    private readonly Action<WebApplicationBuilder>? _configurar;
    private bool _baseCreada;

    private ApiDePrueba(string baseDeDatos, Action<WebApplicationBuilder>? configurar)
    {
        BaseDeDatos = baseDeDatos;
        _configurar = configurar;
    }

    /// <summary>
    /// Suscripción de prueba (HU #13726): toda compañía con Diagnóstico activo salvo que la prueba la apague. Para probar
    /// la implementación real, <c>configurar</c> quita este registro o pone otro.
    /// </summary>
    public ProductosHabilitadosFijo Productos { get; } = new();

    /// <summary>
    /// Doble del cliente gRPC de catálogos de Trámites (HU #13742): el servicio lo consume por el adaptador real
    /// <see cref="CatalogoTiposTramiteGrpc"/> (reintentos, caché, filtro), sin core-api.
    /// </summary>
    public CatalogosFalso Catalogos { get; } = new();

    /// <summary>Motivo para omitir la prueba (sin Postgres en local); null si la API está arriba.</summary>
    public string? Omitir { get; private set; }

    public WebApplication App { get; private set; } = null!;

    /// <summary>Cadena de conexión de la base efímera (o de la inalcanzable).</summary>
    public string BaseDeDatos { get; }

    /// <summary>Crea una base efímera, arranca la API y aplica sus migraciones como al arrancar.</summary>
    public static async Task<ApiDePrueba> ConBaseAsync(Action<WebApplication>? mapear = null, Func<string, string?>? entorno = null, Action<WebApplicationBuilder>? configurar = null)
    {
        entorno ??= Environment.GetEnvironmentVariable;
        var enCi = string.Equals(entorno("CI"), "true", StringComparison.OrdinalIgnoreCase);
        var server = entorno("ConnectionStrings__Core");
        if (server is null && enCi)
            throw new InvalidOperationException(
                "CI=true sin ConnectionStrings__Core: en CI las pruebas de integración no se omiten. Configure el Postgres del job.");
        server ??= $"Host=127.0.0.1;Port=5432;Username={Environment.UserName}";
        var api = new ApiDePrueba(new NpgsqlConnectionStringBuilder(server) { Database = $"flit_dgn_{Guid.NewGuid():N}"[..30] }.ConnectionString, configurar);
        try
        {
            await using var admin = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(server) { Database = "postgres" }.ConnectionString);
            await admin.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE DATABASE \"{new NpgsqlConnectionStringBuilder(api.BaseDeDatos).Database}\"", admin);
            await create.ExecuteNonQueryAsync();
            api._baseCreada = true;
        }
        catch (NpgsqlException ex) when (!enCi)
        {
            api.Omitir = $"Postgres no alcanzable: {ex.Message}";
            return api;
        }

        api.App = api.Construir();
        mapear?.Invoke(api.App);
        await Program.MigrateAsync(api.App);
        await api.App.StartAsync();
        return api;
    }

    /// <summary>Arranca la API con la base inalcanzable y sin migrar.</summary>
    public static async Task<ApiDePrueba> SinBaseAsync(Action<WebApplication>? mapear = null, Action<WebApplicationBuilder>? configurar = null)
    {
        var api = new ApiDePrueba(BaseInalcanzable, configurar);
        api.App = api.Construir();
        mapear?.Invoke(api.App);
        await api.App.StartAsync();
        return api;
    }

    /// <summary>La configuración mínima con la que arranca el servicio.</summary>
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
        // HU #13751: llaves de Data Protection protegidas con un certificado efímero (sin él el servicio no arranca).
        [CertificadoDePrueba.ClaveRuta] = CertificadoDePrueba.Ruta,
        [CertificadoDePrueba.ClaveClave] = CertificadoDePrueba.Clave,
        // HU #13727: el trabajador hospedado de la cola no corre en las pruebas; cada prueba llama al procesador directo.
        ["Diagnostico:Procesador:Habilitado"] = "false",
    };

    public HttpClient Anonimo() => App.GetTestClient();

    public HttpClient Cliente(string token)
    {
        var client = App.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    /// <summary>
    /// Token de producto firmado con la llave del JWKS de prueba (usuario ficticio; empresa ficticia si no se da). Como el
    /// de Identidad: un <c>role_code</c> por rol, <c>permissions</c> como arreglo JSON y <c>tenant_type</c> si se da
    /// (HU #13724).
    /// </summary>
    public string Token(
        string audiencia = ServicioSettings.Codigo,
        Guid? empresa = null,
        IReadOnlyList<string>? permisos = null,
        IReadOnlyList<string>? roles = null,
        string? tipoEmpresa = null)
    {
        var claims = new List<Claim>
        {
            new("sub", Guid.NewGuid().ToString()),
            new("email", "operador@empresa.test"),
            new("tenant_id", (empresa ?? Guid.NewGuid()).ToString()),
            new("company_name", "Empresa de prueba"),
            new("permissions", JsonSerializer.Serialize(permisos ?? []), JsonClaimValueTypes.JsonArray),
        };
        claims.AddRange((roles ?? ["admin_diagnostico"]).Select(r => new Claim("role_code", r)));
        if (tipoEmpresa is not null)
            claims.Add(new Claim("tenant_type", tipoEmpresa));
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = audiencia,
            Subject = new ClaimsIdentity(claims),
            Expires = DateTime.UtcNow.AddMinutes(30),
            SigningCredentials = new SigningCredentials(_llave, SecurityAlgorithms.RsaSha256),
        });
    }

    public async ValueTask DisposeAsync()
    {
        if (App is not null)
            await App.DisposeAsync();
        if (!_baseCreada)
            return;

        NpgsqlConnection.ClearAllPools();
        await using var admin = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(BaseDeDatos) { Database = "postgres" }.ConnectionString);
        await admin.OpenAsync();
        await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{new NpgsqlConnectionStringBuilder(BaseDeDatos).Database}\" WITH (FORCE)", admin);
        await drop.ExecuteNonQueryAsync();
    }

    private WebApplication Construir() => Program.Build([], b =>
    {
        b.WebHost.UseTestServer();
        b.Configuration.AddInMemoryCollection(Configuracion(BaseDeDatos));
        b.Services.AddHttpClient(PlatformAuthenticationExtensions.JwksHttpClientName).ConfigurePrimaryHttpMessageHandler(() => new Jwks(_llave));
        b.Services.AddSingleton<IProductosHabilitados>(Productos);
        b.Services.AddSingleton<Flit.Tramites.Grpc.V1.CatalogosService.CatalogosServiceClient>(Catalogos);
        b.Services.AddScoped<ICatalogoTiposTramite, CatalogoTiposTramiteGrpc>();
        // HU #13733: las pruebas anteriores a la guardia de fuentes sin adaptador habilitan y piden fuentes que en D2 no
        // tienen adaptador; aquí todas cuentan como con adaptador. FuentesSinAdaptadorTests quita este doble y prueba el real.
        b.Services.AddSingleton<Flit.Diagnostico.Api.Modulos.Consultas.IRegistroAdaptadores>(new TodasConAdaptador());
        _configurar?.Invoke(b);
    });

    /// <summary>Doble del registro de adaptadores: toda fuente cuenta como con adaptador (HU #13733).</summary>
    private sealed class TodasConAdaptador : Flit.Diagnostico.Api.Modulos.Consultas.IRegistroAdaptadores
    {
        public bool TieneAdaptador(Flit.Diagnostico.Domain.Consultas.Fuente fuente) => true;
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
