using System.Text;
using FluentAssertions;
using Flit.Api.Consultas;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Repositories;
using Flit.Integration.Tests.Postgres;
using Flit.Platform.Sdk.Messaging;
using Flit.Tramites.Application.Identity;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using RabbitMQ.Client;
using Xunit;

namespace Flit.Integration.Tests.Integrations;

/// <summary>
/// HU #13351 (Epic #13316) AC1 «And Trámites actualiza el trámite que la pidió» — contra la base migrada de verdad y
/// RabbitMQ real: el aviso que publica Consultas en <c>flit.consultas</c> llega al consumidor de core-api, se aplica a
/// la validación con la lógica del webhook y queda en <c>tramites.inbox</c>; repetido, no se aplica dos veces. Broker de
/// <c>FLIT_TEST_RABBITMQ</c>; sin broker alcanzable se omite.
/// </summary>
public sealed class AvisosKyverumPostgresTests(PostgresDatabaseFixture fixture) : PostgresTestBase(fixture)
{
    private static readonly string RabbitMq = Environment.GetEnvironmentVariable("FLIT_TEST_RABBITMQ") ?? "amqp://flit:flit-prueba@127.0.0.1:5672/";
    private static readonly Guid Empresa = Guid.NewGuid();

    [PostgresFact]
    public async Task AC1_ElAvisoDeConsultas_ActualizaLaValidacion_UnaSolaVez()
    {
        var ct = TestContext.Current.CancellationToken;
        try
        {
            await using var probe = await new ConnectionFactory { Uri = new Uri(RabbitMq) }.CreateConnectionAsync(ct);
        }
        catch (Exception ex) when (ex is RabbitMQ.Client.Exceptions.BrokerUnreachableException or System.Net.Sockets.SocketException)
        {
            Assert.Skip($"RabbitMQ no alcanzable ({ex.Message})");
        }

        var validacion = await SembrarAsync(ct);
        var cola = $"tramites.prueba-avisos{Guid.NewGuid():N}"[..36];
        await using var provider = Servicios(cola);
        var hosted = provider.GetServices<IHostedService>().ToList();
        foreach (var h in hosted)
            await h.StartAsync(ct);
        try
        {
            await EsperarAsync(async () => await ConsumidoresAsync(cola) == 1);
            var sobre = Sobre(validacion);
            await PublicarAsync(sobre, ct);
            await PublicarAsync(sobre, ct); // reentrega

            await EsperarAsync(async () =>
            {
                await using var db = NewContext();
                return await db.Set<ProcedureInstanceBiometricValidation>().AnyAsync(v => v.Id == validacion && v.Status == BiometricEstados.Aprobado, ct)
                    && await db.Set<InboxMessage>().AnyAsync(m => m.EventId == sobre.EventId, ct);
            });

            await Task.Delay(500, ct);
            await using var verify = NewContext();
            var fila = await verify.Set<ProcedureInstanceBiometricValidation>().SingleAsync(v => v.Id == validacion, ct);
            fila.Score.Should().Be(93);
            (await verify.Set<InboxMessage>().CountAsync(m => m.EventId == sobre.EventId, ct)).Should().Be(1, "el repetido no se aplica otra vez");
        }
        finally
        {
            foreach (var h in hosted)
                await h.StopAsync(CancellationToken.None);
            await using var limpieza = await new ConnectionFactory { Uri = new Uri(RabbitMq) }.CreateConnectionAsync(CancellationToken.None);
            await using var canal = await limpieza.CreateChannelAsync(cancellationToken: CancellationToken.None);
            foreach (var c in new[] { cola, $"{cola}.retry.1", $"{cola}.retry.2", $"{cola}.retry.3", $"{cola}.dlq" })
                await canal.QueueDeleteAsync(c, cancellationToken: CancellationToken.None);
            await canal.ExchangeDeleteAsync($"{cola}.reintentos", cancellationToken: CancellationToken.None);
        }
    }

    private ServiceProvider Servicios(string cola)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped(_ => NewContext());
        services.AddScoped<IProcedureInstanceRepository>(sp => new ProcedureInstanceRepository(sp.GetRequiredService<FlitDbContext>()));
        services.AddScoped(_ => new IdentityValidationResultApplier(Substitute.For<IIdentityValidationEventPublisher>()));
        services.AddSingleton(Substitute.For<IIdentityValidationAuditLog>());
        services.AddSingleton(Substitute.For<IWebhookSecretProtector>());
        services.AddSingleton(Substitute.For<IKyverumVerifyClient>());
        services.AddScoped<KyverumWebhookHandler>();
        services.AddFlitConsumer<FlitDbContext, AvisoKyverumConsumer, AvisoKyverumVerify>(
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Platform:Messaging:Producer"] = "tramites",
                ["Platform:Messaging:ConnectionString"] = RabbitMq,
            }).Build(),
            cola, producer: "consultas", [AvisoKyverumConsumer.Tipo]);
        return services.BuildServiceProvider();
    }

    private async Task<Guid> SembrarAsync(CancellationToken ct)
    {
        await using var db = NewContext();
        db.Tenants.Add(TenantSeed.Lone(Empresa, "IT-AVISO-K"));
        await db.SaveChangesAsync(ct);
        var persona = Guid.NewGuid();
        db.Persons.Add(new Person
        {
            Id = persona, TenantId = Empresa, DocumentType = "CC", DocumentNumber = "1000000001", FullName = "Ana Prueba",
            Email = "ana@prueba.test", CreatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync(ct);
        var v = new ProcedureInstanceBiometricValidation
        {
            Id = Guid.NewGuid(),
            TenantId = Empresa,
            PersonId = persona, // prevalidación sin trámite (ancla: la persona)
            PartyRole = "comprador",
            Name = "Ana Prueba",
            DocumentType = "CC",
            DocumentNumber = "1000000001",
            Email = "ana@prueba.test",
            Status = BiometricEstados.EnProceso,
            Provider = BiometricProviders.Kyverum,
            KyverumVerificationId = "kyv_it_1",
            TokenHash = Guid.NewGuid().ToString("N"),
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
            CreatedAt = DateTimeOffset.UtcNow,
        };
        db.Set<ProcedureInstanceBiometricValidation>().Add(v);
        await db.SaveChangesAsync(ct);
        return v.Id;
    }

    private static EventEnvelope Sobre(Guid validacion)
    {
        const string cuerpo = """{"evento":"validation.completed","requestId":"r1","data":{"aprobado":true,"closedAt":"2026-10-06T12:00:00Z","subjects":[{"rol":"comprador","status":"aprobado","score":93}]},"ts":"2026-10-06T12:00:01Z"}""";
        return new EventEnvelope(Guid.CreateVersion7(), AvisoKyverumConsumer.Tipo, 1, DateTimeOffset.UtcNow, Empresa, "consultas", "corr-aviso",
            System.Text.Json.JsonSerializer.SerializeToElement(
                new AvisoKyverumVerify(validacion, "kyv_it_1", "tramites", Guid.NewGuid(), cuerpo), EventEnvelope.JsonOptions));
    }

    private static async Task PublicarAsync(EventEnvelope sobre, CancellationToken ct)
    {
        await using var publicador = new RabbitMqEventPublisher(new PlatformMessagingOptions { Producer = "consultas", ConnectionString = RabbitMq });
        await publicador.PublishAsync(new OutboxMessage
        {
            Id = sobre.EventId, Exchange = "flit.consultas", RoutingKey = sobre.Type, Payload = sobre.ToJson(), OccurredAt = sobre.OccurredAt,
        }, ct);
    }

    private static async Task<uint> ConsumidoresAsync(string cola)
    {
        await using var conexion = await new ConnectionFactory { Uri = new Uri(RabbitMq) }.CreateConnectionAsync();
        await using var canal = await conexion.CreateChannelAsync();
        try
        {
            return await canal.ConsumerCountAsync(cola);
        }
        catch (RabbitMQ.Client.Exceptions.OperationInterruptedException)
        {
            return 0;
        }
    }

    private static async Task EsperarAsync(Func<Task<bool>> condicion)
    {
        var limite = DateTime.UtcNow.AddSeconds(15);
        while (!await condicion())
        {
            if (DateTime.UtcNow > limite)
                throw new TimeoutException("La condición no se cumplió a tiempo.");
            await Task.Delay(100);
        }
    }
}
