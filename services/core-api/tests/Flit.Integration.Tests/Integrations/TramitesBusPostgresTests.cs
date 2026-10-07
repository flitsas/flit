using FluentAssertions;
using Flit.Infrastructure.Messaging;
using Flit.Infrastructure.Persistence;
using Flit.Integration.Tests.Postgres;
using Flit.Platform.Sdk.Messaging;
using Flit.Tramites.Domain.Tramites.Estados;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RabbitMQ.Client;
using Xunit;

namespace Flit.Integration.Tests.Integrations;

/// <summary>
/// HU #13350 (Epic #13316) AC1 — contra la base migrada de verdad y RabbitMQ real: un cambio de estado confirmado
/// sale en <c>flit.tramites</c> como <c>tramites.procedure.state_changed</c>; uno que hace rollback no sale. El broker
/// es el de <c>FLIT_TEST_RABBITMQ</c> (el servicio del CI de core-api); sin broker alcanzable se omite.
/// </summary>
public sealed class TramitesBusPostgresTests(PostgresDatabaseFixture fixture) : PostgresTestBase(fixture)
{
    private static readonly string RabbitMq = Environment.GetEnvironmentVariable("FLIT_TEST_RABBITMQ") ?? "amqp://flit:flit-prueba@127.0.0.1:5672/";

    [PostgresFact]
    public async Task AC1_UnCambioConfirmado_SalePorFlitTramites_YUnoRevertidoNo()
    {
        var ct = TestContext.Current.CancellationToken;
        IConnection rabbit;
        try
        {
            rabbit = await new ConnectionFactory { Uri = new Uri(RabbitMq) }.CreateConnectionAsync(ct);
        }
        catch (Exception ex) when (ex is RabbitMQ.Client.Exceptions.BrokerUnreachableException or System.Net.Sockets.SocketException)
        {
            Assert.Skip($"RabbitMQ no alcanzable ({ex.Message})");
            return;
        }

        await using var _ = rabbit;
        await using var canal = await rabbit.CreateChannelAsync(cancellationToken: ct);
        await canal.ExchangeDeclareAsync("flit.tramites", ExchangeType.Topic, durable: true, cancellationToken: ct);
        var cola = (await canal.QueueDeclareAsync(string.Empty, durable: false, exclusive: true, autoDelete: true, cancellationToken: ct)).QueueName;
        await canal.QueueBindAsync(cola, "flit.tramites", TramitesEventos.EstadoCambiado, cancellationToken: ct);

        await using var provider = Servicios();
        var confirmado = Guid.NewGuid();
        var revertido = Guid.NewGuid();
        await TransicionAsync(provider, revertido, confirmar: false, ct);
        await TransicionAsync(provider, confirmado, confirmar: true, ct);

        await using (var db = NewContext())
            (await db.Set<OutboxMessage>().CountAsync(ct)).Should().Be(1, "la transición revertida no deja evento");

        var hosted = provider.GetServices<IHostedService>().ToList();
        foreach (var h in hosted)
            await h.StartAsync(ct);
        try
        {
            BasicGetResult? recibido = null;
            for (var i = 0; i < 100 && recibido is null; i++)
            {
                recibido = await canal.BasicGetAsync(cola, autoAck: true, ct);
                if (recibido is null)
                    await Task.Delay(100, ct);
            }

            recibido.Should().NotBeNull("el publicador del SDK debía sacar el evento de tramites.outbox");
            var sobre = EventEnvelope.FromJson(recibido!.Body.Span);
            sobre.Type.Should().Be(TramitesEventos.EstadoCambiado);
            sobre.Producer.Should().Be("tramites");
            sobre.Data.GetProperty("procedureInstanceId").GetGuid().Should().Be(confirmado);
            (await canal.BasicGetAsync(cola, autoAck: true, ct)).Should().BeNull("solo salió el confirmado");

            await using var db = NewContext();
            (await db.Set<OutboxMessage>().SingleAsync(ct)).PublishedAt.Should().NotBeNull();
        }
        finally
        {
            foreach (var h in hosted)
                await h.StopAsync(CancellationToken.None);
        }
    }

    private ServiceProvider Servicios()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped(_ => NewContext());
        services.AddTramitesBus(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Tramites:Bus:Habilitado"] = "true",
            ["Platform:Messaging:Producer"] = "tramites",
            ["Platform:Messaging:ConnectionString"] = RabbitMq,
            ["Platform:Messaging:PollInterval"] = "00:00:00.200",
        }).Build(), "inprocess");
        return services.BuildServiceProvider();
    }

    private static async Task TransicionAsync(ServiceProvider provider, Guid instancia, bool confirmar, CancellationToken ct)
    {
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FlitDbContext>();
        var publisher = new ProcedureStateChangeOutboxPublisher(
            // Sin la outbox propia: su fila exige un trámite real (FK) y aquí solo importa el evento del bus.
            db, scope.ServiceProvider.GetRequiredService<IPlatformOutbox>(), new TramitesBusOptions { Habilitado = true, EntregaEnProceso = false });
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await publisher.EnqueueAsync(new TramiteTransitionRecord(
            Guid.NewGuid(), instancia, TramiteEstado.Borrador, TramiteEstado.Preparado, null, null, DateTimeOffset.UtcNow), ct);
        await db.SaveChangesAsync(ct);
        if (confirmar)
            await tx.CommitAsync(ct);
        else
            await tx.RollbackAsync(ct);
    }
}
