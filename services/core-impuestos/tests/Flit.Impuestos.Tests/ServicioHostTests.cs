using System.Net;
using Flit.Impuestos.Api;
using Flit.Impuestos.Api.Persistence;
using Flit.Impuestos.Tests.Seguridad;
using FluentAssertions;
using Grpc.Health.V1;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace Flit.Impuestos.Tests;

/// <summary>
/// HU #13340 (plantilla) y HU #14694 (M05) — core-impuestos arranca, expone /health y grpc.health.v1, sus migraciones crean
/// el esquema con outbox e inbox y el modelo completo de la R0 (plan §4.1), y sin su secreto svc-* o sin el certificado de Data
/// Protection no arranca y nombra la variable. Usa el Postgres de <c>ConnectionStrings__Core</c> con una base efímera propia
/// (sin motor: se omite en local y falla en CI).
/// </summary>
public sealed class ServicioHostTests : IAsyncLifetime
{
    private const int GrpcPort = 5999;
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
            b.Configuration.AddInMemoryCollection(Settings());
        });
        await _app.StartAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (_app is not null)
            await _app.DisposeAsync();
        if (_skip is null)
        {
            NpgsqlConnection.ClearAllPools();
            await using var db = new ImpuestosDb(new DbContextOptionsBuilder<ImpuestosDb>().UseNpgsql(_database).Options, new Api.EmpresaFija(null));
            await db.Database.EnsureDeletedAsync();
        }
    }

    [Fact]
    public async Task Arranca_ConSaludRest_YListoSoloDespuesDeMigrar()
    {
        Assert.SkipWhen(_skip is not null, _skip ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var client = _app!.GetTestClient();

        (await client.GetAsync("/health", ct)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetAsync("/health/ready", ct)).StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable, "todavía no migra");

        await Program.MigrateAsync(_app!);
        (await client.GetAsync("/health/ready", ct)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task LaPrimeraMigracion_CreaElEsquemaConOutboxEInbox()
    {
        Assert.SkipWhen(_skip is not null, _skip ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        await Program.MigrateAsync(_app!);

        await using var connection = new NpgsqlConnection(_database);
        await connection.OpenAsync(ct);
        await using var query = new NpgsqlCommand(
            "SELECT table_name FROM information_schema.tables WHERE table_schema = @s ORDER BY table_name", connection);
        query.Parameters.AddWithValue("s", ImpuestosDb.Schema);
        var tables = new List<string>();
        await using (var reader = await query.ExecuteReaderAsync(ct))
            while (await reader.ReadAsync(ct))
                tables.Add(reader.GetString(0));

        tables.Should().Contain(["__EFMigrationsHistory", "inbox", "outbox"], "las tablas propias del servicio se suman a estas");
    }

    [Fact]
    public async Task La_migracion_R0_crea_el_modelo_completo_y_las_llaves_de_data_protection()
    {
        Assert.SkipWhen(_skip is not null, _skip ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        await Program.MigrateAsync(_app!);

        await using var connection = new NpgsqlConnection(_database);
        await connection.OpenAsync(ct);
        await using var query = new NpgsqlCommand(
            "SELECT table_name FROM information_schema.tables WHERE table_schema = @s ORDER BY table_name", connection);
        query.Parameters.AddWithValue("s", ImpuestosDb.Schema);
        var tables = new List<string>();
        await using (var reader = await query.ExecuteReaderAsync(ct))
            while (await reader.ReadAsync(ct))
                tables.Add(reader.GetString(0));

        tables.Should().Contain(
        [
            "aprovisionamientos", "credenciales_empresa", "historial_aprovisionamiento", "configuracion_empresa", "liquidaciones",
            "liquidacion_items", "liquidacion_periodos", "recibos", "avisos_recibidos", "organismos", "fuentes_api", "notificaciones",
            "auditoria_lectura_backoffice", "data_protection_keys",
        ], "plan §4.1: el modelo completo va en la migración inicial R0_Modelo");
        await using var migraciones = new NpgsqlCommand("SELECT migration_id FROM impuestos.\"__EFMigrationsHistory\"", connection);
        var ids = new List<string>();
        await using (var reader = await migraciones.ExecuteReaderAsync(ct))
            while (await reader.ReadAsync(ct))
                ids.Add(reader.GetString(0));
        ids.Should().Contain(id => id.EndsWith("_R0_Modelo", StringComparison.Ordinal));
    }

    [Fact]
    public void Sin_el_certificado_de_data_protection_no_arranca_y_nombra_la_variable()
    {
        var settings = Settings();
        settings.Remove(CertificadoDePrueba.ClaveRuta);

        var act = () => Program.Build([], b =>
        {
            b.WebHost.UseTestServer();
            b.Configuration.AddInMemoryCollection(settings);
        });

        act.Should().Throw<InvalidOperationException>().WithMessage("*Impuestos__DataProtection__CertificadoRuta*")
            .Which.Message.Should().NotContain(CertificadoDePrueba.Clave, "el mensaje no revela la clave");
    }

    [Fact]
    public async Task ExponeGrpcHealthEnSuPuerto()
    {
        Assert.SkipWhen(_skip is not null, _skip ?? string.Empty);
        var channel = GrpcChannel.ForAddress($"http://localhost:{GrpcPort}", new GrpcChannelOptions { HttpHandler = _app!.GetTestServer().CreateHandler() });

        (await new Health.HealthClient(channel).CheckAsync(new HealthCheckRequest(), cancellationToken: TestContext.Current.CancellationToken))
            .Status.Should().Be(HealthCheckResponse.Types.ServingStatus.Serving);
    }

    [Fact]
    public void SinElSecretoDeSuClienteSvc_NoArranca_YNombraLaVariable()
    {
        var settings = Settings();
        settings.Remove("Platform:ServiceClient:ClientSecret");

        var act = () => Program.Build([], b =>
        {
            b.WebHost.UseTestServer();
            b.Configuration.AddInMemoryCollection(settings);
        });

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Platform__ServiceClient__ClientSecret*SVC_IMPUESTOS_CLIENT_SECRET*")
            .Which.Message.Should().NotContain("RABBITMQ_URL", "solo nombra lo que falta");
    }

    [Theory]
    [InlineData("Impuestos:Api:IntegrationToken", "Impuestos__Api__IntegrationToken*IMPUESTOS_SVC_INTEGRATION_TOKEN")]
    [InlineData("Impuestos:Api:BaseUrl", "Impuestos__Api__BaseUrl*IMPUESTOS_SVC_API_BASE_URL")]
    public void Sin_el_token_de_integracion_o_la_base_del_API_no_arranca_y_nombra_la_variable(string clave, string patron)
    {
        var settings = Settings();
        settings.Remove(clave);

        var act = () => Program.Build([], b =>
        {
            b.WebHost.UseTestServer();
            b.Configuration.AddInMemoryCollection(settings);
        });

        act.Should().Throw<InvalidOperationException>().WithMessage($"*{patron}*")
            .Which.Message.Should().NotContain("token-integracion-de-prueba-no-real", "el mensaje no revela secretos");
    }

    [Fact]
    public void Sin_la_clave_del_certificado_no_arranca_y_nombra_la_variable_del_env()
    {
        var settings = Settings();
        settings.Remove(CertificadoDePrueba.ClaveClave);

        var act = () => Program.Build([], b =>
        {
            b.WebHost.UseTestServer();
            b.Configuration.AddInMemoryCollection(settings);
        });

        act.Should().Throw<InvalidOperationException>().WithMessage("*Impuestos__DataProtection__CertificadoClave*IMPUESTOS_SVC_DP_CERTIFICADO_CLAVE*");
    }

    private Dictionary<string, string?> Settings() => new()
    {
        ["ConnectionStrings:Servicio"] = _database,
        ["Platform:ServiceClient:ClientSecret"] = "secreto-de-prueba",
        ["Platform:ServiceClient:TokenEndpoint"] = "http://core-identity/connect/token",
        ["Platform:Auth:JwksUri"] = "http://core-identity/.well-known/jwks.json",
        ["Platform:Auth:Issuers:0"] = "https://hub.prueba/",
        ["Platform:Messaging:ConnectionString"] = "amqp://flit:x@127.0.0.1:1/",
        [ServicioSettings.GrpcPortKey] = GrpcPort.ToString(System.Globalization.CultureInfo.InvariantCulture),
        [CertificadoDePrueba.ClaveRuta] = CertificadoDePrueba.Ruta,
        [CertificadoDePrueba.ClaveClave] = CertificadoDePrueba.Clave,
        ["Impuestos:Api:BaseUrl"] = "https://api.prueba",
        ["Impuestos:Api:IntegrationToken"] = "token-integracion-de-prueba-no-real",
        ["Impuestos:Aprovisionamiento:TrabajadorActivo"] = "false",
    };
}
