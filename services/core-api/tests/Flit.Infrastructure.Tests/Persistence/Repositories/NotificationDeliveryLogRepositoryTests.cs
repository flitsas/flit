using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Admin;
using Flit.Infrastructure.Persistence.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Flit.Infrastructure.Tests.Persistence.Repositories;

/// <summary>
/// HU #11363, AC3 — aislamiento por tenant de la bitácora consultable. El RLS de
/// <c>admin.notification_delivery_logs</c> es decorativo (sin <c>FORCE ROW LEVEL SECURITY</c>, la
/// app es owner): lo que protege AC3 es el <c>WHERE tenant_id</c> explícito de
/// <see cref="NotificationDeliveryLogRepository"/>. Estas pruebas siembran DOS tenants en la MISMA
/// tabla InMemory y comprueban que consultar uno nunca trae filas del otro — si alguien quita el
/// filtro, <see cref="AC3_ConsultaDeUnTenant_NuncaTraeFilasDeOtroTenant"/> cae.
/// </summary>
/// <remarks>
/// Uso de ejemplo:
/// <code>
/// var repo = new NotificationDeliveryLogRepository(context);
/// var propias = await repo.ListByTenantAsync(tenantId, skip: 0, take: 50, ct);
/// </code>
/// </remarks>
public sealed class NotificationDeliveryLogRepositoryTests
{
    private static readonly Guid TenantA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid TenantB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    [Fact]
    public async Task AC3_ConsultaDeUnTenant_NuncaTraeFilasDeOtroTenant()
    {
        var dbName = Guid.NewGuid().ToString();

        var filaDeA = Guid.NewGuid();
        var filaDeB = Guid.NewGuid();

        await using (var seed = NewContext(dbName))
        {
            seed.NotificationDeliveryLogs.Add(Row(filaDeA, TenantA, "a-solo-de-tenant-a@flit.test"));
            seed.NotificationDeliveryLogs.Add(Row(filaDeB, TenantB, "b-solo-de-tenant-b@flit.test"));
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var ctx = NewContext(dbName);
        var repo = new NotificationDeliveryLogRepository(ctx);

        var propiasDeA = await repo.ListByTenantAsync(
            TenantA, skip: 0, take: 50, TestContext.Current.CancellationToken);

        propiasDeA.Should().ContainSingle(l => l.Id == filaDeA);
        propiasDeA.Should().NotContain(l => l.Id == filaDeB, "la fila de otro tenant NUNCA debe aparecer");
        propiasDeA.Should().OnlyContain(l => l.Recipient == "a-solo-de-tenant-a@flit.test");
    }

    [Fact]
    public async Task AC3_TenantSinFilas_DevuelveListaVaciaNoLasDeOtroTenant()
    {
        var dbName = Guid.NewGuid().ToString();
        var tenantSinActividad = Guid.NewGuid();

        await using (var seed = NewContext(dbName))
        {
            seed.NotificationDeliveryLogs.Add(Row(Guid.NewGuid(), TenantB, "otro@flit.test"));
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var ctx = NewContext(dbName);
        var repo = new NotificationDeliveryLogRepository(ctx);

        var resultado = await repo.ListByTenantAsync(
            tenantSinActividad, skip: 0, take: 50, TestContext.Current.CancellationToken);

        resultado.Should().BeEmpty();
    }

    private static NotificationDeliveryLogEntity Row(Guid id, Guid tenantId, string recipient) => new()
    {
        Id = id,
        TenantId = tenantId,
        TemplateKey = "security.forgot-password",
        Channel = "flit_smtp",
        Recipient = recipient,
        Result = "enviado",
        FailureReason = null,
        DurationMs = 42,
        OccurredAt = DateTimeOffset.UtcNow,
        CreatedAt = DateTimeOffset.UtcNow,
    };

    private static FlitDbContext NewContext(string dbName) =>
        new(new DbContextOptionsBuilder<FlitDbContext>().UseInMemoryDatabase(dbName).Options);
}
