using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Flit.Consultas.Api;
using Flit.Consultas.Api.Persistence;
using Flit.Consultas.Grpc.V1;
using Flit.Platform.Sdk.Authentication;
using Flit.Tramites.Application.UseCases.Consultations;
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
/// HU #13343 (Epic #13316) — <c>flit.consultas.v1.ConsultasService</c> contra core-consultas real (Postgres efímero,
/// tokens firmados con una llave de prueba servida como JWKS). La cadena de cada empresa sale de
/// <c>consultas.configuracion_empresa</c>; los proveedores de la prueba son falsos para no salir a la red.
/// </summary>
public sealed class ConsultasGrpcTests : IAsyncLifetime
{
    private const int GrpcPort = 5998;
    private const string Issuer = "https://hub.prueba/";

    private readonly RsaSecurityKey _key = new(RSA.Create(2048)) { KeyId = "prueba" };
    private readonly Guid _empresaRapida = Guid.NewGuid();
    private readonly Guid _empresaConLento = Guid.NewGuid();
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
                ["Consultations:VerifikVehicleMode"] = "mock",
                ["ImprontaRunt:ApiKey"] = "llave-de-prueba",
                [ServicioSettings.GrpcPortKey] = GrpcPort.ToString(System.Globalization.CultureInfo.InvariantCulture),
            });
            b.Services.AddHttpClient(PlatformAuthenticationExtensions.JwksHttpClientName).ConfigurePrimaryHttpMessageHandler(() => new Jwks(_key));
            b.Services.AddTransient<IConsultationProvider>(_ => new Falso("falso_rapido", TimeSpan.Zero));
            b.Services.AddTransient<IConsultationProvider>(_ => new Falso("falso_lento", Timeout.InfiniteTimeSpan));
            b.Services.AddTransient<IConsultationProvider>(_ => new Falso("falso_otro", TimeSpan.Zero));
        });
        await Program.MigrateAsync(_app);
        await using (var scope = _app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ConsultasDb>();
            db.ConfiguracionEmpresas.Add(Config(_empresaRapida, """{"vehicle_plate":{"primary":"falso_rapido","fallback":[]}}"""));
            db.ConfiguracionEmpresas.Add(Config(_empresaConLento, """{"vehicle_plate":{"primary":"falso_lento","fallback":["falso_rapido"]}}"""));
            await db.SaveChangesAsync();
        }

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

    [Fact]
    public async Task AC1_ConsultarVehiculo_UsaLaCadenaDeEsaEmpresa_YDevuelveElResultadoNormalizado()
    {
        Assert.SkipWhen(_skip is not null, _skip ?? string.Empty);
        var respuesta = await Cliente(Token("platform.consultas")).ConsultarVehiculoAsync(
            new ConsultarVehiculoRequest { Placa = new Flit.Platform.Grpc.V1.Placa { Valor = "ABC123" } },
            Empresa(_empresaRapida), cancellationToken: TestContext.Current.CancellationToken);

        respuesta.Resultado.Proveedor.Should().Be("falso_rapido");
        respuesta.Resultado.Semaforo.Should().Be(Semaforo.Verde);
        respuesta.Resultado.Chequeos.Should().ContainSingle(c => c.Clave == "soat" && c.Estado == EstadoChequeo.Ok);
        respuesta.Resultado.Campos.Should().ContainSingle(c => c.Clave == "placa_vista" && c.ValorTexto == "ABC123");
    }

    [Fact]
    public async Task AC2_TokenDeServicioSinElScope_PermissionDenied()
    {
        Assert.SkipWhen(_skip is not null, _skip ?? string.Empty);
        var llamada = async () => await Cliente(Token("platform.identidad.read")).ConsultarVehiculoAsync(
            new ConsultarVehiculoRequest { Placa = new Flit.Platform.Grpc.V1.Placa { Valor = "ABC123" } },
            Empresa(_empresaRapida), cancellationToken: TestContext.Current.CancellationToken);

        (await llamada.Should().ThrowAsync<RpcException>()).Which.StatusCode.Should().Be(StatusCode.PermissionDenied);
    }

    [Fact]
    public async Task AC3_ElPrimeroNoResponde_ElSiguienteContestaDentroDelDeadline()
    {
        Assert.SkipWhen(_skip is not null, _skip ?? string.Empty);
        var inicio = DateTime.UtcNow;
        var respuesta = await Cliente(Token("platform.consultas")).ConsultarVehiculoAsync(
            new ConsultarVehiculoRequest { Placa = new Flit.Platform.Grpc.V1.Placa { Valor = "ABC123" } },
            Empresa(_empresaConLento), deadline: DateTime.UtcNow.AddSeconds(3), cancellationToken: TestContext.Current.CancellationToken);

        respuesta.Resultado.Proveedor.Should().Be("falso_rapido");
        (DateTime.UtcNow - inicio).Should().BeLessThan(TimeSpan.FromSeconds(3));
    }

    [Fact]
    public async Task SinPlacaNiVin_InvalidArgument()
    {
        Assert.SkipWhen(_skip is not null, _skip ?? string.Empty);
        var llamada = async () => await Cliente(Token("platform.consultas")).ConsultarVehiculoAsync(
            new ConsultarVehiculoRequest(), Empresa(_empresaRapida), cancellationToken: TestContext.Current.CancellationToken);

        (await llamada.Should().ThrowAsync<RpcException>()).Which.StatusCode.Should().Be(StatusCode.InvalidArgument);
    }

    [Fact]
    public async Task ConsultarAvaluos_AgregaLasFuentesHabilitadas()
    {
        Assert.SkipWhen(_skip is not null, _skip ?? string.Empty);
        var respuesta = await Cliente(Token("platform.consultas")).ConsultarAvaluosAsync(
            new ConsultarAvaluosRequest { Vin = new Flit.Platform.Grpc.V1.Vin { Valor = "9BWZZZ377VT004251" }, Modelo = 2020 },
            Empresa(_empresaRapida), cancellationToken: TestContext.Current.CancellationToken);

        // Sin configuración de avalúos: solo Fasecolda; en mock y sin valores sembrados, «sin datos».
        respuesta.Avaluos.Should().ContainSingle(a => a.Fuente == "fasecolda").Which.Estado.Should().Be(EstadoAvaluo.SinDatos);
        respuesta.ValorSugerido.Should().Be(0);
    }

    [Fact]
    public async Task HU13344_LoQueGuardaLaAdministracion_AplicaALaSiguienteConsulta()
    {
        Assert.SkipWhen(_skip is not null, _skip ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var empresa = Guid.NewGuid();
        var admin = new ConsultasAdminService.ConsultasAdminServiceClient(Canal(Token("platform.consultas.admin")));
        var consultas = Cliente(Token("platform.consultas"));
        var pedido = new ConsultarVehiculoRequest { Placa = new Flit.Platform.Grpc.V1.Placa { Valor = "ABC123" } };

        await admin.GuardarConfiguracionEmpresaAsync(Guardar("falso_rapido"), Empresa(empresa), cancellationToken: ct);
        (await consultas.ConsultarVehiculoAsync(pedido, Empresa(empresa), cancellationToken: ct)).Resultado.Proveedor.Should().Be("falso_rapido");

        await admin.GuardarConfiguracionEmpresaAsync(Guardar("falso_otro"), Empresa(empresa), cancellationToken: ct);
        (await consultas.ConsultarVehiculoAsync(pedido, Empresa(empresa), cancellationToken: ct)).Resultado.Proveedor.Should().Be("falso_otro");

        var leida = await admin.ObtenerConfiguracionEmpresaAsync(new ObtenerConfiguracionEmpresaRequest(), Empresa(empresa), cancellationToken: ct);
        leida.Configuracion.Cadenas["vehicle_plate"].Principal.Should().Be("falso_otro");
        leida.Configuracion.FuenteMultas.Should().Be("internal");
        leida.Configuracion.AvaluosHabilitados.Should().Equal("fasecolda", "base_gravable");
    }

    [Fact]
    public async Task HU13344_ConsultarConElScopeDeAdministracion_PermissionDenied()
    {
        Assert.SkipWhen(_skip is not null, _skip ?? string.Empty);
        var admin = new ConsultasAdminService.ConsultasAdminServiceClient(Canal(Token("platform.consultas")));
        var llamada = async () => await admin.GuardarConfiguracionEmpresaAsync(Guardar("falso_rapido"), Empresa(Guid.NewGuid()), cancellationToken: TestContext.Current.CancellationToken);

        (await llamada.Should().ThrowAsync<RpcException>()).Which.StatusCode.Should().Be(StatusCode.PermissionDenied);
    }

    [Fact]
    public async Task HU13345_CadaConsultaDejaSuFila_TambienLasQueFallan()
    {
        Assert.SkipWhen(_skip is not null, _skip ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var consultas = Cliente(Token("platform.consultas"));

        await consultas.ConsultarVehiculoAsync(new ConsultarVehiculoRequest { Placa = new Flit.Platform.Grpc.V1.Placa { Valor = "ABC123" } }, Empresa(_empresaRapida), cancellationToken: ct);
        var invalida = async () => await consultas.ConsultarVehiculoAsync(new ConsultarVehiculoRequest(), Empresa(_empresaRapida), cancellationToken: ct);
        await invalida.Should().ThrowAsync<RpcException>();

        await using var scope = _app!.Services.CreateAsyncScope();
        var filas = await scope.ServiceProvider.GetRequiredService<ConsultasDb>().Consumos.AsNoTracking()
            .Where(c => c.TenantId == _empresaRapida).OrderBy(c => c.OcurridoEn).ToListAsync(ct);

        filas.Should().HaveCount(2);
        filas[0].Should().BeEquivalentTo(new { Producto = "tramites", Fuente = "vehiculo", Proveedor = "falso_rapido", Resultado = "verde", DesdeCache = false });
        filas[0].LatenciaMs.Should().BeGreaterThanOrEqualTo(0);
        filas[1].Should().BeEquivalentTo(new { Producto = "tramites", Fuente = "vehiculo", Proveedor = "", Resultado = "error" });
    }

    [Fact]
    public async Task HU13345_ElConsumoAgregadoTraeSoloLaEmpresaPedida_PorProductoYFuente()
    {
        Assert.SkipWhen(_skip is not null, _skip ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var consultas = Cliente(Token("platform.consultas"));
        var pedido = new ConsultarVehiculoRequest { Placa = new Flit.Platform.Grpc.V1.Placa { Valor = "ABC123" } };
        await consultas.ConsultarVehiculoAsync(pedido, Empresa(_empresaRapida), cancellationToken: ct);
        await consultas.ConsultarVehiculoAsync(pedido, Empresa(_empresaRapida), cancellationToken: ct);
        await consultas.ConsultarVehiculoAsync(pedido, Empresa(_empresaConLento), deadline: DateTime.UtcNow.AddSeconds(3), cancellationToken: ct);

        var admin = new ConsultasAdminService.ConsultasAdminServiceClient(Canal(Token("platform.consultas.admin")));
        var consumo = await admin.ObtenerConsumoAsync(new ObtenerConsumoRequest
        {
            Desde = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTimeOffset(DateTimeOffset.UtcNow.AddHours(-1)),
            Hasta = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTimeOffset(DateTimeOffset.UtcNow.AddHours(1)),
        }, Empresa(_empresaRapida), cancellationToken: ct);

        consumo.Consumos.Should().ContainSingle();
        consumo.Consumos[0].Should().BeEquivalentTo(new { Producto = "tramites", Fuente = "vehiculo", Total = 2L, DesdeCache = 0L, Errores = 0L },
            o => o.ExcludingMissingMembers());
    }

    private static GuardarConfiguracionEmpresaRequest Guardar(string principal)
    {
        var config = new Flit.Consultas.Grpc.V1.ConfiguracionEmpresa { FuenteMultas = "internal", AvaluoPrincipal = "base_gravable" };
        config.Cadenas["vehicle_plate"] = new CadenaProveedores { Principal = principal };
        config.AvaluosHabilitados.AddRange(["fasecolda", "base_gravable"]);
        return new GuardarConfiguracionEmpresaRequest { Configuracion = config };
    }

    // ── Apoyo ────────────────────────────────────────────────────────────────────────────────────────────────────

    private ConsultasService.ConsultasServiceClient Cliente(string token) => new(Canal(token));

    private CallInvoker Canal(string token)
    {
        var channel = GrpcChannel.ForAddress($"http://localhost:{GrpcPort}", new GrpcChannelOptions { HttpHandler = _app!.GetTestServer().CreateHandler() });
        return channel.CreateCallInvoker().Intercept(m =>
        {
            m.Add("authorization", $"Bearer {token}");
            return m;
        });
    }

    private static Metadata Empresa(Guid tenant) => new() { { "x-flit-tenant-id", tenant.ToString() } };

    private static Api.Persistence.ConfiguracionEmpresa Config(Guid tenant, string cadenas) =>
        new() { TenantId = tenant, CadenasJson = cadenas, FuenteMultas = "external", ActualizadoEn = DateTimeOffset.UtcNow };

    private string Token(string scope) => new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
    {
        Issuer = Issuer,
        Audience = "consultas",
        Subject = new ClaimsIdentity([new Claim("sub", "svc-tramites"), new Claim("scope", scope)]),
        Expires = DateTime.UtcNow.AddMinutes(10),
        SigningCredentials = new SigningCredentials(_key, SecurityAlgorithms.RsaSha256),
    });

    /// <summary>Proveedor falso: responde al instante o nunca (hasta que lo cancelen).</summary>
    private sealed class Falso(string key, TimeSpan demora) : IConsultationProvider
    {
        public string Key => key;

        public async Task<ConsultationResult> ConsultAsync(ConsultationContext ctx, CancellationToken ct)
        {
            if (demora != TimeSpan.Zero)
                await Task.Delay(demora, ct);
            return new ConsultationResult(key, "green",
                [new ConsultationCheck("soat", "SOAT", "ok", key, null)],
                [new HydratedField("placa_vista", ctx.FieldValues.GetValueOrDefault("plate"), null)]);
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
