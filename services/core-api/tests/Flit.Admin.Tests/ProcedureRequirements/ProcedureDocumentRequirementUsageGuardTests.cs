using Flit.Admin.Domain.ProcedureSnapshots;
using Flit.Infrastructure.Persistence.Entities.Tramites;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Services;
using Flit.Tramites.Domain.Entities;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Flit.Admin.Tests.ProcedureRequirements;

/// <summary>
/// Guard real de uso de una asociación trámite ↔ documento (HU #10197, cierre de la deuda
/// de HU #10195 AC4). Reemplaza al stub: una asociación está "en uso" si un trámite activo
/// del mismo tipo de trámite la congeló en su snapshot documental.
/// </summary>
public sealed class ProcedureDocumentRequirementUsageGuardTests
{
    private static readonly Guid ProcedureTypeId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid TenantId = Guid.Parse("77777777-7777-7777-7777-777777777777");
    private static readonly Guid DocA = Guid.Parse("aaaa1111-1111-1111-1111-111111111111");
    private static readonly Guid DocB = Guid.Parse("bbbb2222-2222-2222-2222-222222222222");

    [Fact]
    public async Task IsInUse_WhenActiveProcedureSnapshotContainsDocument_ReturnsTrue()
    {
        var db = NewDbName();
        var requirementId = await SeedRequirementAsync(db, DocA);
        await SeedSnapshotAsync(db, "borrador", DocA);

        await using var ctx = NewContext(db);
        var inUse = await new ProcedureDocumentRequirementUsageGuard(ctx).IsInUseAsync(requirementId, TestContext.Current.CancellationToken);

        inUse.Should().BeTrue();
    }

    [Fact]
    public async Task IsInUse_WhenSnapshotDoesNotContainDocument_ReturnsFalse()
    {
        var db = NewDbName();
        var requirementId = await SeedRequirementAsync(db, DocA);
        await SeedSnapshotAsync(db, "borrador", DocB); // snapshot congeló otro documento

        await using var ctx = NewContext(db);
        var inUse = await new ProcedureDocumentRequirementUsageGuard(ctx).IsInUseAsync(requirementId, TestContext.Current.CancellationToken);

        inUse.Should().BeFalse();
    }

    [Theory]
    [InlineData("aprobado")]
    [InlineData("anulado")]
    public async Task IsInUse_WhenOnlyInactiveProceduresUseDocument_ReturnsFalse(string status)
    {
        var db = NewDbName();
        var requirementId = await SeedRequirementAsync(db, DocA);
        await SeedSnapshotAsync(db, status, DocA);

        await using var ctx = NewContext(db);
        var inUse = await new ProcedureDocumentRequirementUsageGuard(ctx).IsInUseAsync(requirementId, TestContext.Current.CancellationToken);

        inUse.Should().BeFalse();
    }

    [Fact]
    public async Task IsInUse_WhenRequirementDoesNotExist_ReturnsFalse()
    {
        await using var ctx = NewContext(NewDbName());

        var inUse = await new ProcedureDocumentRequirementUsageGuard(ctx).IsInUseAsync(Guid.NewGuid(), TestContext.Current.CancellationToken);

        inUse.Should().BeFalse();
    }

    // ---------- Helpers ----------

    private static string NewDbName() => $"flit-usage-guard-{Guid.NewGuid()}";

    private static FlitDbContext NewContext(string dbName) =>
        new(new DbContextOptionsBuilder<FlitDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options);

    private static async Task<Guid> SeedRequirementAsync(string dbName, Guid documentTypeId)
    {
        var requirementId = Guid.NewGuid();
        await using var ctx = NewContext(dbName);
        ctx.ProcedureTypes.Add(new ProcedureType { Id = ProcedureTypeId, Code = "TRASPASO", Name = "Traspaso", IsActive = true });
        ctx.DocumentTypes.Add(new DocumentType { Id = documentTypeId, Code = "DOC", Name = "Documento", IsActive = true, CreatedAt = DateTimeOffset.UtcNow });
        ctx.ProcedureDocumentRequirements.Add(new ProcedureDocumentRequirement
        {
            Id = requirementId,
            ProcedureTypeId = ProcedureTypeId,
            DocumentTypeId = documentTypeId,
            IsMandatory = true,
            DefaultSortOrder = 10,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await ctx.SaveChangesAsync();
        return requirementId;
    }

    private static async Task SeedSnapshotAsync(string dbName, string status, Guid snapshotDocumentTypeId)
    {
        var instanceId = Guid.NewGuid();
        var payload = new ProcedureDocumentSnapshotPayload(
            ProcedureTypeId,
            null,
            TenantId,
            [new ProcedureDocumentSnapshotItem(snapshotDocumentTypeId, "DOC", "Documento", true, 10, "DEFAULT")]);

        await using var ctx = NewContext(dbName);
        ctx.ProcedureInstances.Add(new ProcedureInstance
        {
            ProcedureType = ProcedureTypeFixture.Matricula,
            Id = instanceId,
            TenantId = TenantId,
            ProcedureTypeId = ProcedureTypeId,
            ReferenceNumber = $"TR-{status}",
            Status = status,
            CreatedByUserId = Guid.NewGuid(),
            CreatedAt = DateTimeOffset.UtcNow,
        });
        ctx.ProcedureDocumentSnapshots.Add(new ProcedureDocumentSnapshot
        {
            Id = Guid.NewGuid(),
            TenantId = TenantId,
            ProcedureInstanceId = instanceId,
            Snapshot = SnapshotJson.Serialize(payload),
            SnapshotAt = DateTimeOffset.UtcNow,
        });
        await ctx.SaveChangesAsync();
    }
}
