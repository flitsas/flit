using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Flit.Consultas.Api;
using Flit.Consultas.Api.Persistence;
using Flit.Consultas.Api.RuntIntempo;
using Flit.Consultas.Grpc.V1;
using Flit.Platform.Sdk.Authentication;
using FluentAssertions;
using Grpc.Core;
using Grpc.Core.Interceptors;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using Xunit;

namespace Flit.Consultas.Tests;

/// <summary>
/// HU #13876 (F10-H4) — <c>flit.consultas.v1.RuntIntempoService</c> punta a punta con TestHost: los dos rpcs (G2=b)
/// con token de servicio, consumo medido en <c>consultas.consumo</c> (<c>runt_intempo_placa</c> / <c>runt_intempo_vin</c>),
/// validación, no conectado sin la URL base y sin token. Intempo es un <see cref="HttpMessageHandler"/> falso (no sale a
/// la red) y los datos son inventados (TST4xx, VIN 9ZZPRUEBA…). Necesita el Postgres de <c>ConnectionStrings__Core</c>:
/// sin él la prueba FALLA (no se omite), por el contrato de la ola.
/// <para>Uso de ejemplo: <c>Cliente(_app, "platform.consultas").ConsultarPorPlacaAsync(new() { Placa = "TST401" }, Tenant())</c>.</para>
/// </summary>
public sealed class RuntIntempoGrpcTests : IAsyncLifetime
{
    private const int GrpcPort = 5996;
    private const string Issuer = "https://hub.prueba/";
    private const string BaseIntempo = "https://intempo.prueba";
    private const string VinEncontrado = "9ZZPRUEBA00000401";
    private const string VinError = "9ZZPRUEBA00000500";
    private readonly IntempoFalsa _intempo = new();
    private readonly IntempoFalsa _intempoSinConfigurar = new();
    private readonly RsaSecurityKey _key = new(RSA.Create(2048)) { KeyId = "prueba" };
    private readonly Guid _empresa = Guid.NewGuid();
    private string _database = string.Empty;
    private WebApplication? _app;
    private WebApplication? _sinIntempo;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        var server = Environment.GetEnvironmentVariable("ConnectionStrings__Core") ?? $"Host=127.0.0.1;Port=5432;Username={Environment.UserName}";
        _database = new NpgsqlConnectionStringBuilder(server) { Database = $"flit_svc_{Guid.NewGuid():N}"[..30] }.ConnectionString;
        // Sin captura: si Postgres no está, la prueba falla (el patrón «_skip» no cuenta como verde en esta ola).
        await using (var admin = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(server) { Database = "postgres" }.ConnectionString))
        {
            await admin.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE DATABASE \"{new NpgsqlConnectionStringBuilder(_database).Database}\"", admin);
            await create.ExecuteNonQueryAsync();
        }

        _app = Construir(new Dictionary<string, string?>
        {
            [IntempoOpciones.VariableBaseUrl] = BaseIntempo,
            [IntempoOpciones.VariableTimeout] = "5",
        }, _intempo);
        await Program.MigrateAsync(_app);
        await _app.StartAsync();

        // Mismo servicio sin la URL base (vacía pisa cualquier variable de entorno de la máquina).
        _sinIntempo = Construir(new Dictionary<string, string?> { [IntempoOpciones.VariableBaseUrl] = "" }, _intempoSinConfigurar);
        await _sinIntempo.StartAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (_sinIntempo is not null)
            await _sinIntempo.DisposeAsync();
        if (_app is not null)
            await _app.DisposeAsync();
        NpgsqlConnection.ClearAllPools();
        await using var db = new ConsultasDb(new DbContextOptionsBuilder<ConsultasDb>().UseNpgsql(_database).Options);
        await db.Database.EnsureDeletedAsync();
    }

    [Fact]
    public async Task Por_placa_y_por_VIN_responden_ENCONTRADO_y_registran_el_consumo_de_su_rpc()
    {
        var cliente = Cliente(_app!, "platform.consultas");

        var porPlaca = await cliente.ConsultarPorPlacaAsync(new ConsultarPorPlacaRequest { Placa = "TST401" }, Tenant(), cancellationToken: Ct);
        var porVin = await cliente.ConsultarPorVinAsync(new ConsultarPorVinRequest { Vin = VinEncontrado }, Tenant(), cancellationToken: Ct);

        foreach (var r in new[] { porPlaca, porVin })
        {
            (r.Resultado, r.Proveedor, r.Vehiculo.Plate, r.Vehiculo.Vin).Should().Be((ResultadoRuntIntempo.Encontrado, "intempo", "TST401", VinEncontrado));
        }

        _intempo.Pedidos.Should().Equal(
            $"POST {BaseIntempo}{IntempoOpciones.RutaPlacaPorDefecto}?Plate=TST401",
            $"POST {BaseIntempo}{IntempoOpciones.RutaVinPorDefecto}?Vin={VinEncontrado}");

        var consumos = await Consumos();
        consumos.Should().HaveCount(2, "una fila por llamada");
        consumos.Select(c => (c.Fuente, c.Proveedor, c.Resultado, c.DesdeCache, c.TenantId)).Should().BeEquivalentTo(new[]
        {
            ("runt_intempo_placa", "intempo", "con_datos", false, _empresa),
            ("runt_intempo_vin", "intempo", "con_datos", false, _empresa),
        });
    }

    [Fact]
    public async Task No_encontrado_registra_consumo_vacia_y_error_registra_error()
    {
        var cliente = Cliente(_app!, "platform.consultas");

        var noEncontrado = await cliente.ConsultarPorPlacaAsync(new ConsultarPorPlacaRequest { Placa = "TST404" }, Tenant(), cancellationToken: Ct);
        var error = await cliente.ConsultarPorVinAsync(new ConsultarPorVinRequest { Vin = VinError }, Tenant(), cancellationToken: Ct);

        noEncontrado.Resultado.Should().Be(ResultadoRuntIntempo.NoEncontrado);
        (error.Resultado, error.TipoError, error.HttpStatus).Should().Be((ResultadoRuntIntempo.Error, TipoErrorFuente.HttpNoExitoso, 500));

        var consumos = await Consumos();
        consumos.Select(c => (c.Fuente, c.Proveedor, c.Resultado, c.DesdeCache)).Should().BeEquivalentTo(new[]
        {
            ("runt_intempo_placa", "intempo", "vacia", false),
            ("runt_intempo_vin", "intempo", "error", false),
        });
    }

    [Fact]
    public async Task Placa_vacia_y_VIN_vacio_son_InvalidArgument_en_su_rpc_sin_llamar_a_Intempo()
    {
        var cliente = Cliente(_app!, "platform.consultas");

        var placa = (await FluentActions.Awaiting(() => cliente.ConsultarPorPlacaAsync(new ConsultarPorPlacaRequest { Placa = "   " }, Tenant(), cancellationToken: Ct).ResponseAsync)
            .Should().ThrowAsync<RpcException>()).Which;
        var vin = (await FluentActions.Awaiting(() => cliente.ConsultarPorVinAsync(new ConsultarPorVinRequest { Vin = "" }, Tenant(), cancellationToken: Ct).ResponseAsync)
            .Should().ThrowAsync<RpcException>()).Which;

        (placa.StatusCode, placa.Status.Detail).Should().Be((StatusCode.InvalidArgument, "Se necesita la placa."));
        (vin.StatusCode, vin.Status.Detail).Should().Be((StatusCode.InvalidArgument, "Se necesita el VIN."));
        _intempo.Pedidos.Should().BeEmpty();
        (await Consumos()).Should().BeEmpty("la validación corta antes de medir");
    }

    [Fact]
    public async Task Sin_configuracion_es_FailedPrecondition_con_nombres_de_variables()
    {
        var cliente = Cliente(_sinIntempo!, "platform.consultas");

        var placa = (await FluentActions.Awaiting(() => cliente.ConsultarPorPlacaAsync(new ConsultarPorPlacaRequest { Placa = "TST401" }, Tenant(), cancellationToken: Ct).ResponseAsync)
            .Should().ThrowAsync<RpcException>()).Which;
        var vin = (await FluentActions.Awaiting(() => cliente.ConsultarPorVinAsync(new ConsultarPorVinRequest { Vin = VinEncontrado }, Tenant(), cancellationToken: Ct).ResponseAsync)
            .Should().ThrowAsync<RpcException>()).Which;

        foreach (var e in new[] { placa, vin })
        {
            e.StatusCode.Should().Be(StatusCode.FailedPrecondition);
            e.Status.Detail.Should().Contain(IntempoOpciones.VariableBaseUrl).And.NotContain("TST401").And.NotContain(VinEncontrado).And.NotContain("http");
        }

        _intempoSinConfigurar.Pedidos.Should().BeEmpty("sin URL base no se sale a la red");
        (await Consumos()).Should().BeEmpty();
    }

    [Fact]
    public async Task Sin_token_de_servicio_es_Unauthenticated()
    {
        var sinToken = Cliente(_app!, scope: null);
        var otroScope = Cliente(_app!, "platform.identidad.read");

        async Task<StatusCode> Codigo(Func<Task> llamada) => (await FluentActions.Awaiting(llamada).Should().ThrowAsync<RpcException>()).Which.StatusCode;

        (await Codigo(() => sinToken.ConsultarPorPlacaAsync(new ConsultarPorPlacaRequest { Placa = "TST401" }, Tenant(), cancellationToken: Ct).ResponseAsync))
            .Should().Be(StatusCode.Unauthenticated);
        (await Codigo(() => sinToken.ConsultarPorVinAsync(new ConsultarPorVinRequest { Vin = VinEncontrado }, Tenant(), cancellationToken: Ct).ResponseAsync))
            .Should().Be(StatusCode.Unauthenticated);
        (await Codigo(() => otroScope.ConsultarPorPlacaAsync(new ConsultarPorPlacaRequest { Placa = "TST401" }, Tenant(), cancellationToken: Ct).ResponseAsync))
            .Should().Be(StatusCode.PermissionDenied, "exige el scope platform.consultas");
        (await Codigo(() => otroScope.ConsultarPorVinAsync(new ConsultarPorVinRequest { Vin = VinEncontrado }, Tenant(), cancellationToken: Ct).ResponseAsync))
            .Should().Be(StatusCode.PermissionDenied, "exige el scope platform.consultas");

        _intempo.Pedidos.Should().BeEmpty();
        (await Consumos()).Should().BeEmpty();
    }

    [Fact]
    public async Task El_servicio_arranca_sin_variables_de_Intempo()
    {
        (await _sinIntempo!.GetTestClient().GetAsync("/health", Ct)).StatusCode.Should().Be(HttpStatusCode.OK);
        // El servicio está mapeado y responde (no UNIMPLEMENTED): no conectado con el nombre de la variable.
        var e = (await FluentActions.Awaiting(() => Cliente(_sinIntempo!, "platform.consultas")
                .ConsultarPorPlacaAsync(new ConsultarPorPlacaRequest { Placa = "TST401" }, Tenant(), cancellationToken: Ct).ResponseAsync)
            .Should().ThrowAsync<RpcException>()).Which;
        e.StatusCode.Should().Be(StatusCode.FailedPrecondition);
    }

    private WebApplication Construir(Dictionary<string, string?> intempo, IntempoFalsa handler) => Program.Build([], b =>
    {
        b.WebHost.UseTestServer();
        b.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Servicio"] = _database,
            ["Platform:ServiceClient:ClientSecret"] = "secreto",
            ["Platform:ServiceClient:TokenEndpoint"] = "http://core-identity/connect/token",
            ["Platform:Auth:JwksUri"] = "http://core-identity/.well-known/jwks.json",
            ["Platform:Auth:Issuers:0"] = Issuer,
            ["Platform:Messaging:ConnectionString"] = "amqp://flit:x@127.0.0.1:1/",
            ["ImprontaRunt:ApiKey"] = "llave-de-prueba",
            ["Verifik:BearerToken"] = "llave-verifik-de-prueba",
            [ServicioSettings.GrpcPortKey] = GrpcPort.ToString(System.Globalization.CultureInfo.InvariantCulture),
        });
        b.Configuration.AddInMemoryCollection(intempo);
        b.Services.AddHttpClient(PlatformAuthenticationExtensions.JwksHttpClientName).ConfigurePrimaryHttpMessageHandler(() => new Jwks(_key));
    }, s => s.AddHttpClient(ProveedorRuntIntempo.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => handler));

    private Metadata Tenant() => new() { { "x-flit-tenant-id", _empresa.ToString() } };

    private async Task<List<ConsumoFila>> Consumos()
    {
        await using var scope = _app!.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ConsultasDb>().Consumos.AsNoTracking()
            .Where(c => c.Fuente.StartsWith("runt_intempo"))
            .OrderBy(c => c.Fuente)
            .Select(c => new ConsumoFila(c.Fuente, c.Proveedor, c.Resultado, c.DesdeCache, c.TenantId))
            .ToListAsync(Ct);
    }

    private sealed record ConsumoFila(string Fuente, string Proveedor, string Resultado, bool DesdeCache, Guid TenantId);

    private RuntIntempoService.RuntIntempoServiceClient Cliente(WebApplication app, string? scope)
    {
        var channel = GrpcChannel.ForAddress($"http://localhost:{GrpcPort}", new GrpcChannelOptions { HttpHandler = app.GetTestServer().CreateHandler() });
        if (scope is null)
            return new(channel);

        var token = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = "consultas",
            Subject = new ClaimsIdentity([new Claim("sub", "svc-diagnostico"), new Claim("scope", scope)]),
            Expires = DateTime.UtcNow.AddMinutes(10),
            SigningCredentials = new SigningCredentials(_key, SecurityAlgorithms.RsaSha256),
        });
        return new(channel.CreateCallInvoker().Intercept(m =>
        {
            m.Add("authorization", $"Bearer {token}");
            return m;
        }));
    }

    /// <summary>Intempo falsa: registra «MÉTODO url» y responde según el valor consultado (inventado).</summary>
    private sealed class IntempoFalsa : HttpMessageHandler
    {
        private const string Crudo = """{"noPlaca":"TST401","noVin":"9ZZPRUEBA00000401","claseVehiculo":"AUTOMOVIL","marca":"MARCA PRUEBA"}""";

        public List<string> Pedidos { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.AbsoluteUri;
            lock (Pedidos)
                Pedidos.Add($"{request.Method} {url}");
            static HttpResponseMessage R(HttpStatusCode s, string body) => new(s) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
            return Task.FromResult(url switch
            {
                _ when url.EndsWith("Plate=TST404", StringComparison.Ordinal) => R(HttpStatusCode.NotFound, "{}"),
                _ when url.EndsWith($"Vin={VinError}", StringComparison.Ordinal) => R(HttpStatusCode.InternalServerError, "caído"),
                _ => R(HttpStatusCode.OK, Crudo),
            });
        }
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
