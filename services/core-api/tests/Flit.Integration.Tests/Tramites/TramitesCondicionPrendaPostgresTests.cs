using Flit.Infrastructure.Persistence.Entities.Identity;
using Flit.Infrastructure.Persistence.Repositories;
using Flit.Integration.Tests.Postgres;
using Flit.Queries.Domain;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Catalog;
using Flit.Tramites.Domain.Tramites.Estados;
using Flit.Tramites.Domain.Tramites.Services;
using Flit.Tramites.Domain.Tramites.ValueObjects;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Flit.Integration.Tests.Tramites;

/// <summary>
/// Bug #13445 (D2, revisión CR M3 / DB H1) — la condición «Prenda = Sí/No» del listado contra PostgreSQL
/// REAL. Las pruebas de <c>TramitesCondicionesFiltroRepositoryTests</c> corren sobre InMemory, que evalúa
/// el predicado en memoria y no detecta un método sin traducción (el <c>StartsWith('[')</c> que en Npgsql
/// 10 devolvía 500). Aquí el <c>WHERE</c> se ejecuta en el motor sobre las formas reales de la señal RUNT
/// (filas equivalentes a R1–R7 de esa clase) más una decisión vigente.
/// <para>Uso de ejemplo: <c>await SembrarAsync()</c> y
/// <c>repo.ListWithSummaryGraphFilteredAsync(tenant, 0, 100, new() { Condiciones = [Prenda EsAlguno "TRUE"] }, …)</c>.</para>
/// </summary>
public sealed class TramitesCondicionPrendaPostgresTests(PostgresDatabaseFixture fixture) : PostgresTestBase(fixture)
{
    private static readonly Guid Cliente = new("5a5a5a5a-0001-4000-8000-000000013445");
    private static readonly Guid Gestor = new("5a5a5a5a-0002-4000-8000-000000013445");

    private static readonly Guid R1PrendasSi = Id(1);
    private static readonly Guid R2GravamenesNo = Id(2);
    private static readonly Guid R3DetalleConGarantia = Id(3);
    private static readonly Guid R4GravamenSiYOmitir = Id(4);
    private static readonly Guid R5DetalleVacio = Id(5);
    private static readonly Guid R6DetalleEnTexto = Id(6);
    private static readonly Guid R7SinNada = Id(7);
    private static readonly Guid R8DecisionRegistrar = Id(8);

    [PostgresFact]
    public async Task CondicionPrenda_SiYNo_SeEjecutanEnElMotorYSonComplementarias()
    {
        await SembrarAsync();
        await using var ctx = NewContext();
        var repo = new ProcedureInstanceRepository(ctx);

        var (conMarca, totalSi) = await FiltrarAsync(repo, "TRUE");
        var (sinMarca, totalNo) = await FiltrarAsync(repo, "FALSE");

        conMarca.Should().BeEquivalentTo(
            [R1PrendasSi, R3DetalleConGarantia, R4GravamenSiYOmitir, R6DetalleEnTexto, R8DecisionRegistrar],
            "D2: la señal RUNT marca aunque la decisión sea omitir; el detalle en value_text también cuenta");
        sinMarca.Should().BeEquivalentTo([R2GravamenesNo, R5DetalleVacio, R7SinNada]);
        totalSi.Should().Be(5);
        totalNo.Should().Be(3);
    }

    private static async Task<(List<Guid> Ids, int Total)> FiltrarAsync(ProcedureInstanceRepository repo, string valor)
    {
        var (items, total) = await repo.ListWithSummaryGraphFilteredAsync(
            Cliente, 0, 100,
            new ProcedureInstanceListFilter
            {
                Condiciones = [new QueryCondition(TramitesQueryFieldCatalog.Prenda, QueryOperator.EsAlguno, [valor])],
            },
            ProcedureInstanceSortBy.Radicado, SortDirection.Ascending,
            TestContext.Current.CancellationToken);
        return (items.Select(i => i.Id).ToList(), total);
    }

    // ── Escenario ─────────────────────────────────────────────────────────────────────────

    private async Task SembrarAsync()
    {
        await using (var ctx = NewContext())
        {
            ctx.Tenants.Add(TenantSeed.New(Cliente, "IT-13445", isGroupParent: false, parentId: null));
            await ctx.SaveChangesAsync();
            ctx.Users.Add(new User
            {
                Id = Gestor,
                Email = "it-13445@flit.test",
                DisplayName = "Gestor 13445",
                Status = "active",
                HomeTenantId = Cliente,
                CreatedAt = DateTimeOffset.UtcNow,
            });
            await ctx.SaveChangesAsync();
        }

        await using (var ctx = NewContext())
        {
            // Un tipo que NO es de prenda por sí mismo: la marca solo puede venir de la señal o la decisión.
            var tipo = await ctx.ProcedureTypes.AsNoTracking()
                .Where(t => t.Code == TramiteTipologiaCatalog.CodigoTraspasoStandard)
                .Select(t => t.Id)
                .SingleAsync();

            ctx.ProcedureInstances.AddRange(
                Tramite(R1PrendasSi, 1, tipo), Tramite(R2GravamenesNo, 2, tipo), Tramite(R3DetalleConGarantia, 3, tipo),
                Tramite(R4GravamenSiYOmitir, 4, tipo), Tramite(R5DetalleVacio, 5, tipo), Tramite(R6DetalleEnTexto, 6, tipo),
                Tramite(R7SinNada, 7, tipo), Tramite(R8DecisionRegistrar, 8, tipo));
            await ctx.SaveChangesAsync();
        }

        await using (var ctx = NewContext())
        {
            ctx.Set<ProcedureInstanceFieldValue>().AddRange(
                Campo(R1PrendasSi, RuntGravamenSignal.PrendasKey, " si "),
                Campo(R2GravamenesNo, RuntGravamenSignal.GravamenesKey, "NO"),
                Campo(R3DetalleConGarantia, RuntGravamenSignal.DetalleKey, null,
                    """[{"nombreAcreedor":"BANCO DE PRUEBA","numeroDocumentoAcreedor":"900000001"}]"""),
                Campo(R4GravamenSiYOmitir, RuntGravamenSignal.GravamenesKey, "SI"),
                Campo(R5DetalleVacio, RuntGravamenSignal.DetalleKey, null, "[]"),
                Campo(R6DetalleEnTexto, RuntGravamenSignal.DetalleKey, """[{"idPrenda":"77"}]"""));
            ctx.ProcedureInstancePrendas.AddRange(
                Prenda(R4GravamenSiYOmitir, PrendaDecision.Omitir),
                Prenda(R8DecisionRegistrar, PrendaDecision.Registrar, "FINANCIERA DE PRUEBA", "900000002"));
            await ctx.SaveChangesAsync();
        }
    }

    private static Guid Id(int n) => new($"5a5a5a5a-1000-4000-8000-{n:D12}");

    private static ProcedureInstance Tramite(Guid id, int n, Guid tipo) => new()
    {
        Id = id,
        TenantId = Cliente,
        ProcedureTypeId = tipo,
        ReferenceNumber = $"IT13445-{n}",
        Status = TramiteEstado.Borrador,
        Vin = $"VIN13445{n:D9}",
        CreatedByUserId = Gestor,
        CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-n),
    };

    private static ProcedureInstanceFieldValue Campo(Guid instancia, string clave, string? texto, string? json = null) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = Cliente,
        ProcedureInstanceId = instancia,
        FieldKey = clave,
        ValueText = texto,
        ValueJson = json,
        CreatedAt = DateTimeOffset.UtcNow,
    };

    private static ProcedureInstancePrenda Prenda(
        Guid instancia, string decision, string? acreedor = null, string? documento = null) => new()
        {
            Id = Guid.NewGuid(),
            TenantId = Cliente,
            ProcedureInstanceId = instancia,
            Decision = decision,
            Estado = PrendaEstado.Vigente,
            AcreedorNombre = acreedor,
            AcreedorDocumento = documento,
            CreatedAt = DateTimeOffset.UtcNow,
        };
}
