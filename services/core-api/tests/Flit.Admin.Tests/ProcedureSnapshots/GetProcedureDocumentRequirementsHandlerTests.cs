using Flit.Admin.Application.ProcedureSnapshots.GetProcedureDocumentRequirements;
using Flit.Admin.Domain.ProcedureSnapshots;
using Flit.Infrastructure.Persistence.Entities.Tramites;
using Flit.Infrastructure.Persistence.Repositories;
using Flit.Infrastructure.Persistence;
using Flit.Tramites.Domain.Entities;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Flit.Admin.Tests.ProcedureSnapshots;

/// <summary>
/// Lectura de documentos del snapshot de un trámite (HU #10197) — AC4 (devuelve los
/// documentos del snapshot ordenados por el orden capturado) y los 404 (trámite inexistente
/// o sin snapshot). Ejercita handler + repositorios EF reales sobre InMemory.
/// </summary>
public sealed class GetProcedureDocumentRequirementsHandlerTests
{
    private static readonly Guid TramiteId = Guid.Parse("12121212-1212-1212-1212-121212121212");
    private static readonly Guid TenantId = Guid.Parse("77777777-7777-7777-7777-777777777777");
    private static readonly Guid ProcedureTypeId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid DocA = Guid.Parse("aaaa1111-1111-1111-1111-111111111111");
    private static readonly Guid DocB = Guid.Parse("bbbb2222-2222-2222-2222-222222222222");
    private static readonly Guid DocC = Guid.Parse("cccc3333-3333-3333-3333-333333333333");

    // ---------- AC4: retorna documentos del snapshot ordenados por orden capturado ----------

    [Fact]
    public async Task AC4_Get_ReturnsSnapshotDocuments_OrderedByOrdenResueltoAsc()
    {
        var db = NewDbName();
        // Snapshot con documentos deliberadamente desordenados (20, 1, 5).
        var payload = new ProcedureDocumentSnapshotPayload(
            ProcedureTypeId,
            null,
            TenantId,
            [
                new ProcedureDocumentSnapshotItem(DocB, "DOCB", "Documento B", false, 20, "DEFAULT"),
                new ProcedureDocumentSnapshotItem(DocA, "DOCA", "Documento A", true, 1, "CLIENTE"),
                new ProcedureDocumentSnapshotItem(DocC, "DOCC", "Documento C", true, 5, "OT"),
            ]);
        await SeedTramiteWithSnapshotAsync(db, payload);

        await using var ctx = NewContext(db);
        var result = await Handler(ctx).HandleAsync(new GetProcedureDocumentRequirementsQuery { TramiteId = TramiteId }, TestContext.Current.CancellationToken);

        result.Outcome.Should().Be(GetProcedureDocumentRequirementsOutcome.Found);
        result.Data.Select(d => d.DocumentTypeId).Should().ContainInOrder(DocA, DocC, DocB);
        result.Data.Select(d => d.OrdenResuelto).Should().ContainInOrder((short)1, (short)5, (short)20);

        var docA = result.Data[0];
        docA.Codigo.Should().Be("DOCA");
        docA.Nombre.Should().Be("Documento A");
        docA.Obligatorio.Should().BeTrue();
        docA.NivelAplicado.Should().Be("CLIENTE");
    }

    [Fact]
    public async Task Get_NonExistentTramite_Returns404()
    {
        await using var ctx = NewContext(NewDbName());

        var result = await Handler(ctx).HandleAsync(new GetProcedureDocumentRequirementsQuery { TramiteId = Guid.NewGuid() }, TestContext.Current.CancellationToken);

        result.Outcome.Should().Be(GetProcedureDocumentRequirementsOutcome.NotFound);
    }

    [Fact]
    public async Task Get_TramiteWithoutSnapshot_Returns404()
    {
        var db = NewDbName();
        await using (var seed = NewContext(db))
        {
            seed.ProcedureInstances.Add(new ProcedureInstance
            {
                ProcedureType = ProcedureTypeFixture.Matricula,
                Id = TramiteId,
                TenantId = TenantId,
                ProcedureTypeId = ProcedureTypeId,
                ReferenceNumber = "TR-LEGACY",
                Status = "borrador",
                CreatedByUserId = Guid.NewGuid(),
                CreatedAt = DateTimeOffset.UtcNow,
            });
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var ctx = NewContext(db);
        var result = await Handler(ctx).HandleAsync(new GetProcedureDocumentRequirementsQuery { TramiteId = TramiteId }, TestContext.Current.CancellationToken);

        result.Outcome.Should().Be(GetProcedureDocumentRequirementsOutcome.NotFound);
    }

    // ---------- Helpers ----------

    private static GetProcedureDocumentRequirementsHandler Handler(FlitDbContext ctx) =>
        new(new AdminProcedureInstanceRepository(ctx), new ProcedureDocumentSnapshotRepository(ctx));

    private static string NewDbName() => $"flit-tramite-get-{Guid.NewGuid()}";

    private static FlitDbContext NewContext(string dbName) =>
        new(new DbContextOptionsBuilder<FlitDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options);

    private static async Task SeedTramiteWithSnapshotAsync(string dbName, ProcedureDocumentSnapshotPayload payload)
    {
        await using var ctx = NewContext(dbName);
        ctx.ProcedureInstances.Add(new ProcedureInstance
        {
            ProcedureType = ProcedureTypeFixture.Matricula,
            Id = TramiteId,
            TenantId = TenantId,
            ProcedureTypeId = ProcedureTypeId,
            ReferenceNumber = "TR-2026-0001",
            Status = "borrador",
            CreatedByUserId = Guid.NewGuid(),
            CreatedAt = DateTimeOffset.UtcNow,
        });
        ctx.ProcedureDocumentSnapshots.Add(new ProcedureDocumentSnapshot
        {
            Id = Guid.NewGuid(),
            TenantId = TenantId,
            ProcedureInstanceId = TramiteId,
            Snapshot = SnapshotJson.Serialize(payload),
            SnapshotAt = DateTimeOffset.UtcNow,
        });
        await ctx.SaveChangesAsync();
    }
}
