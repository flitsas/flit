using Flit.Admin.Application.DocumentRequirements.CreateProcedureDocumentRequirement;
using Flit.Admin.Application.DocumentRequirements.DeleteProcedureDocumentRequirement;
using Flit.Admin.Application.DocumentRequirements.ListProcedureDocumentRequirements;
using Flit.Admin.Application.DocumentRequirements.UpdateProcedureDocumentRequirement;
using Flit.Admin.Application.DocumentRequirements;
using Flit.Admin.Domain.DocumentRequirements;
using Flit.Infrastructure.Persistence.Entities.Tramites;
using Flit.Infrastructure.Persistence.Repositories;
using Flit.Infrastructure.Persistence;
using Flit.Tramites.Domain.Entities;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Flit.Admin.Tests.DocumentRequirements;

/// <summary>
/// API de asociación documentos ↔ tipos de trámite (HU #10195) — AC1 (alta), AC2
/// (listado por orden), AC3 (actualización), AC4 (borrado físico + guard) y AC5
/// (documento inactivo → 422). Ejercita handlers reales sobre repositorios EF reales
/// con proveedor InMemory. La seguridad SuperAdmin se prueba aparte vía
/// <see cref="AdminProcedureDocumentRequirementsAuthorizationTests"/>.
/// </summary>
public sealed class ProcedureDocumentRequirementHandlerTests
{
    private static readonly Guid Actor = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid ProcedureTypeId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid ActiveDocId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid InactiveDocId = Guid.Parse("55555555-5555-5555-5555-555555555555");

    // ---------- AC1: POST asocia documento a tipo de trámite ----------

    [Fact]
    public async Task AC1_Create_PersistsAssociation_AndReturnsIt()
    {
        var db = NewDbName();
        await SeedCatalogAsync(db);

        CreateProcedureDocumentRequirementResult created;
        await using (var act = NewContext(db))
        {
            var result = await Create(act).HandleAsync(new CreateProcedureDocumentRequirementCommand
            {
                CreatedBy = Actor,
                Request = new CreateProcedureDocumentRequirementRequest(ProcedureTypeId, ActiveDocId, 2, true),
            }, TestContext.Current.CancellationToken);

            result.Outcome.Should().Be(CreateProcedureDocumentRequirementOutcome.Created);
            result.Response.Should().NotBeNull();
            result.Response!.OrdenDefault.Should().Be((short)2);
            result.Response.Obligatorio.Should().BeTrue();
            result.Response.Documento.Codigo.Should().Be("RUT");
            result.Response.Documento.Estado.Should().Be(ProcedureDocumentRequirementResponse.EstadoActivo);
            created = result;
        }

        await using var verify = NewContext(db);
        var row = await verify.ProcedureDocumentRequirements.SingleAsync(r => r.Id == created.Response!.Id, cancellationToken: TestContext.Current.CancellationToken);
        row.ProcedureTypeId.Should().Be(ProcedureTypeId);
        row.DocumentTypeId.Should().Be(ActiveDocId);
        row.DefaultSortOrder.Should().Be((short)2);
        row.IsMandatory.Should().BeTrue();
        row.CreatedBy.Should().Be(Actor);
    }

    [Fact]
    public async Task AC1_Create_NonExistentProcedureType_Returns404()
    {
        var db = NewDbName();
        await SeedCatalogAsync(db);

        await using var ctx = NewContext(db);
        var result = await Create(ctx).HandleAsync(new CreateProcedureDocumentRequirementCommand
        {
            Request = new CreateProcedureDocumentRequirementRequest(Guid.NewGuid(), ActiveDocId, 0, true),
        }, TestContext.Current.CancellationToken);

        result.Outcome.Should().Be(CreateProcedureDocumentRequirementOutcome.ProcedureTypeNotFound);
        (await ctx.ProcedureDocumentRequirements.CountAsync(cancellationToken: TestContext.Current.CancellationToken)).Should().Be(0);
    }

    [Fact]
    public async Task AC1_Create_NonExistentDocument_Returns404()
    {
        var db = NewDbName();
        await SeedCatalogAsync(db);

        await using var ctx = NewContext(db);
        var result = await Create(ctx).HandleAsync(new CreateProcedureDocumentRequirementCommand
        {
            Request = new CreateProcedureDocumentRequirementRequest(ProcedureTypeId, Guid.NewGuid(), 0, true),
        }, TestContext.Current.CancellationToken);

        result.Outcome.Should().Be(CreateProcedureDocumentRequirementOutcome.DocumentTypeNotFound);
    }

    [Fact]
    public async Task AC1_Create_DuplicatePair_Returns422_WithoutPersisting()
    {
        var db = NewDbName();
        await SeedCatalogAsync(db);
        await using (var seed = NewContext(db))
        {
            seed.ProcedureDocumentRequirements.Add(new ProcedureDocumentRequirement
            {
                Id = Guid.NewGuid(),
                ProcedureTypeId = ProcedureTypeId,
                DocumentTypeId = ActiveDocId,
                IsMandatory = true,
                DefaultSortOrder = 0,
                CreatedAt = DateTimeOffset.UtcNow,
            });
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var ctx = NewContext(db);
        var result = await Create(ctx).HandleAsync(new CreateProcedureDocumentRequirementCommand
        {
            Request = new CreateProcedureDocumentRequirementRequest(ProcedureTypeId, ActiveDocId, 1, false),
        }, TestContext.Current.CancellationToken);

        result.Outcome.Should().Be(CreateProcedureDocumentRequirementOutcome.ValidationFailed);
        (await ctx.ProcedureDocumentRequirements.CountAsync(cancellationToken: TestContext.Current.CancellationToken)).Should().Be(1);
    }

    [Theory]
    [InlineData(null, true)]   // orden faltante
    [InlineData(-1, true)]      // orden negativo
    [InlineData(0, null)]       // obligatorio faltante
    public async Task AC1_Create_InvalidPayload_Returns422(int? orden, bool? obligatorio)
    {
        var db = NewDbName();
        await SeedCatalogAsync(db);

        await using var ctx = NewContext(db);
        var result = await Create(ctx).HandleAsync(new CreateProcedureDocumentRequirementCommand
        {
            Request = new CreateProcedureDocumentRequirementRequest(ProcedureTypeId, ActiveDocId, orden, obligatorio),
        }, TestContext.Current.CancellationToken);

        result.Outcome.Should().Be(CreateProcedureDocumentRequirementOutcome.ValidationFailed);
        result.Error.Should().NotBeNullOrWhiteSpace();
        (await ctx.ProcedureDocumentRequirements.CountAsync(cancellationToken: TestContext.Current.CancellationToken)).Should().Be(0);
    }

    // ---------- AC5: documento inactivo rechazado ----------

    [Fact]
    public async Task AC5_Create_InactiveDocument_Returns422_WithExactMessage()
    {
        var db = NewDbName();
        await SeedCatalogAsync(db);

        await using var ctx = NewContext(db);
        var result = await Create(ctx).HandleAsync(new CreateProcedureDocumentRequirementCommand
        {
            Request = new CreateProcedureDocumentRequirementRequest(ProcedureTypeId, InactiveDocId, 0, true),
        }, TestContext.Current.CancellationToken);

        result.Outcome.Should().Be(CreateProcedureDocumentRequirementOutcome.ValidationFailed);
        result.Error.Should().Be("El tipo de documento está inactivo y no puede asociarse a un trámite");
        (await ctx.ProcedureDocumentRequirements.CountAsync(cancellationToken: TestContext.Current.CancellationToken)).Should().Be(0);
    }

    // ---------- AC2: GET lista por orden default asc ----------

    [Fact]
    public async Task AC2_List_ReturnsAssociationsOrderedByOrdenDefaultAsc()
    {
        var db = NewDbName();
        await SeedCatalogAsync(db);
        await using (var seed = NewContext(db))
        {
            seed.ProcedureDocumentRequirements.AddRange(
                new ProcedureDocumentRequirement { Id = Guid.NewGuid(), ProcedureTypeId = ProcedureTypeId, DocumentTypeId = InactiveDocId, IsMandatory = false, DefaultSortOrder = 5, CreatedAt = DateTimeOffset.UtcNow },
                new ProcedureDocumentRequirement { Id = Guid.NewGuid(), ProcedureTypeId = ProcedureTypeId, DocumentTypeId = ActiveDocId, IsMandatory = true, DefaultSortOrder = 1, CreatedAt = DateTimeOffset.UtcNow },
                new ProcedureDocumentRequirement { Id = Guid.NewGuid(), ProcedureTypeId = Guid.NewGuid(), DocumentTypeId = ActiveDocId, IsMandatory = true, DefaultSortOrder = 0, CreatedAt = DateTimeOffset.UtcNow });
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var ctx = NewContext(db);
        var handler = new ListProcedureDocumentRequirementsHandler(new ProcedureDocumentRequirementRepository(ctx));

        var result = await handler.HandleAsync(new ListProcedureDocumentRequirementsQuery { ProcedureTypeId = ProcedureTypeId }, TestContext.Current.CancellationToken);

        result.Data.Should().HaveCount(2); // solo las del trámite consultado
        result.Data.Select(d => d.OrdenDefault).Should().ContainInOrder((short)1, (short)5);
        result.Data[0].Documento.Codigo.Should().Be("RUT");
        result.Data[1].Documento.Estado.Should().Be(ProcedureDocumentRequirementResponse.EstadoInactivo);
    }

    // ---------- AC3: PUT actualiza orden/obligatoriedad ----------

    [Fact]
    public async Task AC3_Update_PersistsChanges_AndReturnsUpdated()
    {
        var db = NewDbName();
        await SeedCatalogAsync(db);
        var id = Guid.NewGuid();
        await using (var seed = NewContext(db))
        {
            seed.ProcedureDocumentRequirements.Add(new ProcedureDocumentRequirement
            {
                Id = id,
                ProcedureTypeId = ProcedureTypeId,
                DocumentTypeId = ActiveDocId,
                IsMandatory = true,
                DefaultSortOrder = 1,
                CreatedAt = DateTimeOffset.UtcNow,
            });
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using (var act = NewContext(db))
        {
            var handler = new UpdateProcedureDocumentRequirementHandler(new ProcedureDocumentRequirementRepository(act));
            var result = await handler.HandleAsync(new UpdateProcedureDocumentRequirementCommand
            {
                Id = id,
                Request = new UpdateProcedureDocumentRequirementRequest(3, false),
            }, TestContext.Current.CancellationToken);

            result.Outcome.Should().Be(UpdateProcedureDocumentRequirementOutcome.Updated);
            result.Response!.OrdenDefault.Should().Be((short)3);
            result.Response.Obligatorio.Should().BeFalse();
        }

        await using var verify = NewContext(db);
        var row = await verify.ProcedureDocumentRequirements.SingleAsync(r => r.Id == id, cancellationToken: TestContext.Current.CancellationToken);
        row.DefaultSortOrder.Should().Be((short)3);
        row.IsMandatory.Should().BeFalse();
    }

    [Fact]
    public async Task AC3_Update_NonExistent_ReturnsNotFound()
    {
        await using var ctx = NewContext(NewDbName());
        var handler = new UpdateProcedureDocumentRequirementHandler(new ProcedureDocumentRequirementRepository(ctx));

        var result = await handler.HandleAsync(new UpdateProcedureDocumentRequirementCommand
        {
            Id = Guid.NewGuid(),
            Request = new UpdateProcedureDocumentRequirementRequest(0, true),
        }, TestContext.Current.CancellationToken);

        result.Outcome.Should().Be(UpdateProcedureDocumentRequirementOutcome.NotFound);
    }

    // ---------- AC4: DELETE borrado físico + guard ----------

    [Fact]
    public async Task AC4_Delete_RemovesRowPhysically()
    {
        var db = NewDbName();
        await SeedCatalogAsync(db);
        var id = Guid.NewGuid();
        await using (var seed = NewContext(db))
        {
            seed.ProcedureDocumentRequirements.Add(new ProcedureDocumentRequirement
            {
                Id = id,
                ProcedureTypeId = ProcedureTypeId,
                DocumentTypeId = ActiveDocId,
                IsMandatory = true,
                DefaultSortOrder = 0,
                CreatedAt = DateTimeOffset.UtcNow,
            });
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using (var act = NewContext(db))
        {
            var handler = new DeleteProcedureDocumentRequirementHandler(
                new ProcedureDocumentRequirementRepository(act),
                new AllowDeleteUsageGuard());
            var result = await handler.HandleAsync(new DeleteProcedureDocumentRequirementCommand { Id = id }, TestContext.Current.CancellationToken);
            result.Outcome.Should().Be(DeleteProcedureDocumentRequirementOutcome.Deleted);
        }

        await using var verify = NewContext(db);
        (await verify.ProcedureDocumentRequirements.AnyAsync(r => r.Id == id, cancellationToken: TestContext.Current.CancellationToken)).Should().BeFalse();
    }

    [Fact]
    public async Task AC4_Delete_NonExistent_ReturnsNotFound()
    {
        await using var ctx = NewContext(NewDbName());
        var handler = new DeleteProcedureDocumentRequirementHandler(
            new ProcedureDocumentRequirementRepository(ctx),
            new AllowDeleteUsageGuard());

        var result = await handler.HandleAsync(new DeleteProcedureDocumentRequirementCommand { Id = Guid.NewGuid() }, TestContext.Current.CancellationToken);

        result.Outcome.Should().Be(DeleteProcedureDocumentRequirementOutcome.NotFound);
    }

    /// <summary>
    /// Documenta el contrato del guard de uso (AC4): cuando reporta in-use → 409 y la
    /// fila no se borra. El stub de producción permite el borrado hasta la HU #10197.
    /// </summary>
    [Fact]
    public async Task AC4_Delete_WhenInUse_ReturnsConflict_AndKeepsRow()
    {
        var db = NewDbName();
        await SeedCatalogAsync(db);
        var id = Guid.NewGuid();
        await using (var seed = NewContext(db))
        {
            seed.ProcedureDocumentRequirements.Add(new ProcedureDocumentRequirement
            {
                Id = id,
                ProcedureTypeId = ProcedureTypeId,
                DocumentTypeId = ActiveDocId,
                IsMandatory = true,
                DefaultSortOrder = 0,
                CreatedAt = DateTimeOffset.UtcNow,
            });
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using (var act = NewContext(db))
        {
            var handler = new DeleteProcedureDocumentRequirementHandler(
                new ProcedureDocumentRequirementRepository(act),
                new BlockDeleteUsageGuard());
            var result = await handler.HandleAsync(new DeleteProcedureDocumentRequirementCommand { Id = id }, TestContext.Current.CancellationToken);
            result.Outcome.Should().Be(DeleteProcedureDocumentRequirementOutcome.InUse);
        }

        await using var verify = NewContext(db);
        (await verify.ProcedureDocumentRequirements.AnyAsync(r => r.Id == id, cancellationToken: TestContext.Current.CancellationToken)).Should().BeTrue();
    }

    // ---------- Helpers ----------

    private static CreateProcedureDocumentRequirementHandler Create(FlitDbContext ctx) =>
        new(
            new ProcedureDocumentRequirementRepository(ctx),
            new ProcedureTypeCatalog(ctx),
            new DocumentTypeRepository(ctx));

    private static string NewDbName() => $"flit-docreq-{Guid.NewGuid()}";

    private static FlitDbContext NewContext(string dbName) =>
        new(new DbContextOptionsBuilder<FlitDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options);

    /// <summary>Siembra un tipo de trámite y dos tipos de documento (activo e inactivo).</summary>
    private static async Task SeedCatalogAsync(string dbName)
    {
        await using var ctx = NewContext(dbName);
        ctx.ProcedureTypes.Add(new ProcedureType { Id = ProcedureTypeId, Code = "TRASPASO", Name = "Traspaso", IsActive = true });
        ctx.DocumentTypes.AddRange(
            new DocumentType { Id = ActiveDocId, Code = "RUT", Name = "Registro Único Tributario", IsActive = true, CreatedAt = DateTimeOffset.UtcNow },
            new DocumentType { Id = InactiveDocId, Code = "OBSOLETO", Name = "Documento obsoleto", IsActive = false, CreatedAt = DateTimeOffset.UtcNow });
        await ctx.SaveChangesAsync();
    }

    private sealed class AllowDeleteUsageGuard : IProcedureDocumentRequirementUsageGuard
    {
        public Task<bool> IsInUseAsync(Guid requirementId, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);
    }

    private sealed class BlockDeleteUsageGuard : IProcedureDocumentRequirementUsageGuard
    {
        public Task<bool> IsInUseAsync(Guid requirementId, CancellationToken cancellationToken = default) =>
            Task.FromResult(true);
    }
}
