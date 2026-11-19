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

    // ── HU #14173 — GetModificacionesPageAsync ──────────────────────────────────────────────────

    // Cada test usa NewContext(nameof(test)) → base independiente, sin colisiones.
    private static async Task SeedModificacionesAsync(FlitDbContext db, int count = 2)
    {
        db.Users.Add(new User { Id = UserId, Email = $"gestor-{UserId:N}@flit.io", DisplayName = "Ana Gestora" });
        db.ProcedureInstances.Add(new ProcedureInstance
        {
            ProcedureType = ProcedureTypeFixture.Matricula,
            Id = InstanceId,
            TenantId = TenantId,
            ProcedureTypeId = Guid.NewGuid(),
            ReferenceNumber = "TRM-MOD-000001",
            CreatedByUserId = UserId,
            CreatedAt = Base,
        });

        for (var i = 0; i < count; i++)
        {
            db.ProcedureInstanceEvents.Add(new ProcedureInstanceEvent
            {
                Id = Guid.NewGuid(),
                TenantId = TenantId,
                ProcedureInstanceId = InstanceId,
                Tipo = "dato_modificado",
                // El más reciente tiene mayor índice → orden desc = i desc.
                CreatedAt = Base.AddMinutes(i),
                CreatedBy = i % 2 == 0 ? UserId : null,
                Payload = $$"""{"etiqueta":"Campo {{i}}","campo":"field_{{i}}","anterior":"antes_{{i}}","nuevo":"despues_{{i}}"}""",
            });
        }

        await db.SaveChangesAsync();
    }

    [Fact] // AC1: dos eventos → ordenados del más reciente al más antiguo, con nombre resuelto
    public async Task AC1_GetModificacionesPage_DosEventos_OrdenDescYNombreResuelto()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = NewContext(nameof(AC1_GetModificacionesPage_DosEventos_OrdenDescYNombreResuelto));
        await SeedModificacionesAsync(db, count: 2);
        var repo = new ProcedureInstanceRepository(db);

        var page = await repo.GetModificacionesPageAsync(InstanceId, TenantId, 0, 20, ct);

        page.Should().NotBeNull();
        var (items, total) = page!.Value;
        total.Should().Be(2);
        items.Should().HaveCount(2);

        // Orden DESC: el que tiene mayor CreatedAt va primero (i=1 > i=0).
        items[0].ChangedAt.Should().BeAfter(items[1].ChangedAt);
        items[0].Etiqueta.Should().Be("Campo 1");
        items[0].Campo.Should().Be("field_1");
        items[0].Anterior.Should().Be("antes_1");
        items[0].Nuevo.Should().Be("despues_1");
        // i=1 → CreatedBy = null
        items[0].ChangedByName.Should().BeNull();

        items[1].Etiqueta.Should().Be("Campo 0");
        // i=0 → CreatedBy = UserId → nombre resuelto
        items[1].ChangedByUserId.Should().Be(UserId);
        items[1].ChangedByName.Should().Be("Ana Gestora");
    }

    [Fact] // AC2: instancia de otro tenant → null
    public async Task AC2_GetModificacionesPage_OtroTenant_RetornaNull()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = NewContext(nameof(AC2_GetModificacionesPage_OtroTenant_RetornaNull));
        await SeedModificacionesAsync(db, count: 1);
        var repo = new ProcedureInstanceRepository(db);

        var page = await repo.GetModificacionesPageAsync(InstanceId, OtherTenantId, 0, 20, ct);

        page.Should().BeNull();
    }

    [Fact] // AC2: instancia inexistente → null
    public async Task AC2_GetModificacionesPage_InstanciaInexistente_RetornaNull()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = NewContext(nameof(AC2_GetModificacionesPage_InstanciaInexistente_RetornaNull));
        await SeedModificacionesAsync(db, count: 1);
        var repo = new ProcedureInstanceRepository(db);

        var page = await repo.GetModificacionesPageAsync(Guid.NewGuid(), TenantId, 0, 20, ct);

        page.Should().BeNull();
    }

    [Fact] // Items de estados no se mezclan con dato_modificado
    public async Task ItemsDeEstados_NoIncluyen_Modificaciones()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = NewContext(nameof(ItemsDeEstados_NoIncluyen_Modificaciones));
        await SeedAsync(db, historyCount: 2);
        // Agregar un evento dato_modificado
        db.ProcedureInstanceEvents.Add(new ProcedureInstanceEvent
        {
            Id = Guid.NewGuid(),
            TenantId = TenantId,
            ProcedureInstanceId = InstanceId,
            Tipo = "dato_modificado",
            CreatedAt = Base,
            Payload = """{"etiqueta":"Placa","campo":"plate","anterior":"ABC123","nuevo":"DEF456"}""",
        });
        await db.SaveChangesAsync(ct);
        var repo = new ProcedureInstanceRepository(db);

        var statusPage = await repo.GetStatusHistoryPageAsync(InstanceId, TenantId, 0, 20, ct);

        // Solo debe haber filas de status_history, no de events
        statusPage.Should().NotBeNull();
        statusPage!.Value.Items.Should().HaveCount(2);
        statusPage.Value.Items.Should().AllSatisfy(i => i.ToStatus.Should().StartWith("estado_"));
    }
}
