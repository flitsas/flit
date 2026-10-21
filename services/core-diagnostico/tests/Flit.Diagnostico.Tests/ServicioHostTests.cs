using System.Net;
using System.Reflection;
using Flit.Diagnostico.Api;
using Flit.Diagnostico.Api.Persistence;
using Flit.Diagnostico.Tests.Api;
using FluentAssertions;
using Grpc.Health.V1;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace Flit.Diagnostico.Tests;

/// <summary>
/// HU #13721 (F00.1-H1) — core-diagnostico nace de la plantilla flit-servicio: arranca con sus migraciones en el esquema
/// <c>diagnostico</c> (outbox, inbox e historial dentro de él, nada en <c>public</c>), expone /health, /health/ready y
/// grpc.health.v1, exige un token de audiencia <c>diagnostico</c> en sus rutas de negocio y su contrato gRPC se genera
/// desde <c>contracts/proto</c>. Las pruebas con base usan el Postgres de <c>ConnectionStrings__Core</c> (base efímera).
/// </summary>
/// <example>
/// <code>
/// dotnet test services/core-diagnostico/Flit.Diagnostico.slnx --filter FullyQualifiedName~ServicioHostTests
/// </code>
/// </example>
public sealed class ServicioHostTests
{
    private const string RutaDeNegocio = Program.Prefijo + "/diagnosticos";

    // Escenario 1 — arranque con migraciones en el esquema propio.
    [Fact]
    public async Task Arranca_y_aplica_migraciones_en_schema_diagnostico()
    {
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;

        await using (var scope = api.App.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DiagnosticoDb>();
            (await db.Database.GetPendingMigrationsAsync(ct)).Should().BeEmpty("el arranque aplica todas las migraciones");
            (await db.Database.GetAppliedMigrationsAsync(ct)).Should().ContainSingle(m => m.EndsWith("_D0_Inicial", StringComparison.Ordinal),
                "la ola D0 tiene una sola migración");
        }

        (await ConsultarAsync(api.BaseDeDatos, "SELECT schema_name FROM information_schema.schemata WHERE schema_name = @s", DiagnosticoDb.Schema, ct))
            .Should().Equal(DiagnosticoDb.Schema);
        (await ConsultarAsync(api.BaseDeDatos, "SELECT table_name FROM information_schema.tables WHERE table_schema = @s", "public", ct))
            .Should().BeEmpty("el servicio no crea nada en public");
    }

    [Fact]
    public async Task Outbox_inbox_e_historial_viven_en_schema_diagnostico()
    {
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;

        (await ConsultarAsync(api.BaseDeDatos, "SELECT table_name FROM information_schema.tables WHERE table_schema = @s", DiagnosticoDb.Schema, ct))
            .Should().Contain(["__EFMigrationsHistory", "inbox", "outbox"]);
        (await ConsultarAsync(api.BaseDeDatos,
                "SELECT table_schema || '.' || table_name FROM information_schema.tables " +
                "WHERE table_name IN ('__EFMigrationsHistory', 'inbox', 'outbox') AND table_schema <> @s",
                DiagnosticoDb.Schema, ct))
            .Should().BeEmpty("outbox, inbox e historial no viven fuera del esquema diagnostico");
    }

    // Escenario 2 — salud del servicio con Postgres disponible.
    [Fact]
    public async Task Health_responde_200()
    {
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var client = api.Anonimo();

        (await client.GetAsync("/health", ct)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetAsync("/health/ready", ct)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // Escenario 3 — readiness sin base de datos.
    [Fact]
    public async Task HealthReady_responde_503_sin_postgres()
    {
        await using var api = await ApiDePrueba.SinBaseAsync();
        var ct = TestContext.Current.CancellationToken;
        var client = api.Anonimo();

        (await client.GetAsync("/health/ready", ct)).StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await client.GetAsync("/health", ct)).StatusCode.Should().Be(HttpStatusCode.OK, "la vida del proceso no depende de la base");
    }

    // Escenario 4 — autenticación del SDK activa en las rutas de negocio.
    [Fact]
    public async Task Ruta_de_negocio_sin_token_responde_401()
    {
        await using var api = await ApiDePrueba.SinBaseAsync();
        var ct = TestContext.Current.CancellationToken;

        (await api.Anonimo().GetAsync(RutaDeNegocio, ct)).StatusCode.Should().Be(HttpStatusCode.Unauthorized, "sin token");
        (await api.Cliente(api.Token(audiencia: "comparendos")).GetAsync(RutaDeNegocio, ct)).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized, "un token de otro producto no sirve en diagnostico");
        (await api.Cliente(api.Token()).GetAsync(RutaDeNegocio, ct)).StatusCode
            .Should().NotBe(HttpStatusCode.Unauthorized, "con token de diagnostico la autenticación pasa");
    }

    [Fact]
    public async Task ExponeGrpcHealthEnSuPuerto()
    {
        await using var api = await ApiDePrueba.SinBaseAsync();
        var channel = GrpcChannel.ForAddress($"http://localhost:{ApiDePrueba.GrpcPort}", new GrpcChannelOptions { HttpHandler = api.App.GetTestServer().CreateHandler() });

        (await new Health.HealthClient(channel).CheckAsync(new HealthCheckRequest(), cancellationToken: TestContext.Current.CancellationToken))
            .Status.Should().Be(HealthCheckResponse.Types.ServingStatus.Serving);
    }

    [Fact]
    public void SinElSecretoDeSuClienteSvc_NoArranca_YNombraLaVariable()
    {
        var settings = ApiDePrueba.Configuracion(ApiDePrueba.BaseInalcanzable);
        settings.Remove("Platform:ServiceClient:ClientSecret");

        var act = () => Program.Build([], b =>
        {
            b.WebHost.UseTestServer();
            b.Configuration.AddInMemoryCollection(settings);
        });

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Platform__ServiceClient__ClientSecret*SVC_DIAGNOSTICO_CLIENT_SECRET*")
            .Which.Message.Should().NotContain("RABBITMQ_URL", "solo nombra lo que falta");
    }

    // Escenario 5 — el contrato gRPC de contracts/proto/flit/diagnostico/v1 se genera en Flit.Diagnostico.Grpc.Contracts.
    [Fact]
    public void Contrato_grpc_de_diagnostico_se_genera_desde_contracts_proto()
    {
        Type.GetType("Flit.Diagnostico.Grpc.V1.DiagnosticoService, Flit.Diagnostico.Grpc.Contracts")
            .Should().NotBeNull("flit.diagnostico.v1 define DiagnosticoService");
    }

    [Fact]
    public void Domain_no_depende_de_infraestructura()
    {
        var referencias = Assembly.Load(new AssemblyName("Flit.Diagnostico.Domain")).GetReferencedAssemblies().Select(a => a.Name ?? string.Empty);

        referencias.Should().NotContain(n =>
            n.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal)
            || n.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal)
            || n.StartsWith("Npgsql", StringComparison.Ordinal)
            || n.StartsWith("Grpc", StringComparison.Ordinal)
            || n.StartsWith("Flit.Platform", StringComparison.Ordinal)
            || n.StartsWith("Flit.Diagnostico.Api", StringComparison.Ordinal));
    }

    private static async Task<List<string>> ConsultarAsync(string baseDeDatos, string sql, string parametro, CancellationToken ct)
    {
        await using var connection = new NpgsqlConnection(baseDeDatos);
        await connection.OpenAsync(ct);
        await using var query = new NpgsqlCommand(sql, connection);
        query.Parameters.AddWithValue("s", parametro);
        var filas = new List<string>();
        await using var reader = await query.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            filas.Add(reader.GetString(0));
        return filas;
    }
}
