using Flit.Infrastructure.Persistence.Entities.Identity;
using Flit.Infrastructure.Persistence.Repositories;
using Flit.Integration.Tests.Postgres;
using Flit.Queries.Domain;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Enums;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Estados;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Flit.Integration.Tests.Tramites;

/// <summary>
/// HU #13664 (Feature #13636 F-F, Épica #12753) AC3 contra PostgreSQL REAL — el filtro <c>AllowedFamilies</c> del listado y de los
/// conteos: <c>null</c> = sin restricción, lista = solo esas familias (por el TIPO, ADR-0050), vacío = ninguna.
/// <para>Uso de ejemplo: <c>new ProcedureInstanceListFilter { AllowedFamilies = ["MATRICULAS", "TRASPASO"] }</c> deja fuera los de OTROS.</para>
/// </summary>
public sealed class TramitesFamiliasPermitidasPostgresTests(PostgresDatabaseFixture fixture) : PostgresTestBase(fixture)
{
    private static readonly Guid Cliente = new("5a5a5a5a-0001-4000-8000-000000013664");
    private static readonly Guid Gestor = new("5a5a5a5a-0002-4000-8000-000000013664");
    private static readonly Guid Matricula = new("5a5a5a5a-1000-4000-8000-000000000001");
    private static readonly Guid Traspaso = new("5a5a5a5a-1000-4000-8000-000000000002");
    private static readonly Guid Otros = new("5a5a5a5a-1000-4000-8000-000000000003");

    [PostgresFact]
    public async Task AC3_AllowedFamilies_FiltraPorFamiliaDelTipo_EnListadoYConteos()
    {
        await SembrarAsync();
        await using var ctx = NewContext();
        var repo = new ProcedureInstanceRepository(ctx);

        (await IdsAsync(repo, null)).Should().BeEquivalentTo([Matricula, Traspaso, Otros], "null = sin restricción");
        (await IdsAsync(repo, ["MATRICULAS", "TRASPASO"])).Should().BeEquivalentTo([Matricula, Traspaso], "sin otros-tramites.read no aparece OTROS");
        (await IdsAsync(repo, ["otros"])).Should().BeEquivalentTo([Otros], "comparación sin distinguir mayúsculas");
        (await IdsAsync(repo, [])).Should().BeEmpty("sin ningún .read no se ve nada");

        var conteos = await repo.CountByStatusFilteredAsync(
            Cliente, new ProcedureInstanceListFilter { AllowedFamilies = ["TRASPASO"] }, TestContext.Current.CancellationToken);
        conteos.Values.Sum().Should().Be(1);
    }

    // Code review 2026-10-19: el lookup del middleware de «Ver por familia» contra PostgreSQL real.
    [PostgresFact]
    public async Task Lookup_DevuelveLaFamiliaDelTipo_YRespetaElAlcanceDeTenant()
    {
        await SembrarAsync();
        await using var ctx = NewContext();
        var lookup = new ProcedureInstanceFamilyLookup(ctx);
        var ct = TestContext.Current.CancellationToken;

        (await lookup.GetFamilyAsync(Matricula, [Cliente], ct)).Should().Be(ProcedureFamily.Matriculas);
        (await lookup.GetFamilyAsync(Traspaso, null, ct)).Should().Be(ProcedureFamily.Traspaso);
        (await lookup.GetFamilyAsync(Otros, [Guid.NewGuid()], ct)).Should().BeNull("fuera del alcance de tenants no se resuelve");
        (await lookup.GetFamilyAsync(Guid.NewGuid(), [Cliente], ct)).Should().BeNull("id inexistente");

        var familias = await lookup.GetFamiliesAsync([Matricula, Traspaso, Otros], [Cliente], ct);
        familias.Should().HaveCount(3);
        familias[Otros].Should().Be(ProcedureFamily.Otros);
    }

    private static async Task<List<Guid>> IdsAsync(ProcedureInstanceRepository repo, IReadOnlyList<string>? familias)
    {
        var (items, _) = await repo.ListWithSummaryGraphFilteredAsync(
            Cliente, 0, 100, new ProcedureInstanceListFilter { AllowedFamilies = familias },
            ProcedureInstanceSortBy.Radicado, SortDirection.Ascending, TestContext.Current.CancellationToken);
        return [.. items.Select(i => i.Id)];
    }

    private async Task SembrarAsync()
    {
        await using (var ctx = NewContext())
        {
            ctx.Tenants.Add(TenantSeed.New(Cliente, "IT-13664", isGroupParent: false, parentId: null));
            await ctx.SaveChangesAsync();
            ctx.Users.Add(new User
            {
                Id = Gestor,
                Email = "it-13664@flit.test",
                DisplayName = "Gestor 13664",
                Status = "active",
                HomeTenantId = Cliente,
                CreatedAt = DateTimeOffset.UtcNow,
            });
            await ctx.SaveChangesAsync();
        }

        await using (var ctx = NewContext())
        {
            async Task<Guid> TipoDe(string familia) => await ctx.ProcedureTypes.AsNoTracking()
                .Where(t => t.Family == familia).Select(t => t.Id).FirstAsync();

            ctx.ProcedureInstances.AddRange(
                Tramite(Matricula, 1, await TipoDe("MATRICULAS")),
                Tramite(Traspaso, 2, await TipoDe("TRASPASO")),
                Tramite(Otros, 3, await TipoDe("OTROS")));
            await ctx.SaveChangesAsync();
        }
    }

    private static ProcedureInstance Tramite(Guid id, int n, Guid tipo) => new()
    {
        Id = id,
        TenantId = Cliente,
        ProcedureTypeId = tipo,
        ReferenceNumber = $"IT13664-{n}",
        Status = TramiteEstado.Borrador,
        Vin = $"VIN13664{n:D9}",
        CreatedByUserId = Gestor,
        CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-n),
    };
}
