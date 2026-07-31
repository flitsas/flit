using Flit.Admin.Application.Auditing;
using Flit.Admin.Domain.Companies.TransitOffices;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Flit.Admin.Tests.Companies.OtBlockingPolicies;

/// <summary>
/// Persistencia real (proveedor InMemory) del repositorio de políticas de bloqueo por OT
/// (FEATURE 05). Verifica el upsert + fila de auditoría, la idempotencia en ambos sentidos
/// (no-op sin duplicar auditoría comparando <c>blocks</c>) y el aislamiento entre tenants.
/// </summary>
public sealed class OtBlockingPolicyRepositoryTests
{
    private static readonly Guid TransitOfficeId = Guid.Parse("aaaaaaaa-0001-4000-8000-000000000001");
    private static readonly Guid ChangedBy = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact] // Registra la política y su auditoría (create).
    public async Task SetAsync_SobreFilaInexistente_Crea_YAuditaComoCreate()
    {
        var db = NewDbName();
        var tenantId = Guid.NewGuid();

        await using (var act = NewContext(db))
        {
            await new OtBlockingPolicyRepository(act, NullAuditContextAccessor.Instance)
                .SetAsync(
                    tenantId, TransitOfficeId, BlockingCriteria.Soat, blocks: false,
                    ChangedBy, correlationId: null, TestContext.Current.CancellationToken);
        }

        await using var verify = NewContext(db);

        var rows = await verify.TenantTransitOfficeBlockingPolicies
            .Where(r => r.TenantId == tenantId)
            .ToListAsync(cancellationToken: TestContext.Current.CancellationToken);
        rows.Should().ContainSingle();
        rows[0].TransitOfficeId.Should().Be(TransitOfficeId);
        rows[0].Criterion.Should().Be(BlockingCriteria.Soat);
        rows[0].Blocks.Should().BeFalse();
        rows[0].CreatedBy.Should().Be(ChangedBy);

        var audits = await verify.TenantConfigAuditLogs
            .Where(a => a.TenantId == tenantId)
            .ToListAsync(cancellationToken: TestContext.Current.CancellationToken);
        audits.Should().ContainSingle();
        audits[0].EntityName.Should().Be("tenant_transit_office_blocking_policies");
        audits[0].FieldName.Should().Be(BlockingCriteria.Soat);
        audits[0].OldValue.Should().BeNull();
        audits[0].NewValue.Should().Be("false");
        audits[0].Operation.Should().Be(AuditVocabulary.Operations.Create);
    }

    [Fact] // Idempotente: reenviar el mismo estado deseado no duplica fila ni auditoría.
    public async Task SetAsync_ConElMismoEstado_EsNoOp_SinDuplicarAuditoria()
    {
        var db = NewDbName();
        var tenantId = Guid.NewGuid();

        await using (var first = NewContext(db))
        {
            await new OtBlockingPolicyRepository(first, NullAuditContextAccessor.Instance)
                .SetAsync(
                    tenantId, TransitOfficeId, BlockingCriteria.Fines, blocks: true,
                    ChangedBy, correlationId: null, TestContext.Current.CancellationToken);
        }

        await using (var second = NewContext(db))
        {
            await new OtBlockingPolicyRepository(second, NullAuditContextAccessor.Instance)
                .SetAsync(
                    tenantId, TransitOfficeId, BlockingCriteria.Fines, blocks: true,
                    ChangedBy, correlationId: null, TestContext.Current.CancellationToken);
        }

        await using var verify = NewContext(db);
        (await verify.TenantTransitOfficeBlockingPolicies
            .CountAsync(r => r.TenantId == tenantId, cancellationToken: TestContext.Current.CancellationToken))
            .Should().Be(1);
        (await verify.TenantConfigAuditLogs
            .CountAsync(a => a.TenantId == tenantId, cancellationToken: TestContext.Current.CancellationToken))
            .Should().Be(1);
    }

    [Fact] // Flip de vuelta sí persiste + audita (update), comparando blocks.
    public async Task SetAsync_FlipDeVuelta_Actualiza_YAuditaComoUpdate()
    {
        var db = NewDbName();
        var tenantId = Guid.NewGuid();

        await using (var first = NewContext(db))
        {
            await new OtBlockingPolicyRepository(first, NullAuditContextAccessor.Instance)
                .SetAsync(
                    tenantId, TransitOfficeId, BlockingCriteria.Soat, blocks: false,
                    ChangedBy, correlationId: null, TestContext.Current.CancellationToken);
        }

        await using (var flipBack = NewContext(db))
        {
            await new OtBlockingPolicyRepository(flipBack, NullAuditContextAccessor.Instance)
                .SetAsync(
                    tenantId, TransitOfficeId, BlockingCriteria.Soat, blocks: true,
                    ChangedBy, correlationId: null, TestContext.Current.CancellationToken);
        }

        await using var verify = NewContext(db);
        var rows = await verify.TenantTransitOfficeBlockingPolicies
            .Where(r => r.TenantId == tenantId)
            .ToListAsync(cancellationToken: TestContext.Current.CancellationToken);
        rows.Should().ContainSingle();
        rows[0].Blocks.Should().BeTrue();

        var audits = await verify.TenantConfigAuditLogs
            .Where(a => a.TenantId == tenantId)
            .OrderBy(a => a.ChangedAt)
            .ToListAsync(cancellationToken: TestContext.Current.CancellationToken);
        audits.Should().HaveCount(2);
        audits[1].OldValue.Should().Be("false");
        audits[1].NewValue.Should().Be("true");
        audits[1].Operation.Should().Be(AuditVocabulary.Operations.Update);
    }

    [Fact] // Aislamiento: ListAsync de un tenant no ve filas de otro.
    public async Task ListAsync_SoloDevuelveFilasDelTenant()
    {
        var db = NewDbName();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        await using (var seed = NewContext(db))
        {
            var repo = new OtBlockingPolicyRepository(seed, NullAuditContextAccessor.Instance);
            await repo.SetAsync(
                tenantA, TransitOfficeId, BlockingCriteria.Soat, blocks: false,
                ChangedBy, correlationId: null, TestContext.Current.CancellationToken);
            await repo.SetAsync(
                tenantB, TransitOfficeId, BlockingCriteria.Fines, blocks: true,
                ChangedBy, correlationId: null, TestContext.Current.CancellationToken);
        }

        await using var verify = NewContext(db);
        var repository = new OtBlockingPolicyRepository(verify, NullAuditContextAccessor.Instance);

        var itemsA = await repository.ListAsync(tenantA, TestContext.Current.CancellationToken);
        itemsA.Should().ContainSingle().Which.Criterion.Should().Be(BlockingCriteria.Soat);
    }

    [Fact] // Tabla dispersa: sin filas, ListForOfficeAsync devuelve vacío.
    public async Task ListForOfficeAsync_SinFilas_DevuelveVacio()
    {
        await using var ctx = NewContext(NewDbName());
        var repository = new OtBlockingPolicyRepository(ctx, NullAuditContextAccessor.Instance);

        var rows = await repository.ListForOfficeAsync(
            Guid.NewGuid(), TransitOfficeId, TestContext.Current.CancellationToken);

        rows.Should().BeEmpty();
    }

    private static string NewDbName() => $"flit-ot-blocking-policies-{Guid.NewGuid()}";

    private static FlitDbContext NewContext(string dbName) =>
        new(new DbContextOptionsBuilder<FlitDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options);
}
