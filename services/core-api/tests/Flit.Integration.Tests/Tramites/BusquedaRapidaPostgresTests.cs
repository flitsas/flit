using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Identity;
using Flit.Infrastructure.Persistence.Repositories;
using Flit.Integration.Tests.Postgres;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Estados;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Flit.Integration.Tests.Tramites;

/// <summary>
/// Epic #12686 (HU #12805) — los filtros SQL de la búsqueda rápida contra PostgreSQL REAL. InMemory
/// no traduce a SQL, así que no puede decir si «la última entrada a Entregado» o la lista de ids se
/// ejecutan en el motor: aquí sí se ejecutan.
///
/// <para>Uso de ejemplo: <c>await SembrarAsync()</c> y luego
/// <c>new CountProcedureInstancesByStatusHandler(repo).HandleAsync(new() { TenantId = Cliente, BusquedaRapida = "mas_de_5_dias" })</c>.</para>
/// </summary>
public sealed class BusquedaRapidaPostgresTests(PostgresDatabaseFixture fixture) : PostgresTestBase(fixture)
{
    private static readonly Guid Cliente = new("5a5a5a5a-0001-4000-8000-000000012686");
    private static readonly Guid Gestor = new("5a5a5a5a-0002-4000-8000-000000012686");

    private static readonly Guid Entregado7Dias = Id(1);
    private static readonly Guid Entregado3Dias = Id(2);
    private static readonly Guid Reentregado2Dias = Id(3);
    private static readonly Guid EntregadoSinHistorial12Dias = Id(4);
    private static readonly Guid Borrador = Id(5);
    private static readonly Guid BorradorPausado = Id(6);

    [PostgresFact] // AC1 — más de 5 días: última entrada a Entregado; sin historial, la radicación.
    public async Task MasDe5Dias_CuentaYListaSoloLosEntregadosViejos()
    {
        await SembrarAsync();
        await using var ctx = NewContext();
        var repo = new ProcedureInstanceRepository(ctx);

        var conteos = await new CountProcedureInstancesByStatusHandler(repo).HandleAsync(
            new ProcedureInstanceListRequest { TenantId = Cliente, BusquedaRapida = BusquedaRapida.MasDe5Dias });
        var (items, total) = await new ListProcedureInstancesFilteredHandler(repo).HandleAsync(
            new ProcedureInstanceListRequest { TenantId = Cliente, BusquedaRapida = BusquedaRapida.MasDe5Dias });

        conteos[TramiteEstado.Entregado].Should().Be(2);
        conteos[TramiteEstado.Borrador].Should().Be(0);
        total.Should().Be(2);
        items.Select(i => i.Id).Should().BeEquivalentTo(
            [Entregado7Dias, EntregadoSinHistorial12Dias],
            "el reentregado hace 2 días vuelve a contar desde su última entrada, aunque la primera fue hace 20");
    }

    [PostgresFact] // AC1 — más de 10 días.
    public async Task MasDe10Dias_SoloElDe12Dias()
    {
        await SembrarAsync();
        await using var ctx = NewContext();

        var (items, total) = await new ListProcedureInstancesFilteredHandler(new ProcedureInstanceRepository(ctx))
            .HandleAsync(new ProcedureInstanceListRequest { TenantId = Cliente, BusquedaRapida = BusquedaRapida.MasDe10Dias });

        total.Should().Be(1);
        items.Single().Id.Should().Be(EntregadoSinHistorial12Dias);
    }

    [PostgresFact] // AC5 — la lista de ids se traduce, también vacía (= ninguno, nunca «sin filtro»).
    public async Task IdsIncluidos_SeTraduceAlMotorYVacioNoTraeNada()
    {
        await SembrarAsync();
        await using var ctx = NewContext();
        var repo = new ProcedureInstanceRepository(ctx);

        var dos = await repo.CountByStatusFilteredAsync(
            Cliente, new ProcedureInstanceListFilter { IdsIncluidos = [Borrador, Entregado3Dias] }, CancellationToken.None);
        var ninguno = await repo.CountByStatusFilteredAsync(
            Cliente, new ProcedureInstanceListFilter { IdsIncluidos = [] }, CancellationToken.None);

        dos.Values.Sum().Should().Be(2);
        ninguno.Values.Sum().Should().Be(0);
    }

    [PostgresFact] // AC4 — pausados: la evaluación en memoria corre sobre el grafo real y solo mira borradores.
    public async Task Pausados_SoloDevuelveBorradoresEIncluyeElPausadoManualmente()
    {
        await SembrarAsync();
        await using var ctx = NewContext();

        var (items, total) = await new ListProcedureInstancesFilteredHandler(new ProcedureInstanceRepository(ctx))
            .HandleAsync(new ProcedureInstanceListRequest { TenantId = Cliente, BusquedaRapida = BusquedaRapida.Pausados });

        items.Should().OnlyContain(i => i.Estado == TramiteEstado.Borrador);
        items.Select(i => i.Id).Should().Contain(BorradorPausado);
        total.Should().Be(items.Count);
    }

    // ── Escenario ─────────────────────────────────────────────────────────────────────────

    private async Task SembrarAsync()
    {
        var ahora = DateTimeOffset.UtcNow;
        await using (var ctx = NewContext())
        {
            ctx.Tenants.Add(TenantSeed.New(Cliente, "IT-12686", isGroupParent: false, parentId: null));
            await ctx.SaveChangesAsync();
            ctx.Users.Add(new User
            {
                Id = Gestor,
                Email = "it-12686@flit.test",
                DisplayName = "Gestor 12686",
                Status = "active",
                HomeTenantId = Cliente,
                CreatedAt = ahora.AddDays(-30),
            });
            await ctx.SaveChangesAsync();
        }

        await using (var ctx = NewContext())
        {
            var tipo = await ctx.ProcedureTypes.AsNoTracking()
                .Where(t => t.Code == "MATRICULA_NUEVA")
                .Select(t => t.Id)
                .FirstOrDefaultAsync();
            if (tipo == Guid.Empty)
                tipo = await ctx.ProcedureTypes.AsNoTracking().OrderBy(t => t.Code).Select(t => t.Id).FirstAsync();

            ctx.ProcedureInstances.AddRange(
                Tramite(Entregado7Dias, 1, TramiteEstado.Entregado, tipo, ahora.AddDays(-7)),
                Tramite(Entregado3Dias, 2, TramiteEstado.Entregado, tipo, ahora.AddDays(-3)),
                Tramite(Reentregado2Dias, 3, TramiteEstado.Entregado, tipo, ahora.AddDays(-20)),
                Tramite(EntregadoSinHistorial12Dias, 4, TramiteEstado.Entregado, tipo, ahora.AddDays(-12)),
                Tramite(Borrador, 5, TramiteEstado.Borrador, tipo, submittedAt: null),
                Tramite(BorradorPausado, 6, TramiteEstado.Borrador, tipo, submittedAt: null, pausado: true));
            await ctx.SaveChangesAsync();
        }

        await using (var ctx = NewContext())
        {
            ctx.ProcedureInstanceStatusHistories.AddRange(
                Historia(Entregado7Dias, 1, TramiteEstado.Preparado, TramiteEstado.Entregado, ahora.AddDays(-7)),
                Historia(Entregado3Dias, 2, TramiteEstado.Preparado, TramiteEstado.Entregado, ahora.AddDays(-3)),
                Historia(Reentregado2Dias, 3, TramiteEstado.Preparado, TramiteEstado.Entregado, ahora.AddDays(-20)),
                Historia(Reentregado2Dias, 4, TramiteEstado.Entregado, TramiteEstado.Rechazado, ahora.AddDays(-15)),
                Historia(Reentregado2Dias, 5, TramiteEstado.Rechazado, TramiteEstado.Entregado, ahora.AddDays(-2)));
            await ctx.SaveChangesAsync();
        }
    }

    private static ProcedureInstance Tramite(
        Guid id, int n, string estado, Guid tipo, DateTimeOffset? submittedAt, bool pausado = false) => new()
    {
        Id = id,
        TenantId = Cliente,
        ProcedureTypeId = tipo,
        ReferenceNumber = $"IT12686-{n}",
        Status = estado,
        Vin = $"VIN12686{n:D9}",
        CreatedByUserId = Gestor,
        CreatedAt = DateTimeOffset.UtcNow.AddDays(-30),
        SubmittedAt = submittedAt,
        IsPaused = pausado,
    };

    private static ProcedureInstanceStatusHistory Historia(
        Guid tramite, int n, string desde, string hacia, DateTimeOffset cuando) => new()
    {
        Id = new Guid($"5a5a5a5a-0004-4000-8000-{n:D12}"),
        TenantId = Cliente,
        ProcedureInstanceId = tramite,
        FromStatus = desde,
        ToStatus = hacia,
        ChangedAt = cuando,
        ChangedBy = Gestor,
    };

    private static Guid Id(int n) => new($"5a5a5a5a-0003-4000-8000-{n:D12}");
}
