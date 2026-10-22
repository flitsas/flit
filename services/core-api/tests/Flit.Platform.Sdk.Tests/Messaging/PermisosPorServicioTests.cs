using System.Diagnostics;
using System.Text;
using Flit.Platform.Sdk.Messaging;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;
using Xunit;

namespace Flit.Platform.Sdk.Tests.Messaging;

/// <summary>
/// HU #13349 (Epic #13316) AC2 — con el usuario de un servicio creado por <c>deploy/rabbitmq/usuario-de-servicio.sh</c>,
/// el broker deja publicar en su exchange y rechaza el de otro servicio. Necesita un broker con
/// <c>deploy/rabbitmq/definitions.json</c> cargado y la cadena del usuario <c>consultas</c> en
/// <c>FLIT_TEST_RABBITMQ_CONSULTAS</c> (p. ej. <c>amqp://consultas:clave@127.0.0.1:5673/flit</c>; usuario creado con
/// trabajos a <c>notificaciones</c>); sin ella se omite.
/// </summary>
public sealed class PermisosPorServicioTests
{
    private static readonly string? Cadena = Environment.GetEnvironmentVariable("FLIT_TEST_RABBITMQ_CONSULTAS");

    [Fact]
    public async Task PublicarEnElExchangeDeOtroServicio_LoRechazaElBroker()
    {
        Assert.SkipWhen(Cadena is null, "Sin FLIT_TEST_RABBITMQ_CONSULTAS");
        await using var conexion = await new ConnectionFactory { Uri = new Uri(Cadena!) }.CreateConnectionAsync(TestContext.Current.CancellationToken);
        await using var canal = await conexion.CreateChannelAsync(
            new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true), TestContext.Current.CancellationToken);

        var publicar = async () => await canal.BasicPublishAsync("flit.tramites", "tramites.procedure.state_changed", Encoding.UTF8.GetBytes("{}"), TestContext.Current.CancellationToken);

        var error = (await publicar.Should().ThrowAsync<Exception>()).Which;
        Rechazado(error).Should().BeTrue($"el broker debía rechazar con 403 y respondió {error.GetType().Name}: {error.Message}");
    }

    [Fact]
    public async Task ElPublicadorDelSdk_PublicaEnSuPropioExchange()
    {
        Assert.SkipWhen(Cadena is null, "Sin FLIT_TEST_RABBITMQ_CONSULTAS");
        await using var publicador = new RabbitMqEventPublisher(new PlatformMessagingOptions { Producer = "consultas", ConnectionString = Cadena! });
        var sobre = new EventEnvelope(Guid.CreateVersion7(), "consultas.consulta.realizada", 1, DateTimeOffset.UtcNow, Guid.NewGuid(), "consultas", "corr",
            System.Text.Json.JsonSerializer.SerializeToElement(new { fuente = "vehiculo" }));

        var publicar = () => publicador.PublishAsync(new OutboxMessage
        {
            Id = sobre.EventId, Exchange = "flit.consultas", RoutingKey = sobre.Type, Payload = sobre.ToJson(), OccurredAt = sobre.OccurredAt,
        }, TestContext.Current.CancellationToken);

        await publicar.Should().NotThrowAsync();
    }

    [Fact]
    public async Task HU13354_ConPermisoDeTrabajos_ElPublicadorDelSdk_DejaUnTrabajoEnFlitNotificaciones()
    {
        // Usuario creado con «usuario-de-servicio.sh consultas <clave> notificaciones».
        Assert.SkipWhen(Cadena is null, "Sin FLIT_TEST_RABBITMQ_CONSULTAS");
        await using var publicador = new RabbitMqEventPublisher(new PlatformMessagingOptions { Producer = "consultas", ConnectionString = Cadena! });
        var sobre = new EventEnvelope(Guid.CreateVersion7(), "notificaciones.email.send", 1, DateTimeOffset.UtcNow, Guid.NewGuid(), "consultas", "corr",
            System.Text.Json.JsonSerializer.SerializeToElement(new { plantilla = "x" }));

        var publicar = () => publicador.PublishAsync(new OutboxMessage
        {
            Id = sobre.EventId, Exchange = "flit.notificaciones", RoutingKey = sobre.Type, Payload = sobre.ToJson(), OccurredAt = sobre.OccurredAt,
        }, TestContext.Current.CancellationToken);

        await publicar.Should().NotThrowAsync("declara el exchange ajeno en modo pasivo y tiene permiso de escritura");
    }

    [Fact]
    public async Task DeclararUnaColaDeOtroServicio_LoRechazaElBroker()
    {
        Assert.SkipWhen(Cadena is null, "Sin FLIT_TEST_RABBITMQ_CONSULTAS");
        await using var conexion = await new ConnectionFactory { Uri = new Uri(Cadena!) }.CreateConnectionAsync(TestContext.Current.CancellationToken);
        await using var canal = await conexion.CreateChannelAsync(cancellationToken: TestContext.Current.CancellationToken);

        var declarar = async () => await canal.QueueDeclareAsync("notificaciones.correo", durable: true, exclusive: false, autoDelete: false, cancellationToken: TestContext.Current.CancellationToken);

        (await declarar.Should().ThrowAsync<OperationInterruptedException>()).Which.ShutdownReason!.ReplyCode.Should().Be(403);
    }

    private static bool Rechazado(Exception e) => e switch
    {
        OperationInterruptedException oie => oie.ShutdownReason?.ReplyCode == 403,
        PublishException => true,
        _ => false,
    };
}

/// <summary>
/// HU #13349 AC2 — el consumidor del SDK con el usuario restringido de un servicio: no puede declarar el exchange del
/// productor (403 en configure) y aun así ata su cola a él y recibe los eventos. Necesita además Postgres y la cadena
/// de administración (<c>FLIT_TEST_RABBITMQ</c>) con permisos en el vhost <c>flit</c>, para publicar como el productor.
/// </summary>
public sealed class ConsumidorConUsuarioDeServicioTests(MessagingFixture fixture) : IClassFixture<MessagingFixture>
{
    private static readonly string? Cadena = Environment.GetEnvironmentVariable("FLIT_TEST_RABBITMQ_CONSULTAS");

    [Fact]
    public async Task ElConsumidorDeConsultas_RecibeEventosDeTramites_SinPermisoParaDeclararSuExchange()
    {
        Assert.SkipWhen(Cadena is null, "Sin FLIT_TEST_RABBITMQ_CONSULTAS");
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var cola = $"consultas.prueba{Guid.NewGuid():N}"[..32];
        const string tipo = "tramites.prueba.permisos";
        var admin = new UriBuilder(fixture.RabbitMq) { Path = "/flit" }.Uri.ToString();

        await using (var db = fixture.NewDb())
            await db.Database.EnsureCreatedAsync(ct);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<PruebaDb>(o => o.UseNpgsql(fixture.Postgres));
        services.AddSingleton(new Comportamiento());
        services.AddFlitConsumer<PruebaDb, EfectoDePrueba, PedidoCreado>(
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Platform:Messaging:Producer"] = "consultas",
                ["Platform:Messaging:ConnectionString"] = Cadena,
            }).Build(),
            cola, producer: "tramites", [tipo]);
        await using var provider = services.BuildServiceProvider();
        var hosted = provider.GetServices<IHostedService>().ToList();
        foreach (var h in hosted)
            await h.StartAsync(ct);

        try
        {
            await using var conexion = await new ConnectionFactory { Uri = new Uri(admin) }.CreateConnectionAsync(ct);
            await Eventually(async () =>
            {
                await using var probe = await conexion.CreateChannelAsync();
                try { return await probe.ConsumerCountAsync(cola) == 1; }
                catch (OperationInterruptedException) { return false; }
            });

            var sobre = new EventEnvelope(Guid.CreateVersion7(), tipo, 1, DateTimeOffset.UtcNow, Guid.NewGuid(), "tramites", "corr-permisos",
                System.Text.Json.JsonSerializer.SerializeToElement(new PedidoCreado("uno"), EventEnvelope.JsonOptions));
            await using (var publicador = new RabbitMqEventPublisher(new PlatformMessagingOptions { Producer = "tramites", ConnectionString = admin }))
            {
                await publicador.PublishAsync(new OutboxMessage
                {
                    Id = sobre.EventId, Exchange = "flit.tramites", RoutingKey = tipo, Payload = sobre.ToJson(), OccurredAt = sobre.OccurredAt,
                }, ct);
            }

            await Eventually(async () =>
            {
                await using var db = fixture.NewDb();
                return await db.Set<Efecto>().CountAsync(e => e.EventId == sobre.EventId) == 1;
            });
        }
        finally
        {
            foreach (var h in hosted)
                await h.StopAsync(CancellationToken.None);
            await using var limpieza = await new ConnectionFactory { Uri = new Uri(admin) }.CreateConnectionAsync(CancellationToken.None);
            await using var canal = await limpieza.CreateChannelAsync(cancellationToken: CancellationToken.None);
            foreach (var c in new[] { cola, $"{cola}.retry.1", $"{cola}.retry.2", $"{cola}.retry.3", $"{cola}.dlq" })
                await canal.QueueDeleteAsync(c, cancellationToken: CancellationToken.None);
            await canal.ExchangeDeleteAsync($"{cola}.reintentos", cancellationToken: CancellationToken.None);
        }
    }

    private static async Task Eventually(Func<Task<bool>> condition)
    {
        var limit = Stopwatch.StartNew();
        while (!await condition())
        {
            if (limit.Elapsed > TimeSpan.FromSeconds(10))
                throw new TimeoutException("La condición no se cumplió a tiempo.");
            await Task.Delay(50);
        }
    }
}
