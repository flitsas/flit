using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Flit.Consultas.Api;
using Flit.Consultas.Api.Comparendos;
using Flit.Consultas.Api.Persistence;
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
/// HU #13520 — <c>flit.consultas.v1.FuentesComparendosService</c>: el registro completo de cada comparendo por el
/// proveedor que pida Comparendos, vacío distinto de error, y los proveedores sin contrato como no conectados.
/// Verifik es falso (no sale a la red): responde según el documento consultado.
/// </summary>
public sealed class FuentesComparendosTests : IAsyncLifetime
{
    private const int GrpcPort = 5997;
    private const string Issuer = "https://hub.prueba/";
    private readonly RsaSecurityKey _key = new(RSA.Create(2048)) { KeyId = "prueba" };
    private readonly Guid _empresa = Guid.NewGuid();
    private string _database = string.Empty;
    private string? _skip;
    private WebApplication? _app;

    public async ValueTask InitializeAsync()
    {
        var server = Environment.GetEnvironmentVariable("ConnectionStrings__Core") ?? $"Host=127.0.0.1;Port=5432;Username={Environment.UserName}";
        _database = new NpgsqlConnectionStringBuilder(server) { Database = $"flit_svc_{Guid.NewGuid():N}"[..30] }.ConnectionString;
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
            return;
        }

        _app = Program.Build([], b =>
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
                ["Consultations:VerifikSimitMode"] = "real",
                ["Verifik:BearerToken"] = "llave-verifik-de-prueba",
                ["Verifik:BaseUrl"] = "https://verifik.prueba",
                ["Consultations:ComparendosSimulados"] = "true",
                [ServicioSettings.GrpcPortKey] = GrpcPort.ToString(System.Globalization.CultureInfo.InvariantCulture),
            });
            b.Services.AddHttpClient(PlatformAuthenticationExtensions.JwksHttpClientName).ConfigurePrimaryHttpMessageHandler(() => new Jwks(_key));
        }, s => s.AddHttpClient(ProveedorVerifikSimit.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => new VerifikFalsa()));
        await Program.MigrateAsync(_app);
        await _app.StartAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (_app is not null)
            await _app.DisposeAsync();
        if (_skip is null)
        {
            NpgsqlConnection.ClearAllPools();
            await using var db = new ConsultasDb(new DbContextOptionsBuilder<ConsultasDb>().UseNpgsql(_database).Options);
            await db.Database.EnsureDeletedAsync();
        }
    }

    private Task<ConsultarComparendosResponse> Consultar(string proveedor, CanalComparendos canal, string numero = "", string placa = "", string scope = "platform.consultas") =>
        Cliente(scope).ConsultarComparendosAsync(new ConsultarComparendosRequest { Canal = canal, Proveedor = proveedor, TipoDocumento = "NIT", NumeroDocumento = numero, Placa = placa },
            new Metadata { { "x-flit-tenant-id", _empresa.ToString() } }, cancellationToken: TestContext.Current.CancellationToken).ResponseAsync;

    [Fact]
    public async Task AC1_Verifik_DevuelveElRegistroCompleto_YMideElConsumo()
    {
        Assert.SkipWhen(_skip is not null, _skip ?? string.Empty);
        var r = await Consultar("verifik_simit", CanalComparendos.SimitIdentificacion, "900123456");
        r.Proveedor.Should().Be("verifik_simit");
        var parcial = r.Parciales.Should().ContainSingle().Subject;
        parcial.Resultado.Should().Be(ResultadoFuente.ExitosaConDatos);
        var c = parcial.Comparendos.Should().ContainSingle().Subject;
        (c.Numero, c.Placa, c.TipoDocumentoInfractor, c.NumeroDocumentoInfractor, c.NombreInfractor).Should().Be(("11001000000041234567", "ABC123", "NIT", "900123456", "EMPRESA DE PRUEBA S.A.S."));
        (c.CodigoInfraccion, c.FechaImposicion, c.Secretaria, c.ValorTotal, c.ValorCapital, c.EstadoFuente)
            .Should().Be(("C29", "2026-09-28", "BOGOTA D.C.", "604100.00", "580000.00", "Pendiente de pago"));
        c.HasFechaNotificacion.Should().BeFalse("lo que la fuente no trae va ausente, no vacío");
        r.RespuestaCruda.Should().Contain("multas");

        await using var scope = _app!.Services.CreateAsyncScope();
        var consumo = await scope.ServiceProvider.GetRequiredService<ConsultasDb>().Consumos.AsNoTracking().SingleAsync(x => x.Fuente == "comparendos", TestContext.Current.CancellationToken);
        (consumo.Proveedor, consumo.Resultado, consumo.TenantId).Should().Be(("verifik_simit", "con_datos", _empresa));
    }

    [Theory]
    [InlineData("800000404", ResultadoFuente.ExitosaVacia, TipoErrorFuente.Unspecified)]
    [InlineData("800000200", ResultadoFuente.ExitosaVacia, TipoErrorFuente.Unspecified)]
    [InlineData("800000500", ResultadoFuente.Error, TipoErrorFuente.HttpNoExitoso)]
    [InlineData("800000401", ResultadoFuente.Error, TipoErrorFuente.Autenticacion)]
    [InlineData("800000999", ResultadoFuente.Error, TipoErrorFuente.RespuestaInvalida)]
    public async Task AC2_VacioDistintoDeError(string numero, ResultadoFuente esperado, TipoErrorFuente tipo)
    {
        Assert.SkipWhen(_skip is not null, _skip ?? string.Empty);
        var parcial = (await Consultar("verifik_simit", CanalComparendos.SimitIdentificacion, numero)).Parciales.Single();
        (parcial.Resultado, parcial.TipoError).Should().Be((esperado, tipo));
        parcial.Comparendos.Should().BeEmpty();
    }

    [Fact]
    public async Task AC3_SinContrato_NoConectado_YNoSeConsulta()
    {
        Assert.SkipWhen(_skip is not null, _skip ?? string.Empty);
        var lista = await Cliente("platform.consultas").ListarProveedoresComparendosAsync(new ListarProveedoresComparendosRequest(),
            new Metadata { { "x-flit-tenant-id", _empresa.ToString() } }, cancellationToken: TestContext.Current.CancellationToken);
        lista.Proveedores.Single(p => p.Clave == "verifik_simit").Conectado.Should().BeTrue();
        var sc = lista.Proveedores.Single(p => p.Clave == "smartcloud");
        (sc.Conectado, sc.Motivo).Should().Be((false, "Falta el contrato de SmartCloud (WSDL, credenciales y una respuesta real)."));
        lista.Proveedores.Single(p => p.Clave == "municipal").Canales.Should().Equal(CanalComparendos.MunicipalIdentificacion, CanalComparendos.MunicipalPlaca);

        (await FluentActions.Awaiting(() => Consultar("smartcloud", CanalComparendos.SimitIdentificacion, "900123456")).Should().ThrowAsync<RpcException>())
            .Which.StatusCode.Should().Be(StatusCode.FailedPrecondition);
        (await FluentActions.Awaiting(() => Consultar("verifik_simit", CanalComparendos.MunicipalIdentificacion, "900123456")).Should().ThrowAsync<RpcException>())
            .Which.StatusCode.Should().Be(StatusCode.FailedPrecondition, "Verifik no atiende la fuente municipal");
        (await FluentActions.Awaiting(() => Consultar("verifik_simit", CanalComparendos.SimitPlaca)).Should().ThrowAsync<RpcException>())
            .Which.StatusCode.Should().Be(StatusCode.InvalidArgument);
        (await FluentActions.Awaiting(() => Consultar("verifik_simit", CanalComparendos.SimitIdentificacion, "900123456", scope: "platform.identidad.read")).Should().ThrowAsync<RpcException>())
            .Which.StatusCode.Should().Be(StatusCode.PermissionDenied);
    }

    [Fact]
    public async Task Simulado_PorPlaca_DevuelveDatosDePrueba()
    {
        Assert.SkipWhen(_skip is not null, _skip ?? string.Empty);
        var r = await Consultar("simulado", CanalComparendos.SimitPlaca, placa: "XYZ987");
        r.Parciales.Single().Comparendos.Should().OnlyContain(c => c.Placa == "XYZ987");
        (await Consultar("simulado", CanalComparendos.SimitPlaca, placa: "ABC000")).Parciales.Single().Resultado.Should().Be(ResultadoFuente.Error);
    }

    private FuentesComparendosService.FuentesComparendosServiceClient Cliente(string scope)
    {
        var token = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = "consultas",
            Subject = new ClaimsIdentity([new Claim("sub", "svc-comparendos"), new Claim("scope", scope)]),
            Expires = DateTime.UtcNow.AddMinutes(10),
            SigningCredentials = new SigningCredentials(_key, SecurityAlgorithms.RsaSha256),
        });
        var channel = GrpcChannel.ForAddress($"http://localhost:{GrpcPort}", new GrpcChannelOptions { HttpHandler = _app!.GetTestServer().CreateHandler() });
        return new(channel.CreateCallInvoker().Intercept(m =>
        {
            m.Add("authorization", $"Bearer {token}");
            return m;
        }));
    }

    /// <summary>Verifik falsa: el documento decide la respuesta (404, 500, 401, sin lista, vacía o una multa).</summary>
    private sealed class VerifikFalsa : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            request.Headers.Authorization!.Parameter.Should().Be("llave-verifik-de-prueba");
            var q = request.RequestUri!.Query;
            static HttpResponseMessage R(HttpStatusCode s, string body) => new(s) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
            return Task.FromResult(q switch
            {
                _ when q.Contains("800000404", StringComparison.Ordinal) => R(HttpStatusCode.NotFound, "{}"),
                _ when q.Contains("800000500", StringComparison.Ordinal) => R(HttpStatusCode.InternalServerError, "caído"),
                _ when q.Contains("800000401", StringComparison.Ordinal) => R(HttpStatusCode.Unauthorized, "{}"),
                _ when q.Contains("800000999", StringComparison.Ordinal) => R(HttpStatusCode.OK, """{"data":{"otra":1}}"""),
                _ when q.Contains("800000200", StringComparison.Ordinal) => R(HttpStatusCode.OK, """{"data":{"multas":[]}}"""),
                _ => R(HttpStatusCode.OK, """
                    {"data":{"multas":[{"numeroComparendo":"11001000000041234567","placa":"ABC123","valor":580000,"valorPagar":604100,
                      "organismoTransito":"BOGOTA D.C.","fechaComparendo":"28/09/2026","estadoComparendo":null,"estadoCartera":"Pendiente de pago",
                      "infractor":{"tipoDocumento":"NIT","numeroDocumento":"900123456","nombre":"EMPRESA DE PRUEBA","apellido":"S.A.S."},
                      "infracciones":[{"codigoInfraccion":"C29","descripcionInfraccion":"Velocidad","valorInfraccion":604100}]}]}}
                    """),
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
