using System.Net;
using System.Security.Cryptography;
using System.Text;
using Flit.Admin.Application.OtWebhooks;
using Flit.Admin.Application.OtWebhooks.CreateOtWebhook;
using Flit.Admin.Application.OtWebhooks.ListOtApiLogs;
using Flit.Admin.Application.OtWebhooks.ListOtWebhooks;
using Flit.Admin.Application.OtWebhooks.UpdateOtWebhook;
using Flit.Admin.Domain.OtWebhooks;
using Flit.Infrastructure.OtWebhooks;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Admin;
using Flit.Infrastructure.Persistence.Repositories;
using Flit.Modules.Notificaciones.Webhooks;
using Flit.Platform.Sdk.Messaging;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Flit.Admin.Tests.OtWebhooks;

/// <summary>Tests webhooks OT y bitácora API (HU #10216) — AC1–AC5.</summary>
public sealed class OtWebhookHandlerTests
{
    private static readonly Guid TenantA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid TenantB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid ChangedBy = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public async Task AC1_CreateWebhook_PersistsSecretHashAndIsActive()
    {
        var db = NewDbName();
        await using var ctx = NewContext(db);

        var handler = new CreateOtWebhookHandler(
            new OtWebhookSubscriptionRepository(ctx),
            new OtWebhookSecretHasherService());

        var result = await handler.HandleAsync(new CreateOtWebhookCommand
        {
            TenantId = TenantA,
            CreatedBy = ChangedBy,
            Request = new CreateOtWebhookRequest
            {
                EventType = OtWebhookEventTypes.VehicleStateChanged,
                TargetUrl = "https://hooks.example.com/ot",
                Secret = "super-secret",
            },
        }, TestContext.Current.CancellationToken);

        result.Status.Should().Be(CreateOtWebhookStatus.Created);
        result.Webhook!.IsActive.Should().BeTrue();

        await using var verify = NewContext(db);
        var entity = await verify.OtWebhookSubscriptions.SingleAsync(cancellationToken: TestContext.Current.CancellationToken);
        entity.SecretHash.Should().Be(OtWebhookSecretHasher.HashSecret("super-secret"));
        entity.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task AC3_UpdateWebhook_ChangesTargetUrlWithoutRestart()
    {
        var db = NewDbName();
        var subscriptionId = Guid.NewGuid();

        await using (var seed = NewContext(db))
        {
            seed.OtWebhookSubscriptions.Add(new OtWebhookSubscriptionEntity
            {
                Id = subscriptionId,
                TenantId = TenantA,
                EventType = OtWebhookEventTypes.VehicleStateChanged,
                TargetUrl = "https://hooks.example.com/old",
                SecretHash = OtWebhookSecretHasher.HashSecret("secret"),
                IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow,
            });
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var ctx = NewContext(db);
        var handler = new UpdateOtWebhookHandler(new OtWebhookSubscriptionRepository(ctx));
        var result = await handler.HandleAsync(new UpdateOtWebhookCommand
        {
            TenantId = TenantA,
            SubscriptionId = subscriptionId,
            ChangedBy = ChangedBy,
            Request = new UpdateOtWebhookRequest
            {
                TargetUrl = "https://hooks.example.com/new",
            },
        }, TestContext.Current.CancellationToken);

        result.Status.Should().Be(UpdateOtWebhookStatus.Updated);
        result.Webhook!.TargetUrl.Should().Be("https://hooks.example.com/new");
    }

    [Fact]
    public async Task AC4_ListApiLogs_ReturnsPaginatedFieldsWithoutRawPayload()
    {
        var db = NewDbName();
        var calledAt = new DateTimeOffset(2026, 6, 15, 10, 0, 0, TimeSpan.Zero);
        var correlationId = Guid.NewGuid();

        await using (var seed = NewContext(db))
        {
            seed.OtApiCallLogs.Add(new OtApiCallLogEntity
            {
                Id = Guid.NewGuid(),
                TenantId = TenantA,
                Direction = "outbound",
                Endpoint = "https://hooks.example.com/ot",
                HttpMethod = "POST",
                PayloadHash = "abc123payloadhash",
                ResponseCode = 200,
                DurationMs = 42,
                CalledAt = calledAt,
                CorrelationId = correlationId,
            });
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var ctx = NewContext(db);
        var handler = new ListOtApiLogsHandler(new OtApiCallLogRepository(ctx));
        var result = await handler.HandleAsync(new ListOtApiLogsQuery
        {
            TenantId = TenantA,
            Direction = "outbound",
            From = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero),
            To = new DateTimeOffset(2026, 6, 30, 0, 0, 0, TimeSpan.Zero),
            Page = 1,
            PageSize = 50,
        }, TestContext.Current.CancellationToken);

        result.TotalCount.Should().Be(1);
        var log = result.Data.Should().ContainSingle().Subject;
        log.Endpoint.Should().Be("https://hooks.example.com/ot");
        log.HttpMethod.Should().Be("POST");
        log.ResponseCode.Should().Be(200);
        log.DurationMs.Should().Be(42);
        log.CalledAt.Should().Be(calledAt);
        log.CorrelationId.Should().Be(correlationId);
        log.PayloadHash.Should().Be("abc123payloadhash");
    }

    [Fact]
    public async Task AC5_ListApiLogs_IsolatesTenantData()
    {
        var db = NewDbName();

        await using (var seed = NewContext(db))
        {
            seed.OtApiCallLogs.Add(new OtApiCallLogEntity
            {
                Id = Guid.NewGuid(),
                TenantId = TenantB,
                Direction = "outbound",
                Endpoint = "https://hooks.example.com/other",
                HttpMethod = "POST",
                PayloadHash = "hash-b",
                CalledAt = DateTimeOffset.UtcNow,
            });
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var ctx = NewContext(db);
        var handler = new ListOtApiLogsHandler(new OtApiCallLogRepository(ctx));
        var result = await handler.HandleAsync(new ListOtApiLogsQuery { TenantId = TenantA }, TestContext.Current.CancellationToken);

        result.Data.Should().BeEmpty();
        result.TotalCount.Should().Be(0);
    }

    [Fact]
    public async Task ListWebhooks_ReturnsAllTenantSubscriptions()
    {
        var db = NewDbName();
        var subscriptionId = Guid.NewGuid();

        await using (var seed = NewContext(db))
        {
            seed.OtWebhookSubscriptions.Add(new OtWebhookSubscriptionEntity
            {
                Id = subscriptionId,
                TenantId = TenantA,
                EventType = OtWebhookEventTypes.ProcedureStateChanged,
                TargetUrl = "https://hooks.example.com/proc",
                SecretHash = OtWebhookSecretHasher.HashSecret("s"),
                IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow,
            });
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var ctx = NewContext(db);
        var handler = new ListOtWebhooksHandler(new OtWebhookSubscriptionRepository(ctx));
        var result = await handler.HandleAsync(new ListOtWebhooksQuery { TenantId = TenantA }, TestContext.Current.CancellationToken);

        result.Data.Should().ContainSingle(w => w.Id == subscriptionId);
    }

    [Fact]
    public async Task AC2_HU13359_ElWebhookQuedaComoTrabajoFirmado_YLaBitacoraOutboundLoRegistra()
    {
        var db = NewDbName();
        const string secret = "dispatch-secret";
        const string targetUrl = "https://hooks.example.com/ot";
        await using (var seed = NewContext(db))
        {
            seed.OtWebhookSubscriptions.Add(new OtWebhookSubscriptionEntity
            {
                Id = Guid.NewGuid(),
                TenantId = TenantA,
                EventType = OtWebhookEventTypes.VehicleStateChanged,
                TargetUrl = targetUrl,
                SecretHash = OtWebhookSecretHasher.HashSecret(secret),
                IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow,
            });
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped(_ => NewContext(db));
        services.AddFlitOutbox<FlitDbContext>(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Platform:Messaging:Producer"] = "tramites",
                ["Platform:Messaging:ConnectionString"] = "amqp://tramites:x@127.0.0.1:5672/flit",
            }).Build());
        await using var provider = services.BuildServiceProvider();

        await using var ctx = NewContext(db);
        var dispatch = new OtWebhookDispatchService(
            new OtWebhookSubscriptionRepository(ctx),
            new OtApiCallLogRepository(ctx),
            provider.GetRequiredService<IServiceScopeFactory>());

        await dispatch.DispatchAsync(TenantA, OtWebhookEventTypes.VehicleStateChanged, new { to_status = "submitted" }, TestContext.Current.CancellationToken);

        await using var verify = NewContext(db);
        var mensaje = await verify.Set<OutboxMessage>().SingleAsync(TestContext.Current.CancellationToken);
        mensaje.Exchange.Should().Be("flit.notificaciones");
        var trabajo = EventEnvelope.FromJson(Encoding.UTF8.GetBytes(mensaje.Payload)).DataAs<TrabajoWebhook>();
        trabajo.Url.Should().Be(targetUrl);
        trabajo.Origen.Should().Be("ot");
        var firmaEsperada = ComputeHmacSha256Hex(trabajo.Cuerpo, OtWebhookSecretHasher.SigningKeyFromStoredHash(OtWebhookSecretHasher.HashSecret(secret)));
        trabajo.Cabeceras["X-Webhook-Signature"].Should().Be($"sha256={firmaEsperada}", "viaja la firma del cuerpo, no la llave");
        trabajo.Cabeceras.Should().ContainKey("X-Correlation-Id");
        // HU #13359: core-api no hace el POST (lo hace Notificaciones); la bitácora outbound registra el envío encolado.
        var log = await verify.OtApiCallLogs.SingleAsync(TestContext.Current.CancellationToken);
        log.Direction.Should().Be("outbound");
        log.Endpoint.Should().Be(targetUrl);
        log.HttpMethod.Should().Be("POST");
        log.ResponseCode.Should().BeNull("aún no hay respuesta del destino");
        log.CorrelationId.Should().Be(Guid.Parse(trabajo.Cabeceras["X-Correlation-Id"]));
        log.PayloadHash.Should().NotBeNullOrWhiteSpace();
    }

    private static string NewDbName() => Guid.NewGuid().ToString();

    private static FlitDbContext NewContext(string dbName) =>
        new(new DbContextOptionsBuilder<FlitDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options);

    private static string ComputeHmacSha256Hex(string payload, byte[] key)
    {
        var hash = HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
