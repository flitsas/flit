using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Identity;
using Flit.Infrastructure.Persistence.Repositories;
using Flit.Tramites.Domain.Entities;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Flit.Infrastructure.Tests.Persistence;

/// <summary>
/// Consulta real del historial de transiciones (HU-2 N03, RF05) sobre
/// <see cref="ProcedureInstanceRepository.GetStatusHistoryPageAsync"/> con EF InMemory:
/// orden desc, paginación, aislamiento por tenant y resolución de changed_by_name.
/// </summary>
public sealed class ProcedureInstanceStatusHistoryQueryTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid OtherTenantId = Guid.NewGuid();
    private static readonly Guid InstanceId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly DateTimeOffset Base = new(2026, 7, 1, 12, 0, 0, TimeSpan.Zero);

    private static FlitDbContext NewContext(string dbName) =>
        new(new DbContextOptionsBuilder<FlitDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options);

    private static async Task SeedAsync(FlitDbContext db, int historyCount = 3)
    {
        db.Users.Add(new User { Id = UserId, Email = $"gestor-{UserId:N}@flit.io", DisplayName = "Ana Gestora" });
        db.ProcedureInstances.Add(new ProcedureInstance
        {
            ProcedureType = ProcedureTypeFixture.Matricula,
            Id = InstanceId,
            TenantId = TenantId,
            ProcedureTypeId = Guid.NewGuid(),
            ReferenceNumber = "TRM-2026-000001",
            CreatedByUserId = UserId,
            CreatedAt = Base,
        });

        for (var i = 0; i < historyCount; i++)
        {
            db.ProcedureInstanceStatusHistories.Add(new ProcedureInstanceStatusHistory
            {
                Id = Guid.NewGuid(),
                TenantId = TenantId,
                ProcedureInstanceId = InstanceId,
                FromStatus = i == 0 ? null : $"estado_{i - 1}",
                ToStatus = $"estado_{i}",
                ChangedAt = Base.AddMinutes(i),
                ChangedBy = i % 2 == 0 ? UserId : null,
                Reason = i % 2 == 0 ? null : $"motivo {i}",
            });
        }

        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task ReturnsRowsOrderedByChangedAtDesc()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = NewContext(nameof(ReturnsRowsOrderedByChangedAtDesc));
        await SeedAsync(db);
        var repo = new ProcedureInstanceRepository(db);

        var page = await repo.GetStatusHistoryPageAsync(InstanceId, TenantId, 0, 20, ct);

        page.Should().NotBeNull();
        var (items, total) = page!.Value;
        total.Should().Be(3);
        items.Select(i => i.ToStatus).Should().ContainInOrder("estado_2", "estado_1", "estado_0");
        items.Select(i => i.ChangedAt).Should().BeInDescendingOrder();
    }

    [Fact]
    public async Task PaginatesWithExactTotal()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = NewContext(nameof(PaginatesWithExactTotal));
        await SeedAsync(db, historyCount: 5);
        var repo = new ProcedureInstanceRepository(db);

        var page2 = await repo.GetStatusHistoryPageAsync(InstanceId, TenantId, 2, 2, ct);

        var (items, total) = page2!.Value;
        total.Should().Be(5);
        items.Should().HaveCount(2);
        items.Select(i => i.ToStatus).Should().ContainInOrder("estado_2", "estado_1");
    }

    [Fact]
    public async Task ResolvesChangedByNameFromIdentityUsers()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = NewContext(nameof(ResolvesChangedByNameFromIdentityUsers));
        await SeedAsync(db, historyCount: 2);
        var repo = new ProcedureInstanceRepository(db);

        var page = await repo.GetStatusHistoryPageAsync(InstanceId, TenantId, 0, 20, ct);

        var (items, _) = page!.Value;
        var conUsuario = items.Single(i => i.ChangedByUserId == UserId);
        var sinUsuario = items.Single(i => i.ChangedByUserId == null);
        conUsuario.ChangedByName.Should().Be("Ana Gestora");
        sinUsuario.ChangedByName.Should().BeNull();
        sinUsuario.Reason.Should().Be("motivo 1");
    }

    [Fact]
    public async Task OtherTenant_ReturnsNull()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = NewContext(nameof(OtherTenant_ReturnsNull));
        await SeedAsync(db);
        var repo = new ProcedureInstanceRepository(db);

        var page = await repo.GetStatusHistoryPageAsync(InstanceId, OtherTenantId, 0, 20, ct);

        page.Should().BeNull();
    }

    [Fact]
    public async Task UnknownInstance_ReturnsNull()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = NewContext(nameof(UnknownInstance_ReturnsNull));
        await SeedAsync(db);
        var repo = new ProcedureInstanceRepository(db);

        var page = await repo.GetStatusHistoryPageAsync(Guid.NewGuid(), TenantId, 0, 20, ct);

        page.Should().BeNull();
    }

    [Fact]
    public async Task InstanceWithoutHistory_ReturnsEmptyPageWithZeroTotal()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = NewContext(nameof(InstanceWithoutHistory_ReturnsEmptyPageWithZeroTotal));
        await SeedAsync(db, historyCount: 0);
        var repo = new ProcedureInstanceRepository(db);

        var page = await repo.GetStatusHistoryPageAsync(InstanceId, TenantId, 0, 20, ct);

        page.Should().NotBeNull();
        page!.Value.Total.Should().Be(0);
        page.Value.Items.Should().BeEmpty();
    }
}
