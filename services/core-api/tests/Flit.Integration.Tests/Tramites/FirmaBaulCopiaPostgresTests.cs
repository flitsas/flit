using Flit.Infrastructure.Persistence.Repositories;
using Flit.Integration.Tests.Postgres;
using Flit.Integration.Tests.Tenancy;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace Flit.Integration.Tests.Tramites;

/// <summary>
/// Copia de la firma de baúl al radicar contra PostgreSQL real (DDL 195): la fila y la marca
/// <c>firma_baul_copiada_at</c> se guardan en un mismo guardado, se leen por trámite y en lote, y el índice único
/// parcial admite una sola copia ACTIVA por slot (la reemplazada se marca borrada y deja sitio a la nueva).
/// <para>Datos sintéticos: ningún documento ni nombre reales.</para>
/// </summary>
public sealed class FirmaBaulCopiaPostgresTests(PostgresDatabaseFixture fixture) : PostgresTestBase(fixture)
{
    [PostgresFact]
    public async Task LaCopiaYLaMarcaSePersistenYSeLeenPorTramiteYEnLote()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = await CrearTramiteAsync();

        await using (var ctx = NewContext())
        {
            var repo = new ProcedureInstanceRepository(ctx);
            var instance = (await ctx.ProcedureInstances.SingleAsync(i => i.Id == id, ct));
            instance.FirmaBaulCopiadaAt = DateTimeOffset.UtcNow;
            await repo.AddFirmaBaulCopiasAsync([Copia(id, "comprador"), Copia(id, "vendedor")], ct);
            await ctx.SaveChangesAsync(ct);
        }

        await using (var ctx = NewContext())
        {
            var repo = new ProcedureInstanceRepository(ctx);
            (await ctx.ProcedureInstances.AsNoTracking().SingleAsync(i => i.Id == id, ct)).FirmaBaulCopiadaAt.Should().NotBeNull();

            var porTramite = await repo.ListFirmaBaulCopiasAsync(id, HierarchyScenario.C1, ct);
            porTramite.Select(c => c.SlotKey).Should().BeEquivalentTo("comprador", "vendedor");
            porTramite.Should().OnlyContain(c => c.StoragePath == "copia/propia.png" && c.DocumentNumber == "1000000001");

            var enLote = await repo.ListFirmaBaulCopiasPorInstanciasAsync([id, Guid.NewGuid()], ct);
            enLote.Should().ContainKey(id);
            enLote[id].Should().HaveCount(2);
        }
    }

    [PostgresFact]
    public async Task UnaSolaCopiaActivaPorSlot_LaReemplazadaSeMarcaBorradaYDejaSitioALaNueva()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = await CrearTramiteAsync();

        await using (var ctx = NewContext())
        {
            await new ProcedureInstanceRepository(ctx).AddFirmaBaulCopiasAsync([Copia(id, "comprador")], ct);
            await ctx.SaveChangesAsync(ct);
        }

        // Dos activas del mismo slot: el índice único parcial lo impide.
        await using (var ctx = NewContext())
        {
            await new ProcedureInstanceRepository(ctx).AddFirmaBaulCopiasAsync([Copia(id, "comprador")], ct);
            var act = () => ctx.SaveChangesAsync(ct);
            await act.Should().ThrowAsync<DbUpdateException>();
        }

        // Reemplazo legítimo: la anterior se marca borrada y la nueva entra.
        await using (var ctx = NewContext())
        {
            var repo = new ProcedureInstanceRepository(ctx);
            foreach (var previa in await repo.ListFirmaBaulCopiasAsync(id, HierarchyScenario.C1, ct))
                previa.DeletedAt = DateTimeOffset.UtcNow;
            await repo.AddFirmaBaulCopiasAsync([Copia(id, "comprador")], ct);
            await ctx.SaveChangesAsync(ct);
        }

        await using (var ctx = NewContext())
        {
            (await new ProcedureInstanceRepository(ctx).ListFirmaBaulCopiasAsync(id, HierarchyScenario.C1, ct))
                .Should().ContainSingle();
        }
    }

    private static ProcedureInstanceFirmaBaulCopia Copia(Guid instanceId, string slot) => new()
    {
        TenantId = HierarchyScenario.C1,
        ProcedureInstanceId = instanceId,
        SlotKey = slot,
        PartyRole = slot,
        DocumentType = "CC",
        DocumentNumber = "1000000001",
        SignatureVaultId = Guid.NewGuid(),
        FullName = "Persona Sintetica",
        SignatureHash = "hash",
        VigenciaDesde = new DateOnly(2026, 1, 1),
        VigenciaHasta = new DateOnly(2026, 12, 31),
        CapturedAt = DateTimeOffset.UtcNow,
        StoragePath = "copia/propia.png",
        StorageSha256 = "sha",
        SourceStoragePath = "vault/origen.png",
        CreatedAt = DateTimeOffset.UtcNow,
    };

    private async Task<Guid> CrearTramiteAsync()
    {
        var tipo = await HierarchyScenario.SeedAsync(Fixture);
        await using var ctx = NewContext();
        var fila = await ctx.ProcedureTypes.SingleAsync(t => t.Id == tipo.Id, TestContext.Current.CancellationToken);
        fila.PublicationStatus = Flit.Tramites.Domain.Enums.PublicationStatus.Published;
        fila.WizardEnabled = true;
        await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);

        var handler = new CreateProcedureInstanceHandler(new ProcedureInstanceRepository(ctx), new ProcedureTypeRepository(ctx));
        var (resultado, error) = await handler.HandleAsync(
            new CreateProcedureInstanceRequest(
                HierarchyScenario.C1, tipo.Id, HierarchyScenario.UserOf(HierarchyScenario.C1), TransitOfficeId: null),
            TestContext.Current.CancellationToken);
        error.Should().BeNull();
        return resultado!.Id;
    }
}
