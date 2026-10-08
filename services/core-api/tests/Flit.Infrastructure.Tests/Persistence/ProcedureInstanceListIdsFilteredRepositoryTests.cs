using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Repositories;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Estados;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Flit.Infrastructure.Tests.Persistence;

/// <summary>
/// HU #13370 (épica #13216) — <see cref="ProcedureInstanceRepository.ListIdsFilteredAsync"/> sobre EF InMemory:
/// devuelve TODOS los trámites del filtro (sin el tope de página), en el mismo orden que el listado, acotados
/// al tenant y sin los borrados lógicos. Reutiliza <c>ApplyListFilters</c>/<c>ApplyListSort</c>; aquí se fija
/// que la proyección no cambia ni el universo ni el orden respecto a <c>ListWithSummaryGraphFilteredAsync</c>.
/// </summary>
/// <remarks>
/// Uso de ejemplo:
/// <code>
/// var refs = await repo.ListIdsFilteredAsync(tenantId, filtro, ProcedureInstanceSortBy.Default, SortDirection.Descending, ct);
/// </code>
/// </remarks>
public sealed class ProcedureInstanceListIdsFilteredRepositoryTests
{
    private static readonly Guid TenantC = Guid.NewGuid();
    private static readonly Guid TenantD = Guid.NewGuid();
    private static readonly DateTimeOffset Base = new(2026, 7, 1, 12, 0, 0, TimeSpan.Zero);

    private static FlitDbContext NewContext(string dbName) =>
        new(new DbContextOptionsBuilder<FlitDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options);

    private static ProcedureInstance Instancia(
        Guid tenantId, string reference, string estado, int minutos, string? plate = null) => new()
        {
            ProcedureType = ProcedureTypeFixture.For("traspaso"),
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ProcedureTypeId = Guid.NewGuid(),
            ReferenceNumber = reference,
            Consecutivo = RadicadoFixture.ConsecutivoDe(reference),
            Plate = plate,
            Status = estado,
            CreatedByUserId = Guid.NewGuid(),
            CreatedAt = Base.AddMinutes(minutos),
        };

    // ── AC1 — sin tope de página y en el orden del listado ──────────────────────────────

    [Fact]
    public async Task Filtro_350Coincidencias_DevuelveLas350EnElMismoOrdenQueElListado()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = NewContext("ids-350");
        for (var i = 0; i < 350; i++)
            db.ProcedureInstances.Add(Instancia(TenantC, $"R{i:D4}", TramiteEstado.Borrador, i, $"P{i:D4}"));
        for (var i = 0; i < 20; i++)
            db.ProcedureInstances.Add(Instancia(TenantC, $"E{i:D4}", TramiteEstado.Entregado, i));
        await db.SaveChangesAsync(ct);
        var repo = new ProcedureInstanceRepository(db);
        var filtro = new ProcedureInstanceListFilter { Estados = [TramiteEstado.Borrador] };

        var refs = await repo.ListIdsFilteredAsync(
            TenantC, filtro, ProcedureInstanceSortBy.Default, SortDirection.Descending, null, ct);

        var (pagina1, total) = await repo.ListWithSummaryGraphFilteredAsync(
            TenantC, 0, 200, filtro, ProcedureInstanceSortBy.Default, SortDirection.Descending, ct);
        var (pagina2, _) = await repo.ListWithSummaryGraphFilteredAsync(
            TenantC, 200, 200, filtro, ProcedureInstanceSortBy.Default, SortDirection.Descending, ct);

        total.Should().Be(350);
        refs.Should().HaveCount(350, "la selección no tiene el tope de 200 de la página");
        refs.Select(r => r.Id).Should().Equal(pagina1.Concat(pagina2).Select(p => p.Id),
            "mismo orden que el listado para el mismo filtro");
        refs.Should().OnlyContain(r => r.TenantId == TenantC && r.ReferenceNumber.StartsWith('R'));
        refs.First(r => r.ReferenceNumber == "R0007").Plate.Should().Be("P0007");
    }

    [Fact]
    public async Task Filtro_ConOrdenExplicito_RespetaElOrdenDelListado()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = NewContext("ids-orden-placa");
        db.ProcedureInstances.AddRange(
            Instancia(TenantC, "R1", TramiteEstado.Borrador, 1, "CCC111"),
            Instancia(TenantC, "R2", TramiteEstado.Borrador, 2, "AAA111"),
            Instancia(TenantC, "R3", TramiteEstado.Borrador, 3, "BBB111"));
        await db.SaveChangesAsync(ct);
        var repo = new ProcedureInstanceRepository(db);

        var refs = await repo.ListIdsFilteredAsync(
            TenantC, new ProcedureInstanceListFilter(), ProcedureInstanceSortBy.Placa, SortDirection.Ascending, null, ct);

        refs.Select(r => r.Plate).Should().Equal("AAA111", "BBB111", "CCC111");
    }

    // ── AC2 — ids acotados a la compañía ────────────────────────────────────────────────

    [Fact]
    public async Task Ids_TresDeCYUnoDeD_SoloDevuelveLosTresDeC()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = NewContext("ids-otro-tenant");
        var deC = Enumerable.Range(1, 3)
            .Select(i => Instancia(TenantC, $"C{i}", TramiteEstado.Borrador, i)).ToList();
        var deD = Instancia(TenantD, "D1", TramiteEstado.Borrador, 9);
        db.ProcedureInstances.AddRange(deC);
        db.ProcedureInstances.Add(deD);
        await db.SaveChangesAsync(ct);
        var repo = new ProcedureInstanceRepository(db);

        var refs = await repo.ListIdsFilteredAsync(
            TenantC,
            new ProcedureInstanceListFilter { IdsIncluidos = deC.Select(x => x.Id).Append(deD.Id).ToList() },
            ProcedureInstanceSortBy.Default, SortDirection.Descending, null, ct);

        refs.Select(r => r.Id).Should().BeEquivalentTo(deC.Select(x => x.Id));
        refs.Select(r => r.Id).Should().NotContain(deD.Id);
    }

    [Fact]
    public async Task SinTenant_SuperAdmin_VeTodasLasCompanias()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = NewContext("ids-superadmin");
        db.ProcedureInstances.AddRange(
            Instancia(TenantC, "C1", TramiteEstado.Borrador, 1),
            Instancia(TenantD, "D1", TramiteEstado.Borrador, 2));
        await db.SaveChangesAsync(ct);
        var repo = new ProcedureInstanceRepository(db);

        var refs = await repo.ListIdsFilteredAsync(
            null, new ProcedureInstanceListFilter(), ProcedureInstanceSortBy.Default, SortDirection.Descending, null, ct);

        refs.Select(r => r.TenantId).Should().BeEquivalentTo([TenantC, TenantD]);
    }

    // ── AC5 — borrado lógico ────────────────────────────────────────────────────────────

    [Fact]
    public async Task TramiteConDeletedAt_NoApareceNiPorFiltroNiPorIds()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = NewContext("ids-borrado");
        var vivo = Instancia(TenantC, "R1", TramiteEstado.Borrador, 1);
        var borrado = Instancia(TenantC, "R2", TramiteEstado.Borrador, 2);
        borrado.DeletedAt = Base;
        db.ProcedureInstances.AddRange(vivo, borrado);
        await db.SaveChangesAsync(ct);
        var repo = new ProcedureInstanceRepository(db);

        var porFiltro = await repo.ListIdsFilteredAsync(
            TenantC, new ProcedureInstanceListFilter(), ProcedureInstanceSortBy.Default, SortDirection.Descending, null, ct);
        var porIds = await repo.ListIdsFilteredAsync(
            TenantC, new ProcedureInstanceListFilter { IdsIncluidos = [vivo.Id, borrado.Id] },
            ProcedureInstanceSortBy.Default, SortDirection.Descending, null, ct);

        porFiltro.Select(r => r.Id).Should().Equal(vivo.Id);
        porIds.Select(r => r.Id).Should().Equal(vivo.Id);
    }

    // ── Code review épica #13216 (Obs2) — límite de lectura y conteo con el mismo predicado ──

    [Fact]
    public async Task Obs2_ConLimite_DevuelveElPrefijoDelMismoOrden_YElConteoEsElTotalSinLimite()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = NewContext("ids-limite");
        for (var i = 0; i < 30; i++)
            db.ProcedureInstances.Add(Instancia(TenantC, $"R{i:D4}", TramiteEstado.Borrador, i));
        for (var i = 0; i < 5; i++)
            db.ProcedureInstances.Add(Instancia(TenantD, $"D{i:D4}", TramiteEstado.Borrador, i));
        for (var i = 0; i < 4; i++)
            db.ProcedureInstances.Add(Instancia(TenantC, $"E{i:D4}", TramiteEstado.Entregado, i));
        var borrado = Instancia(TenantC, "RX", TramiteEstado.Borrador, 99);
        borrado.DeletedAt = Base;
        db.ProcedureInstances.Add(borrado);
        await db.SaveChangesAsync(ct);
        var repo = new ProcedureInstanceRepository(db);
        var filtro = new ProcedureInstanceListFilter { Estados = [TramiteEstado.Borrador] };

        var todos = await repo.ListIdsFilteredAsync(TenantC, filtro, ProcedureInstanceSortBy.Default, SortDirection.Descending, null, ct);
        var corte = await repo.ListIdsFilteredAsync(TenantC, filtro, ProcedureInstanceSortBy.Default, SortDirection.Descending, 11, ct);
        var holgado = await repo.ListIdsFilteredAsync(TenantC, filtro, ProcedureInstanceSortBy.Default, SortDirection.Descending, 100, ct);

        todos.Should().HaveCount(30);
        corte.Should().Equal(todos.Take(11), "el límite corta después del mismo orden");
        holgado.Should().Equal(todos, "un límite mayor que la selección no la corta");
        (await repo.CountIdsFilteredAsync(TenantC, filtro, ct)).Should().Be(30, "mismo predicado: sin borrados ni otros estados");
        (await repo.CountIdsFilteredAsync(null, filtro, ct)).Should().Be(35, "null = todas las compañías");
        (await repo.CountIdsFilteredAsync(
                TenantC, filtro with { IdsIncluidos = [todos[0].Id, todos[1].Id, borrado.Id, Guid.NewGuid()] }, ct))
            .Should().Be(2, "IdsIncluidos se interseca con el resto del filtro");
    }
}
