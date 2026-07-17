using Flit.Admin.Application.Auditing;
using Flit.Admin.Domain.Companies.TransitOffices;
using Flit.Admin.Tests.TestDoubles;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Flit.Admin.Tests.Companies.OtConsultationRestrictions;

/// <summary>
/// Persistencia real (proveedor InMemory) del repositorio de restricciones de consulta por OT
/// (HU #10759). Complementa los tests de handler (mockeados con NSubstitute) verificando lo
/// que estos no pueden: AC1 (upsert + fila de auditoría), AC2 (idempotencia en ambos sentidos
/// — no-op sin duplicar auditoría) y AC5 (aislamiento entre tenants).
/// </summary>
public sealed class OtConsultationRestrictionRepositoryTests
{
    private static readonly Guid TransitOfficeId = Guid.Parse("aaaaaaaa-0001-4000-8000-000000000001");
    private static readonly Guid ChangedBy = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact] // AC1 — registra la restricción y su auditoría (create).
    public async Task AC1_SetAsync_SobreFilaInexistente_Crea_YAuditaComoCreate()
    {
        var db = NewDbName();
        var tenantId = Guid.NewGuid();

        await using (var act = NewContext(db))
        {
            await new OtConsultationRestrictionRepository(act, NullAuditContextAccessor.Instance)
                .SetAsync(
                    tenantId, TransitOfficeId, RestrictedConsultationKinds.Rnmc, enabled: false,
                    ChangedBy, correlationId: null, TestContext.Current.CancellationToken);
        }

        await using var verify = NewContext(db);

        var rows = await verify.TenantTransitOfficeConsultationRestrictions
            .Where(r => r.TenantId == tenantId)
            .ToListAsync(cancellationToken: TestContext.Current.CancellationToken);
        rows.Should().ContainSingle();
        rows[0].TransitOfficeId.Should().Be(TransitOfficeId);
        rows[0].ConsultationKind.Should().Be(RestrictedConsultationKinds.Rnmc);
        rows[0].Enabled.Should().BeFalse();
        rows[0].CreatedBy.Should().Be(ChangedBy);

        var audits = await verify.TenantConfigAuditLogs
            .Where(a => a.TenantId == tenantId)
            .ToListAsync(cancellationToken: TestContext.Current.CancellationToken);
        audits.Should().ContainSingle();
        audits[0].EntityName.Should().Be("tenant_transit_office_consultation_restrictions");
        audits[0].FieldName.Should().Be(RestrictedConsultationKinds.Rnmc);
        audits[0].OldValue.Should().BeNull();
        audits[0].NewValue.Should().Be("false");
        audits[0].Operation.Should().Be(AuditVocabulary.Operations.Create);
    }

    [Fact] // AC2 — idempotente: reenviar el mismo estado deseado no duplica fila ni auditoría.
    public async Task AC2_SetAsync_ConElMismoEstado_EsNoOp_SinDuplicarAuditoria()
    {
        var db = NewDbName();
        var tenantId = Guid.NewGuid();

        await using (var first = NewContext(db))
        {
            await new OtConsultationRestrictionRepository(first, NullAuditContextAccessor.Instance)
                .SetAsync(
                    tenantId, TransitOfficeId, RestrictedConsultationKinds.Rnmc, enabled: false,
                    ChangedBy, correlationId: null, TestContext.Current.CancellationToken);
        }

        await using (var second = NewContext(db))
        {
            await new OtConsultationRestrictionRepository(second, NullAuditContextAccessor.Instance)
                .SetAsync(
                    tenantId, TransitOfficeId, RestrictedConsultationKinds.Rnmc, enabled: false,
                    ChangedBy, correlationId: null, TestContext.Current.CancellationToken);
        }

        await using var verify = NewContext(db);
        (await verify.TenantTransitOfficeConsultationRestrictions
            .CountAsync(r => r.TenantId == tenantId, cancellationToken: TestContext.Current.CancellationToken))
            .Should().Be(1);
        (await verify.TenantConfigAuditLogs
            .CountAsync(a => a.TenantId == tenantId, cancellationToken: TestContext.Current.CancellationToken))
            .Should().Be(1);
    }

    [Fact] // AC2 — idempotente en el OTRO sentido: el flip de vuelta sí persiste + audita (update).
    public async Task AC2_SetAsync_FlipDeVuelta_Actualiza_YAuditaComoUpdate()
    {
        var db = NewDbName();
        var tenantId = Guid.NewGuid();

        await using (var first = NewContext(db))
        {
            await new OtConsultationRestrictionRepository(first, NullAuditContextAccessor.Instance)
                .SetAsync(
                    tenantId, TransitOfficeId, RestrictedConsultationKinds.Rnmc, enabled: false,
                    ChangedBy, correlationId: null, TestContext.Current.CancellationToken);
        }

        await using (var flipBack = NewContext(db))
        {
            await new OtConsultationRestrictionRepository(flipBack, NullAuditContextAccessor.Instance)
                .SetAsync(
                    tenantId, TransitOfficeId, RestrictedConsultationKinds.Rnmc, enabled: true,
                    ChangedBy, correlationId: null, TestContext.Current.CancellationToken);
        }

        await using var verify = NewContext(db);
        var rows = await verify.TenantTransitOfficeConsultationRestrictions
            .Where(r => r.TenantId == tenantId)
            .ToListAsync(cancellationToken: TestContext.Current.CancellationToken);
        rows.Should().ContainSingle();
        rows[0].Enabled.Should().BeTrue();

        var audits = await verify.TenantConfigAuditLogs
            .Where(a => a.TenantId == tenantId)
            .OrderBy(a => a.ChangedAt)
            .ToListAsync(cancellationToken: TestContext.Current.CancellationToken);
        audits.Should().HaveCount(2);
        audits[1].OldValue.Should().Be("false");
        audits[1].NewValue.Should().Be("true");
        audits[1].Operation.Should().Be(AuditVocabulary.Operations.Update);
    }

    [Fact] // AC5 — aislamiento: ListAsync de un tenant no ve filas de otro.
    public async Task AC5_ListAsync_SoloDevuelveFilasDelTenant()
    {
        var db = NewDbName();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        await using (var seed = NewContext(db))
        {
            var repo = new OtConsultationRestrictionRepository(seed, NullAuditContextAccessor.Instance);
            await repo.SetAsync(
                tenantA, TransitOfficeId, RestrictedConsultationKinds.Rnmc, enabled: false,
                ChangedBy, correlationId: null, TestContext.Current.CancellationToken);
            await repo.SetAsync(
                tenantB, TransitOfficeId, RestrictedConsultationKinds.Fines, enabled: false,
                ChangedBy, correlationId: null, TestContext.Current.CancellationToken);
        }

        await using var verify = NewContext(db);
        var repository = new OtConsultationRestrictionRepository(verify, NullAuditContextAccessor.Instance);

        var itemsA = await repository.ListAsync(tenantA, TestContext.Current.CancellationToken);
        itemsA.Should().ContainSingle().Which.ConsultationKind.Should().Be(RestrictedConsultationKinds.Rnmc);
    }

    [Fact] // AC6 — tabla dispersa: sin filas, ListDisabledKindsAsync devuelve vacío (permisivo).
    public async Task AC6_ListDisabledKindsAsync_SinFilas_DevuelveVacio()
    {
        await using var ctx = NewContext(NewDbName());
        var repository = new OtConsultationRestrictionRepository(ctx, NullAuditContextAccessor.Instance);

        var disabled = await repository.ListDisabledKindsAsync(
            Guid.NewGuid(), TransitOfficeId, TestContext.Current.CancellationToken);

        disabled.Should().BeEmpty();
    }

    private static string NewDbName() => $"flit-ot-consultation-restrictions-{Guid.NewGuid()}";

    private static FlitDbContext NewContext(string dbName) =>
        new(new DbContextOptionsBuilder<FlitDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options);
}
