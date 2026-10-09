using System.Net;
using Flit.Plantilla.Api;
using Flit.Plantilla.Api.Persistence;
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

namespace Flit.Plantilla.Tests;

/// <summary>
/// HU #13340 (Epic #13316) — un servicio de la plantilla arranca, expone /health y grpc.health.v1, su primera migración
/// crea su esquema con outbox e inbox, y sin su secreto svc-* no arranca y nombra la variable. Usa el Postgres de
/// <c>ConnectionStrings__Core</c> con una base efímera propia (sin motor: se omite en local y falla en CI).
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
            await using var db = new PlantillaDb(new DbContextOptionsBuilder<PlantillaDb>().UseNpgsql(_database).Options);
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
        query.Parameters.AddWithValue("s", PlantillaDb.Schema);
        var tables = new List<string>();
        await using (var reader = await query.ExecuteReaderAsync(ct))
            while (await reader.ReadAsync(ct))
                tables.Add(reader.GetString(0));

        tables.Should().Contain(["__EFMigrationsHistory", "inbox", "outbox"], "las tablas propias del servicio se suman a estas");
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
            .WithMessage("*Platform__ServiceClient__ClientSecret*SVC_PLANTILLA_CLIENT_SECRET*")
            .Which.Message.Should().NotContain("RABBITMQ_URL", "solo nombra lo que falta");
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
    };
}
