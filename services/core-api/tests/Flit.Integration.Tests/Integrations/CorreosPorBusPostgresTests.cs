using FluentAssertions;
using Flit.Admin.Domain.Companies.Settings;
using Flit.Infrastructure.Notifications.Bus;
using Flit.Infrastructure.Notifications.Routing;
using Flit.Infrastructure.Persistence;
using Flit.Integration.Tests.Postgres;
using Flit.Modules.Notificaciones;
using Flit.Modules.Security.Domain.Auth;
using Flit.Platform.Sdk.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using RabbitMQ.Client;
using Xunit;

namespace Flit.Integration.Tests.Integrations;

/// <summary>
/// HU #13355 (Epic #13316) AC1 — contra la base migrada de verdad y RabbitMQ real: con Notificaciones remoto, un correo
/// de core-api (outbox <c>tramites.outbox</c>) y uno de core-identity (outbox <c>identity.outbox</c>, que crea la migración
/// HU13355_IdentityOutbox) llegan como trabajos <c>notificaciones.email.send</c> a <c>flit.notificaciones</c>. Broker de
/// <c>FLIT_TEST_RABBITMQ</c>; sin broker alcanzable se omite.
/// </summary>
public sealed class CorreosPorBusPostgresTests(PostgresDatabaseFixture fixture) : PostgresTestBase(fixture)
{
    private static readonly string RabbitMq = Environment.GetEnvironmentVariable("FLIT_TEST_RABBITMQ") ?? "amqp://flit:flit-prueba@127.0.0.1:5672/";

    [PostgresFact]
    public async Task AC1_LosCorreosDeCoreApiYDeCoreIdentity_LleganComoTrabajosANotificaciones()
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
        await canal.ExchangeDeclareAsync("flit.notificaciones", ExchangeType.Topic, durable: true, cancellationToken: ct);
        var cola = (await canal.QueueDeclareAsync(string.Empty, durable: false, exclusive: true, autoDelete: true, cancellationToken: ct)).QueueName;
        await canal.QueueBindAsync(cola, "flit.notificaciones", TrabajoCorreo.Tipo, cancellationToken: ct);

        var empresa = Guid.NewGuid();
        await using var coreApi = Servicios<FlitDbContext>(NewContext, "tramites");
        await using var coreIdentity = Servicios<IdentityDbContext>(
            () =>
            {
                var options = new DbContextOptionsBuilder<IdentityDbContext>();
                NpgsqlConventions.Apply(options, Fixture.ConnectionString);
                return new IdentityDbContext(options.Options);
            },
            "plataforma");

        (await Enviar(coreApi, "tramites.aprobado", empresa, ct)).Success.Should().BeTrue();
        (await Enviar(coreIdentity, "security.forgot-password", empresa, ct)).Success.Should().BeTrue();

        var hosted = coreApi.GetServices<IHostedService>().Concat(coreIdentity.GetServices<IHostedService>()).ToList();
        foreach (var h in hosted)
            await h.StartAsync(ct);
        try
        {
            var recibidos = new List<EventEnvelope>();
            for (var i = 0; i < 100 && recibidos.Count < 2; i++)
            {
                var m = await canal.BasicGetAsync(cola, autoAck: true, ct);
                if (m is null)
                    await Task.Delay(100, ct);
                else
                    recibidos.Add(EventEnvelope.FromJson(m.Body.Span));
            }

            recibidos.Select(r => r.Producer).Should().BeEquivalentTo("tramites", "plataforma");
            recibidos.Should().OnlyContain(r => r.TenantId == empresa && r.Type == TrabajoCorreo.Tipo);
            recibidos.Single(r => r.Producer == "plataforma").DataAs<TrabajoCorreo>().Plantilla.Should().Be("security.forgot-password");
        }
        finally
        {
            foreach (var h in hosted)
                await h.StopAsync(CancellationToken.None);
        }
    }

    private static ServiceProvider Servicios<TContext>(Func<TContext> contexto, string productor)
        where TContext : DbContext, IIdentityDb
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped(_ => contexto());
        services.AddScoped<IIdentityDb>(sp => sp.GetRequiredService<TContext>());
        services.AddFlitOutbox<TContext>(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Platform:Messaging:Producer"] = productor,
            ["Platform:Messaging:ConnectionString"] = RabbitMq,
            ["Platform:Messaging:PollInterval"] = "00:00:00.200",
        }).Build());
        return services.BuildServiceProvider();
    }

    private static Task<EmailSendResult> Enviar(ServiceProvider provider, string plantilla, Guid empresa, CancellationToken ct) =>
        new CorreoPorBusEmailSender(new SinEnvio(), new CanalFlit(), provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<CorreoPorBusEmailSender>.Instance)
            .SendAsync(new EmailMessage(empresa, plantilla, "persona@prueba.test", "Persona", "Asunto", "<p>Hola</p>"), ct);

    private sealed class SinEnvio : IEmailSender
    {
        public Task<EmailSendResult> SendAsync(EmailMessage message, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Con empresa, nada sale en proceso.");
    }

    private sealed class CanalFlit : INotificationChannelResolver
    {
        public Task<NotificationChannel> ResolveAsync(Guid? tenantId, CancellationToken cancellationToken) => Task.FromResult(NotificationChannel.FlitSmtp);
    }
}
