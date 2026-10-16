using Flit.Ict.Domain.Entities;
using Flit.Ict.Infrastructure.ExternalClients;
using Flit.Ict.Infrastructure.Jobs;
using Flit.Ict.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Xunit;

namespace Flit.Ict.Application.Tests.Jobs;

/// <summary>
/// Bug #13445 (reapertura QA) — <c>SendToCoreApiJob</c> relee el master en un scope/DbContext NUEVO antes de
/// llamar a <c>CreateDraftAsync</c>. Sin <c>Include(m =&gt; m.Transformations)</c> la colección llegaba vacía
/// (core-ict no tiene AutoInclude ni lazy loading) y el borrador no traía <c>cambio_color</c> /
/// <c>cambio_carroceria</c>: ni ícono en /tramites ni transformación marcada en el paso 3 del wizard.
/// <para>Los tests persisten con un contexto y cargan con OTRO (misma base InMemory, change tracker vacío),
/// tal como lo hace el job; con el mismo contexto el fix-up del tracker ocultaría el defecto.</para>
/// <para>Uso de ejemplo:
/// <code>
/// var master = await SendToCoreApiJob.LoadMasterForDraftAsync(db, masterId, ct);
/// IctDraftFieldValuesMapper.Map(master!) // contiene cambio_color="true" si el lote declaró el código 7
/// </code></para>
/// </summary>
public sealed class SendToCoreApiJobCargaTransformacionesBug13445Tests
{
    private readonly InMemoryDatabaseRoot _root = new();
    private readonly string _dbName = $"ict-13445-{Guid.NewGuid():N}";

    private IctDbContext NuevoContexto() =>
        new(new DbContextOptionsBuilder<IctDbContext>()
            .UseInMemoryDatabase(_dbName, _root)
            .Options);

    private async Task<Guid> PersistirMasterConTransformacionesAsync(params int[] codigos)
    {
        var master = new ExternalIntegrationMaster
        {
            Id = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            TransactionType = 3,
            Plate = "ABC123",
            Vin = "9BWZZZ377VT004251",
            SellingPrice = 45_000_000m,
            SellingDate = "2026-08-20",
        };
        master.Actors.Add(new ExternalIntegrationActor
        {
            Id = Guid.NewGuid(),
            MasterId = master.Id,
            TenantId = master.TenantId,
        });
        foreach (var codigo in codigos)
        {
            master.Transformations.Add(new ExternalIntegrationMasterTransformation
            {
                MasterId = master.Id,
                TenantId = master.TenantId,
                IdTransformationType = codigo,
                Description = "desc",
            });
        }

        await using var escritura = NuevoContexto();
        escritura.Masters.Add(master);
        await escritura.SaveChangesAsync(TestContext.Current.CancellationToken);
        return master.Id;
    }

    [Fact]
    public async Task LoteConTransformaciones7y6_ElBorradorLlevaCambioColorYCambioCarroceria()
    {
        var masterId = await PersistirMasterConTransformacionesAsync(7, 6);

        await using var lectura = NuevoContexto();
        lectura.ChangeTracker.Entries().Should().BeEmpty("el job carga en un scope nuevo");
        var master = await SendToCoreApiJob.LoadMasterForDraftAsync(
            lectura, masterId, TestContext.Current.CancellationToken);

        master.Should().NotBeNull();
        var valores = IctDraftFieldValuesMapper.Map(master!).ToDictionary(f => f.FieldKey, f => f.ValueText);
        valores.Should().Contain("cambio_color", "true");
        valores.Should().Contain("cambio_carroceria", "true");
    }

    [Fact]
    public async Task LoadMasterForDraft_CargaTransformacionesActoresDesdeBd()
    {
        var masterId = await PersistirMasterConTransformacionesAsync(6, 7, 8);

        await using var lectura = NuevoContexto();
        var master = await SendToCoreApiJob.LoadMasterForDraftAsync(
            lectura, masterId, TestContext.Current.CancellationToken);

        master!.Transformations.Select(t => t.IdTransformationType).Should().BeEquivalentTo([6, 7, 8]);
        master.Actors.Should().HaveCount(1);
    }

    [Fact]
    public async Task LoteSinTransformaciones_NoEmiteBanderasDeTransformacion()
    {
        var masterId = await PersistirMasterConTransformacionesAsync();

        await using var lectura = NuevoContexto();
        var master = await SendToCoreApiJob.LoadMasterForDraftAsync(
            lectura, masterId, TestContext.Current.CancellationToken);

        var claves = IctDraftFieldValuesMapper.Map(master!).Select(f => f.FieldKey).ToList();
        claves.Should().NotContain(["cambio_color", "cambio_carroceria", "blindaje", "cambio_combustible"]);
    }
}
