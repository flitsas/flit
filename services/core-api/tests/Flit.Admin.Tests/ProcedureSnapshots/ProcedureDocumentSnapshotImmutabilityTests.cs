using Flit.Admin.Application.ProcedureInstances.CreateProcedureInstance;
using Flit.Admin.Application.ProcedureSnapshots.GetProcedureDocumentRequirements;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Identity;
using Flit.Infrastructure.Persistence.Entities.Tramites;
using Flit.Infrastructure.Persistence.Repositories;
using Flit.Infrastructure.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Flit.Admin.Tests.ProcedureSnapshots;

/// <summary>
/// Inmutabilidad del snapshot documental (HU #10197, AC2 / RF19): un trámite creado con
/// documentos [DocA, DocB] sigue devolviendo [DocA, DocB] aunque el SuperAdmin modifique
/// después la configuración documental del tipo de trámite (agrega DocC). El GET lee el
/// snapshot congelado, no la matriz live.
/// </summary>
public sealed class ProcedureDocumentSnapshotImmutabilityTests
{
    private static readonly Guid ProcedureTypeId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid ClienteId = Guid.Parse("77777777-7777-7777-7777-777777777777");
    private static readonly Guid Actor = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid DocA = Guid.Parse("aaaa1111-1111-1111-1111-111111111111");
    private static readonly Guid DocB = Guid.Parse("bbbb2222-2222-2222-2222-222222222222");
    private static readonly Guid DocC = Guid.Parse("cccc3333-3333-3333-3333-333333333333");

    [Fact]
    public async Task AC2_AddingDocumentToProcedureAfterCreation_DoesNotAlterSnapshot()
    {
        var db = NewDbName();
        await SeedAsync(db);

        // Crear el trámite captura el snapshot con DocA y DocB.
        Guid tramiteId;
        await using (var act = NewContext(db))
        {
            var created = await CreateHandler(act).HandleAsync(new CreateProcedureInstanceCommand
            {
                ActorUserId = Actor,
                Request = new CreateProcedureInstanceRequest(ProcedureTypeId, ClienteId, null, "TR-2026-0007", null),
            });
            created.Outcome.Should().Be(CreateProcedureInstanceOutcome.Created);
            created.Response!.DocumentCount.Should().Be(2);
            tramiteId = created.Response.Id;
        }

        // El SuperAdmin agrega DocC a la configuración del tipo de trámite (cambio posterior).
        await using (var change = NewContext(db))
        {
            change.DocumentTypes.Add(new DocumentType { Id = DocC, Code = "DOCC", Name = "Documento C", IsActive = true, CreatedAt = DateTimeOffset.UtcNow });
            change.ProcedureDocumentRequirements.Add(new ProcedureDocumentRequirement
            {
                Id = Guid.NewGuid(),
                ProcedureTypeId = ProcedureTypeId,
                DocumentTypeId = DocC,
                IsMandatory = true,
                DefaultSortOrder = 30,
                CreatedAt = DateTimeOffset.UtcNow,
            });
            await change.SaveChangesAsync();
        }

        // El GET sigue devolviendo el snapshot original [DocA, DocB], sin DocC.
        await using var ctx = NewContext(db);
        var result = await GetHandler(ctx).HandleAsync(new GetProcedureDocumentRequirementsQuery { TramiteId = tramiteId });

        result.Outcome.Should().Be(GetProcedureDocumentRequirementsOutcome.Found);
        result.Data.Should().HaveCount(2);
        result.Data.Select(d => d.DocumentTypeId).Should().BeEquivalentTo([DocA, DocB]);
        result.Data.Select(d => d.DocumentTypeId).Should().NotContain(DocC);
    }

    // ---------- Helpers ----------

    private static CreateProcedureInstanceHandler CreateHandler(FlitDbContext ctx) =>
        new(
            new ProcedureTypeCatalog(ctx),
            new TenantCatalog(ctx),
            new ResolvedDocumentMatrixResolver(ctx),
            new ProcedureInstanceRepository(ctx));

    private static GetProcedureDocumentRequirementsHandler GetHandler(FlitDbContext ctx) =>
        new(new ProcedureInstanceRepository(ctx), new ProcedureDocumentSnapshotRepository(ctx));

    private static string NewDbName() => $"flit-tramite-immut-{Guid.NewGuid()}";

    private static FlitDbContext NewContext(string dbName) =>
        new(new DbContextOptionsBuilder<FlitDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options);

    private static async Task SeedAsync(string dbName)
    {
        await using var ctx = NewContext(dbName);
        ctx.ProcedureTypes.Add(new ProcedureType { Id = ProcedureTypeId, Code = "TRASPASO", Name = "Traspaso", IsActive = true });
        ctx.Tenants.Add(new Tenant { Id = ClienteId, Code = "C1", LegalName = "Cliente 1" });
        ctx.Users.Add(new User { Id = Actor, Email = "op@cliente.co", DisplayName = "Operador", Status = "active", CreatedAt = DateTimeOffset.UtcNow });
        ctx.DocumentTypes.AddRange(
            new DocumentType { Id = DocA, Code = "DOCA", Name = "Documento A", IsActive = true, CreatedAt = DateTimeOffset.UtcNow },
            new DocumentType { Id = DocB, Code = "DOCB", Name = "Documento B", IsActive = true, CreatedAt = DateTimeOffset.UtcNow });
        ctx.ProcedureDocumentRequirements.AddRange(
            new ProcedureDocumentRequirement { Id = Guid.NewGuid(), ProcedureTypeId = ProcedureTypeId, DocumentTypeId = DocA, IsMandatory = true, DefaultSortOrder = 10, CreatedAt = DateTimeOffset.UtcNow },
            new ProcedureDocumentRequirement { Id = Guid.NewGuid(), ProcedureTypeId = ProcedureTypeId, DocumentTypeId = DocB, IsMandatory = false, DefaultSortOrder = 20, CreatedAt = DateTimeOffset.UtcNow });
        await ctx.SaveChangesAsync();
    }
}
