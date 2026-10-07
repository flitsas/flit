using System.Text.Json;
using FluentAssertions;
using Flit.Infrastructure.Messaging;
using Flit.Infrastructure.Persistence;
using Flit.Platform.Sdk.Messaging;
using Flit.Tramites.Application.Identity.Events;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Tramites.Estados;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Flit.Infrastructure.Tests.Messaging;

/// <summary>
/// HU #13350 (Epic #13316) — Trámites encola sus eventos para <c>flit.tramites</c> en la outbox del SDK, en la misma
/// unidad de trabajo que el cambio (AC1), sin dejar de llenar la outbox propia de la que salen el correo, el webhook
/// del OT y el reflejo a ICT (AC2). La publicación al broker la cubren las pruebas del SDK contra RabbitMQ real.
/// </summary>
public sealed class TramitesBusTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid InstanceId = Guid.NewGuid();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AC1_ConElBusEncendido_ElCambioDeEstadoVaALaOutboxDelSdk_EnLaMismaUnidadDeTrabajo()
    {
        var dbName = NewDbName();
        await using var provider = Servicios(dbName, habilitado: true);
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FlitDbContext>();
        var publisher = new ProcedureStateChangeOutboxPublisher(
            db, scope.ServiceProvider.GetRequiredService<IPlatformOutbox>(), scope.ServiceProvider.GetRequiredService<TramitesBusOptions>());

        await publisher.EnqueueAsync(Registro(), Ct);

        // Sin SaveChanges no hay nada en la base: si la transición hace rollback, el evento desaparece con ella.
        await using (var verify = NewContext(dbName))
            (await verify.Set<OutboxMessage>().CountAsync(Ct)).Should().Be(0);

        await db.SaveChangesAsync(Ct);
        await using (var verify = NewContext(dbName))
        {
            var mensaje = await verify.Set<OutboxMessage>().SingleAsync(Ct);
            mensaje.Exchange.Should().Be("flit.tramites");
            mensaje.RoutingKey.Should().Be("tramites.procedure.state_changed");
            mensaje.PublishedAt.Should().BeNull();

            var sobre = JsonDocument.Parse(mensaje.Payload).RootElement;
            sobre.GetProperty("type").GetString().Should().Be("tramites.procedure.state_changed");
            sobre.GetProperty("producer").GetString().Should().Be("tramites");
            sobre.GetProperty("tenantId").GetGuid().Should().Be(TenantId);
            var datos = sobre.GetProperty("data");
            Propiedades(datos).Should().BeEquivalentTo("procedureInstanceId", "fromStatus", "toStatus", "reason", "changedByUserId");
            datos.GetProperty("procedureInstanceId").GetGuid().Should().Be(InstanceId);
            datos.GetProperty("fromStatus").GetString().Should().Be(TramiteEstado.Borrador);
            datos.GetProperty("toStatus").GetString().Should().Be(TramiteEstado.Preparado);
            datos.GetProperty("reason").GetString().Should().Be("gates ok");

            (await verify.ProcedureStateChangeOutbox.CountAsync(Ct)).Should().Be(1, "la entrega en proceso sigue (AC2)");
        }
    }

    [Fact]
    public async Task ConElBusApagado_SoloSeLlenaLaOutboxPropia()
    {
        var dbName = NewDbName();
        await using var db = NewContext(dbName);

        await new ProcedureStateChangeOutboxPublisher(db).EnqueueAsync(Registro(), Ct);
        await db.SaveChangesAsync(Ct);

        await using var verify = NewContext(dbName);
        (await verify.ProcedureStateChangeOutbox.CountAsync(Ct)).Should().Be(1);
        (await verify.Set<OutboxMessage>().CountAsync(Ct)).Should().Be(0);
    }

    [Fact]
    public async Task ConLaEntregaEnProcesoApagada_SoloSaleElEventoDelBus()
    {
        var dbName = NewDbName();
        await using var provider = Servicios(dbName, habilitado: true, entregaEnProceso: false);
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FlitDbContext>();
        var publisher = new ProcedureStateChangeOutboxPublisher(
            db, scope.ServiceProvider.GetRequiredService<IPlatformOutbox>(), scope.ServiceProvider.GetRequiredService<TramitesBusOptions>());

        await publisher.EnqueueAsync(Registro(), Ct);
        await db.SaveChangesAsync(Ct);

        await using var verify = NewContext(dbName);
        (await verify.ProcedureStateChangeOutbox.CountAsync(Ct)).Should().Be(0);
        (await verify.Set<OutboxMessage>().CountAsync(Ct)).Should().Be(1);
    }

    [Fact]
    public async Task LasValidacionesDeIdentidad_SalenAlBus_YUnCompletedRepetidoNoSeVuelveAPublicar()
    {
        var dbName = NewDbName();
        await using var provider = Servicios(dbName, habilitado: true);
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FlitDbContext>();
        var publisher = new RabbitMqIdentityValidationEventPublisher(
            db, scope.ServiceProvider.GetRequiredService<IPlatformOutbox>(), NullLogger<RabbitMqIdentityValidationEventPublisher>.Instance);
        var validacion = Guid.NewGuid();

        await publisher.PublishAsync(new IdentityValidationRequested
        {
            TenantId = TenantId, ProcedureInstanceId = InstanceId, ValidationId = validacion, Provider = "mock", Parte = "comprador", ProviderVerificationId = "prov-1",
        }, Ct);
        await db.SaveChangesAsync(Ct);
        var completed = new IdentityValidationCompleted
        {
            TenantId = TenantId, ProcedureInstanceId = InstanceId, ValidationId = validacion, Provider = "mock", Parte = "comprador", Estado = "aprobado", ProviderStatus = "approved", Score = 97,
        };
        await publisher.PublishAsync(completed, Ct);
        await db.SaveChangesAsync(Ct);
        await publisher.PublishAsync(completed, Ct); // se re-terminaliza: el writer no encola otro
        await db.SaveChangesAsync(Ct);

        await using var verify = NewContext(dbName);
        var mensajes = await verify.Set<OutboxMessage>().OrderBy(m => m.OccurredAt).ToListAsync(Ct);
        mensajes.Select(m => m.RoutingKey).Should().Equal("tramites.identity_validation.requested", "tramites.identity_validation.completed");
        var datos = JsonDocument.Parse(mensajes[1].Payload).RootElement.GetProperty("data");
        Propiedades(datos).Should().BeEquivalentTo("procedureInstanceId", "validationId", "provider", "parte", "estado", "providerStatus", "score");
        datos.GetProperty("estado").GetString().Should().Be("aprobado");
        datos.GetProperty("score").GetInt32().Should().Be(97);
        (await verify.Set<IdentityValidationOutbox>().CountAsync(Ct)).Should().Be(2, "el auto-flujo de firma/FUR sigue leyendo la outbox propia");
    }

    [Fact]
    public async Task ConElBusEncendido_SeRegistranLaOutboxYSuPublicador()
    {
        await using var provider = ServiciosDeInfraestructura(new()
        {
            ["Tramites:Bus:Habilitado"] = "true",
            ["Platform:Messaging:Producer"] = "tramites",
            ["Platform:Messaging:ConnectionString"] = "amqp://tramites:clave@rabbitmq:5672/flit",
        });

        provider.GetRequiredService<TramitesBusOptions>().Habilitado.Should().BeTrue();
        await using (var scope = provider.CreateAsyncScope())
            scope.ServiceProvider.GetService<IPlatformOutbox>().Should().NotBeNull();
        provider.GetServices<Microsoft.Extensions.Hosting.IHostedService>()
            .Should().Contain(s => s.GetType().Name.StartsWith("OutboxPublisherService", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("false", "false", "inprocess", "EntregaEnProceso")]
    [InlineData("false", "true", "rabbitmq", "Messaging:IdentityValidation")]
    public void LasCombinacionesQuePerderianEventos_FallanAlArrancar(string habilitado, string entrega, string mensajeria, string menciona)
    {
        var config = Config(new() { ["Tramites:Bus:Habilitado"] = habilitado, ["Tramites:Bus:EntregaEnProceso"] = entrega });

        var registrar = () => new ServiceCollection().AddTramitesBus(config, mensajeria);

        registrar.Should().Throw<InvalidOperationException>().WithMessage($"*{menciona}*");
    }

    [Fact]
    public void ConElBusEncendido_ElProductorTieneQueSerTramites()
    {
        var config = Config(new()
        {
            ["Tramites:Bus:Habilitado"] = "true",
            ["Platform:Messaging:Producer"] = "consultas",
            ["Platform:Messaging:ConnectionString"] = "amqp://tramites:clave@rabbitmq:5672/flit",
        });

        var registrar = () => new ServiceCollection().AddTramitesBus(config, "inprocess");

        registrar.Should().Throw<InvalidOperationException>().WithMessage("*Producer*tramites*");
    }

    // ── Apoyo ────────────────────────────────────────────────────────────────────────────────────────────────────

    private static ServiceProvider Servicios(string dbName, bool habilitado, bool entregaEnProceso = true)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped(_ => NewContext(dbName));
        services.AddTramitesBus(Config(new()
        {
            ["Tramites:Bus:Habilitado"] = habilitado.ToString(),
            ["Tramites:Bus:EntregaEnProceso"] = entregaEnProceso.ToString(),
            ["Platform:Messaging:Producer"] = "tramites",
            ["Platform:Messaging:ConnectionString"] = "amqp://tramites:clave@127.0.0.1:5672/flit",
        }), "inprocess");
        return services.BuildServiceProvider();
    }

    private static ServiceProvider ServiciosDeInfraestructura(Dictionary<string, string?> valores)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped(_ => NewContext(NewDbName()));
        services.AddTramitesBus(Config(valores), "inprocess");
        return services.BuildServiceProvider();
    }

    private static IConfiguration Config(Dictionary<string, string?> valores) =>
        new ConfigurationBuilder().AddInMemoryCollection(valores).Build();

    private static List<string> Propiedades(JsonElement objeto) => objeto.EnumerateObject().Select(p => p.Name).ToList();

    private static TramiteTransitionRecord Registro() => new(
        TenantId, InstanceId, TramiteEstado.Borrador, TramiteEstado.Preparado, "gates ok", Guid.NewGuid(), DateTimeOffset.UtcNow);

    private static string NewDbName() => $"flit-13350-bus-{Guid.NewGuid()}";

    private static FlitDbContext NewContext(string dbName) =>
        new(new DbContextOptionsBuilder<FlitDbContext>().UseInMemoryDatabase(dbName).Options);
}
