using Flit.Infrastructure.Notifications.Tramites;
using Flit.Infrastructure.Persistence;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Tramites.Estados;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Flit.Infrastructure.Tests.Notifications.Tramites;

public sealed class TramiteRechazoEmailDataLoaderTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task CausalSinFilaDeCatalogo_UsaMarcadorRetirada()
    {
        var tenantId = Guid.NewGuid();
        var instanceId = Guid.NewGuid();
        var historyId = Guid.NewGuid();
        await using var db = new FlitDbContext(
            new DbContextOptionsBuilder<FlitDbContext>()
                .UseInMemoryDatabase($"rechazo-loader-{Guid.NewGuid()}")
                .Options);

        db.ProcedureInstanceStatusHistories.Add(new ProcedureInstanceStatusHistory
        {
            Id = historyId,
            TenantId = tenantId,
            ProcedureInstanceId = instanceId,
            FromStatus = TramiteEstado.Entregado,
            ToStatus = TramiteEstado.Rechazado,
            ChangedAt = DateTimeOffset.UtcNow,
            Reason = "Observación vigente",
            Metadata = "{}",
        });
        db.ProcedureInstanceRejectionReasons.Add(new ProcedureInstanceRejectionReason
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ProcedureInstanceId = instanceId,
            StatusHistoryId = historyId,
            RejectionReasonId = Guid.NewGuid(),
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync(Ct);

        var (causales, observacion) = await TramiteRechazoEmailDataLoader.LoadAsync(
            db, tenantId, instanceId, Ct);

        causales.Should().Equal(TramiteRechazoEmailDataLoader.CausalRetirada);
        observacion.Should().Be("Observación vigente");
    }
}
