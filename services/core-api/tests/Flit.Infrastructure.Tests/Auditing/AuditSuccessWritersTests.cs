using Flit.Admin.Application.Auditing;
using Flit.Admin.Domain.Companies.Settings;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Flit.Infrastructure.Tests.Auditing;

/// <summary>
/// RNF01 (ADR-0024): las filas de auditoría de operaciones EXITOSAS deben traer la IP de
/// origen, la operación explícita y <c>result = success</c>. Cubre <c>TenantSettingsRepository</c>
/// y <c>TransitGrantRepository</c> (los otros dos writers comparten exactamente el mismo patrón).
/// </summary>
public sealed class AuditSuccessWritersTests
{
    private const string ClientIp = "203.0.113.7";
    private static readonly Guid TenantId = Guid.NewGuid();

    private static FlitDbContext NewContext(string dbName) =>
        new(new DbContextOptionsBuilder<FlitDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options);

    /// <summary>Accessor fijo que simula la IP resuelta desde la petición HTTP.</summary>
    private sealed class FixedAuditContextAccessor : IAuditContextAccessor
    {
        public string? ClientIp { get; init; }

        public Guid? UserId { get; init; }
    }

    [Fact]
    public async Task TenantSettings_Success_WritesIpOperationAndResult()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = NewContext(nameof(TenantSettings_Success_WritesIpOperationAndResult));
        var repo = new TenantSettingsRepository(db, new FixedAuditContextAccessor { ClientIp = ClientIp });

        await repo.SaveAsync(
            TenantSettings.Default(TenantId),
            [new TenantConfigChange("allow_initial_registration", "false", "true")],
            changedBy: Guid.NewGuid(),
            correlationId: null,
            ct);

        var audit = await db.TenantConfigAuditLogs.SingleAsync(ct);
        audit.ClientIp.Should().Be(ClientIp);
        audit.Operation.Should().Be(AuditVocabulary.Operations.Update);
        audit.Result.Should().Be(AuditVocabulary.Results.Success);
    }

    [Fact]
    public async Task TransitGrant_Add_WritesCreateSuccessWithIp()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = NewContext(nameof(TransitGrant_Add_WritesCreateSuccessWithIp));
        var repo = new TransitGrantRepository(db, new FixedAuditContextAccessor { ClientIp = ClientIp });

        var added = await repo.AddGrantAsync(TenantId, Guid.NewGuid(), Guid.NewGuid(), correlationId: null, ct);

        added.Should().BeTrue();
        var audit = await db.TenantConfigAuditLogs.SingleAsync(ct);
        audit.ClientIp.Should().Be(ClientIp);
        audit.Operation.Should().Be(AuditVocabulary.Operations.Create);
        audit.Result.Should().Be(AuditVocabulary.Results.Success);
    }

    [Fact]
    public async Task NullAccessor_Success_LeavesIpNullButKeepsOperationAndResult()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = NewContext(nameof(NullAccessor_Success_LeavesIpNullButKeepsOperationAndResult));
        var repo = new TransitGrantRepository(db, NullAuditContextAccessor.Instance);

        await repo.AddGrantAsync(TenantId, Guid.NewGuid(), Guid.NewGuid(), correlationId: null, ct);

        var audit = await db.TenantConfigAuditLogs.SingleAsync(ct);
        audit.ClientIp.Should().BeNull();
        audit.Operation.Should().Be(AuditVocabulary.Operations.Create);
        audit.Result.Should().Be(AuditVocabulary.Results.Success);
    }
}
