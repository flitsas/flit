using Flit.Admin.Application.Companies.TransitOffices;
using Flit.Admin.Application.DocumentOrderOverrides.CreateDocumentOrderOverride;
using Flit.Admin.Application.DocumentOrderOverrides.DeleteDocumentOrderOverride;
using Flit.Admin.Application.DocumentOrderOverrides.ListDocumentOrderOverrides;
using Flit.Admin.Application.DocumentOrderOverrides.UpdateDocumentOrderOverride;
using Flit.Admin.Application.DocumentOrderOverrides;
using Flit.Admin.Domain.DocumentOrderOverrides;
using Flit.Infrastructure.Persistence.Entities.Identity;
using Flit.Infrastructure.Persistence.Entities.Tramites;
using Flit.Infrastructure.Persistence.Repositories;
using Flit.Infrastructure.Persistence;
using Flit.Tramites.Domain.Entities;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Flit.Admin.Tests.DocumentOrderOverrides;

/// <summary>
/// API de overrides de orden documental (HU #10196) — AC1 (override OT), AC2 (override
/// CLIENTE), AC5 (listado por scope) y AC6 (borrado físico). Ejercita handlers reales
/// sobre repositorios EF reales con proveedor InMemory. La precedencia de la matriz se
/// prueba en <see cref="ResolvedDocumentMatrixHandlerTests"/> y la seguridad SuperAdmin en
/// <see cref="AdminDocumentOrderOverridesAuthorizationTests"/>.
/// </summary>
public sealed class DocumentOrderOverrideHandlerTests
{
    private static readonly Guid Actor = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid ProcedureTypeId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid DocId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid SecondDocId = Guid.Parse("66666666-6666-6666-6666-666666666666");
    private static readonly Guid ClienteId = Guid.Parse("77777777-7777-7777-7777-777777777777");

    // Organismo de tránsito existente en el catálogo estático (HU #10192).
    private static readonly Guid TransitOfficeId = Guid.Parse("aaaaaaaa-0001-4000-8000-000000000001");

    // ---------- AC1: POST crea override por OT ----------

    [Fact]
    public async Task AC1_Create_OtOverride_PersistsWithScopeOt_AndScopeRefTransitOffice()
    {
        var db = NewDbName();
        await SeedCatalogAsync(db);

        CreateDocumentOrderOverrideResult created;
        await using (var act = NewContext(db))
        {
            var result = await Create(act).HandleAsync(new CreateDocumentOrderOverrideCommand
            {
                Scope = DocumentOrderScope.Ot,
                CreatedBy = Actor,
                Request = new CreateDocumentOrderOverrideRequest(ProcedureTypeId, DocId, TransitOfficeId, null, 2),
            });

            result.Outcome.Should().Be(CreateDocumentOrderOverrideOutcome.Created);
            result.Response!.Scope.Should().Be("OT");
            result.Response.ScopeRefId.Should().Be(TransitOfficeId);
            result.Response.Orden.Should().Be((short)2);
            result.Response.Documento.Codigo.Should().Be("RUT");
            created = result;
        }

        await using var verify = NewContext(db);
        var row = await verify.DocumentOrderOverrides.SingleAsync(o => o.Id == created.Response!.Id);
        row.ScopeType.Should().Be("OT");
        row.ScopeRefId.Should().Be(TransitOfficeId);
        row.SortOrder.Should().Be((short)2);
        row.CreatedBy.Should().Be(Actor);
    }

    [Fact]
    public async Task AC1_Create_OtOverride_NonExistentTransitOffice_Returns404()
    {
        var db = NewDbName();
        await SeedCatalogAsync(db);

        await using var ctx = NewContext(db);
        var result = await Create(ctx).HandleAsync(new CreateDocumentOrderOverrideCommand
        {
            Scope = DocumentOrderScope.Ot,
            Request = new CreateDocumentOrderOverrideRequest(ProcedureTypeId, DocId, Guid.NewGuid(), null, 0),
        });

        result.Outcome.Should().Be(CreateDocumentOrderOverrideOutcome.ScopeRefNotFound);
        (await ctx.DocumentOrderOverrides.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task AC1_Create_OtOverride_MissingTransitOffice_Returns422()
    {
        var db = NewDbName();
        await SeedCatalogAsync(db);

        await using var ctx = NewContext(db);
        var result = await Create(ctx).HandleAsync(new CreateDocumentOrderOverrideCommand
        {
            Scope = DocumentOrderScope.Ot,
            Request = new CreateDocumentOrderOverrideRequest(ProcedureTypeId, DocId, null, null, 0),
        });

        result.Outcome.Should().Be(CreateDocumentOrderOverrideOutcome.ValidationFailed);
        result.Error.Should().NotBeNullOrWhiteSpace();
    }

    // ---------- AC2: POST crea override por Cliente ----------

    [Fact]
    public async Task AC2_Create_ClienteOverride_PersistsWithScopeCliente_AndScopeRefCliente()
    {
        var db = NewDbName();
        await SeedCatalogAsync(db);

        CreateDocumentOrderOverrideResult created;
        await using (var act = NewContext(db))
        {
            var result = await Create(act).HandleAsync(new CreateDocumentOrderOverrideCommand
            {
                Scope = DocumentOrderScope.Cliente,
                CreatedBy = Actor,
                Request = new CreateDocumentOrderOverrideRequest(ProcedureTypeId, DocId, null, ClienteId, 1),
            });

            result.Outcome.Should().Be(CreateDocumentOrderOverrideOutcome.Created);
            result.Response!.Scope.Should().Be("CLIENTE");
            result.Response.ScopeRefId.Should().Be(ClienteId);
            created = result;
        }

        await using var verify = NewContext(db);
        var row = await verify.DocumentOrderOverrides.SingleAsync(o => o.Id == created.Response!.Id);
        row.ScopeType.Should().Be("CLIENTE");
        row.ScopeRefId.Should().Be(ClienteId);
        row.SortOrder.Should().Be((short)1);
    }

    [Fact]
    public async Task AC2_Create_ClienteOverride_NonExistentTenant_Returns404()
    {
        var db = NewDbName();
        await SeedCatalogAsync(db);

        await using var ctx = NewContext(db);
        var result = await Create(ctx).HandleAsync(new CreateDocumentOrderOverrideCommand
        {
            Scope = DocumentOrderScope.Cliente,
            Request = new CreateDocumentOrderOverrideRequest(ProcedureTypeId, DocId, null, Guid.NewGuid(), 0),
        });

        result.Outcome.Should().Be(CreateDocumentOrderOverrideOutcome.ScopeRefNotFound);
    }

    // ---------- Reglas de negocio del POST ----------

    [Fact]
    public async Task Create_NonExistentProcedureType_Returns404()
    {
        var db = NewDbName();
        await SeedCatalogAsync(db);

        await using var ctx = NewContext(db);
        var result = await Create(ctx).HandleAsync(new CreateDocumentOrderOverrideCommand
        {
            Scope = DocumentOrderScope.Ot,
            Request = new CreateDocumentOrderOverrideRequest(Guid.NewGuid(), DocId, TransitOfficeId, null, 0),
        });

        result.Outcome.Should().Be(CreateDocumentOrderOverrideOutcome.ProcedureTypeNotFound);
    }

    [Fact]
    public async Task Create_NonExistentDocument_Returns404()
    {
        var db = NewDbName();
        await SeedCatalogAsync(db);

        await using var ctx = NewContext(db);
        var result = await Create(ctx).HandleAsync(new CreateDocumentOrderOverrideCommand
        {
            Scope = DocumentOrderScope.Ot,
            Request = new CreateDocumentOrderOverrideRequest(ProcedureTypeId, Guid.NewGuid(), TransitOfficeId, null, 0),
        });

        result.Outcome.Should().Be(CreateDocumentOrderOverrideOutcome.DocumentTypeNotFound);
    }

    [Fact]
    public async Task Create_DocumentNotAssociatedToProcedure_Returns422()
    {
        var db = NewDbName();
        await SeedCatalogAsync(db);

        // SecondDocId existe pero NO está asociado al trámite (no hay requirement).
        await using var ctx = NewContext(db);
        var result = await Create(ctx).HandleAsync(new CreateDocumentOrderOverrideCommand
        {
            Scope = DocumentOrderScope.Ot,
            Request = new CreateDocumentOrderOverrideRequest(ProcedureTypeId, SecondDocId, TransitOfficeId, null, 0),
        });

        result.Outcome.Should().Be(CreateDocumentOrderOverrideOutcome.ValidationFailed);
        result.Error.Should().Contain("no está asociado");
        (await ctx.DocumentOrderOverrides.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Create_DuplicateOverride_Returns422_WithoutPersisting()
    {
        var db = NewDbName();
        await SeedCatalogAsync(db);
        await using (var seed = NewContext(db))
        {
            seed.DocumentOrderOverrides.Add(new DocumentOrderOverride
            {
                Id = Guid.NewGuid(),
                ProcedureTypeId = ProcedureTypeId,
                DocumentTypeId = DocId,
                ScopeType = "OT",
                ScopeRefId = TransitOfficeId,
                SortOrder = 0,
                CreatedAt = DateTimeOffset.UtcNow,
            });
            await seed.SaveChangesAsync();
        }

        await using var ctx = NewContext(db);
        var result = await Create(ctx).HandleAsync(new CreateDocumentOrderOverrideCommand
        {
            Scope = DocumentOrderScope.Ot,
            Request = new CreateDocumentOrderOverrideRequest(ProcedureTypeId, DocId, TransitOfficeId, null, 5),
        });

        result.Outcome.Should().Be(CreateDocumentOrderOverrideOutcome.ValidationFailed);
        (await ctx.DocumentOrderOverrides.CountAsync()).Should().Be(1);
    }

    [Theory]
    [InlineData(null)] // orden faltante
    [InlineData(-1)]    // orden negativo
    public async Task Create_InvalidOrden_Returns422(int? orden)
    {
        var db = NewDbName();
        await SeedCatalogAsync(db);

        await using var ctx = NewContext(db);
        var result = await Create(ctx).HandleAsync(new CreateDocumentOrderOverrideCommand
        {
            Scope = DocumentOrderScope.Ot,
            Request = new CreateDocumentOrderOverrideRequest(ProcedureTypeId, DocId, TransitOfficeId, null, orden),
        });

        result.Outcome.Should().Be(CreateDocumentOrderOverrideOutcome.ValidationFailed);
        result.Error.Should().NotBeNullOrWhiteSpace();
        (await ctx.DocumentOrderOverrides.CountAsync()).Should().Be(0);
    }

    // ---------- AC5: GET lista overrides por scope ----------

    [Fact]
    public async Task AC5_List_ReturnsOverridesOfScopeCombination_OrderedByOrdenAsc()
    {
        var db = NewDbName();
        await SeedCatalogAsync(db);
        var otherTransitOffice = Guid.Parse("aaaaaaaa-0001-4000-8000-000000000002");
        await using (var seed = NewContext(db))
        {
            seed.DocumentOrderOverrides.AddRange(
                new DocumentOrderOverride { Id = Guid.NewGuid(), ProcedureTypeId = ProcedureTypeId, DocumentTypeId = SecondDocId, ScopeType = "OT", ScopeRefId = TransitOfficeId, SortOrder = 7, CreatedAt = DateTimeOffset.UtcNow },
                new DocumentOrderOverride { Id = Guid.NewGuid(), ProcedureTypeId = ProcedureTypeId, DocumentTypeId = DocId, ScopeType = "OT", ScopeRefId = TransitOfficeId, SortOrder = 3, CreatedAt = DateTimeOffset.UtcNow },
                // Otra OT — no debe aparecer.
                new DocumentOrderOverride { Id = Guid.NewGuid(), ProcedureTypeId = ProcedureTypeId, DocumentTypeId = DocId, ScopeType = "OT", ScopeRefId = otherTransitOffice, SortOrder = 0, CreatedAt = DateTimeOffset.UtcNow },
                // Scope CLIENTE — no debe aparecer.
                new DocumentOrderOverride { Id = Guid.NewGuid(), ProcedureTypeId = ProcedureTypeId, DocumentTypeId = DocId, ScopeType = "CLIENTE", ScopeRefId = ClienteId, SortOrder = 0, CreatedAt = DateTimeOffset.UtcNow });
            await seed.SaveChangesAsync();
        }

        await using var ctx = NewContext(db);
        var handler = new ListDocumentOrderOverridesHandler(new DocumentOrderOverrideRepository(ctx));

        var result = await handler.HandleAsync(new ListDocumentOrderOverridesQuery
        {
            ProcedureTypeId = ProcedureTypeId,
            Scope = DocumentOrderScope.Ot,
            ScopeRefId = TransitOfficeId,
        });

        result.Data.Should().HaveCount(2);
        result.Data.Select(d => d.Orden).Should().ContainInOrder((short)3, (short)7);
        result.Data.Should().OnlyContain(d => d.Scope == "OT" && d.ScopeRefId == TransitOfficeId);
    }

    // ---------- Reordenamiento (PUT) — arrastre en la lista de overrides (HU #10198) ----------

    [Fact]
    public async Task Update_PersistsNewOrder_AndReturnsUpdatedOverride()
    {
        var db = NewDbName();
        await SeedCatalogAsync(db);
        var id = Guid.NewGuid();
        await using (var seed = NewContext(db))
        {
            seed.DocumentOrderOverrides.Add(new DocumentOrderOverride
            {
                Id = id,
                ProcedureTypeId = ProcedureTypeId,
                DocumentTypeId = DocId,
                ScopeType = "OT",
                ScopeRefId = TransitOfficeId,
                SortOrder = 5,
                CreatedAt = DateTimeOffset.UtcNow,
            });
            await seed.SaveChangesAsync();
        }

        await using (var act = NewContext(db))
        {
            var handler = new UpdateDocumentOrderOverrideHandler(new DocumentOrderOverrideRepository(act));
            var result = await handler.HandleAsync(new UpdateDocumentOrderOverrideCommand { Id = id, Orden = 30 });

            result.Outcome.Should().Be(UpdateDocumentOrderOverrideOutcome.Updated);
            result.Response!.Orden.Should().Be((short)30);
            result.Response.Documento.Codigo.Should().Be("RUT");
        }

        await using var verify = NewContext(db);
        (await verify.DocumentOrderOverrides.SingleAsync(o => o.Id == id)).SortOrder.Should().Be((short)30);
    }

    [Fact]
    public async Task Update_NonExistent_ReturnsNotFound()
    {
        await using var ctx = NewContext(NewDbName());
        var handler = new UpdateDocumentOrderOverrideHandler(new DocumentOrderOverrideRepository(ctx));

        var result = await handler.HandleAsync(new UpdateDocumentOrderOverrideCommand { Id = Guid.NewGuid(), Orden = 1 });

        result.Outcome.Should().Be(UpdateDocumentOrderOverrideOutcome.NotFound);
    }

    [Theory]
    [InlineData(null)] // orden faltante
    [InlineData(-1)]    // orden negativo
    public async Task Update_InvalidOrden_Returns422_WithoutTouchingRow(int? orden)
    {
        var db = NewDbName();
        await SeedCatalogAsync(db);
        var id = Guid.NewGuid();
        await using (var seed = NewContext(db))
        {
            seed.DocumentOrderOverrides.Add(new DocumentOrderOverride
            {
                Id = id,
                ProcedureTypeId = ProcedureTypeId,
                DocumentTypeId = DocId,
                ScopeType = "OT",
                ScopeRefId = TransitOfficeId,
                SortOrder = 5,
                CreatedAt = DateTimeOffset.UtcNow,
            });
            await seed.SaveChangesAsync();
        }

        await using var ctx = NewContext(db);
        var handler = new UpdateDocumentOrderOverrideHandler(new DocumentOrderOverrideRepository(ctx));
        var result = await handler.HandleAsync(new UpdateDocumentOrderOverrideCommand { Id = id, Orden = orden });

        result.Outcome.Should().Be(UpdateDocumentOrderOverrideOutcome.ValidationFailed);
        (await ctx.DocumentOrderOverrides.SingleAsync(o => o.Id == id)).SortOrder.Should().Be((short)5);
    }

    // ---------- AC6: DELETE borrado físico ----------

    [Fact]
    public async Task AC6_Delete_RemovesRowPhysically()
    {
        var db = NewDbName();
        await SeedCatalogAsync(db);
        var id = Guid.NewGuid();
        await using (var seed = NewContext(db))
        {
            seed.DocumentOrderOverrides.Add(new DocumentOrderOverride
            {
                Id = id,
                ProcedureTypeId = ProcedureTypeId,
                DocumentTypeId = DocId,
                ScopeType = "CLIENTE",
                ScopeRefId = ClienteId,
                SortOrder = 0,
                CreatedAt = DateTimeOffset.UtcNow,
            });
            await seed.SaveChangesAsync();
        }

        await using (var act = NewContext(db))
        {
            var handler = new DeleteDocumentOrderOverrideHandler(new DocumentOrderOverrideRepository(act));
            var result = await handler.HandleAsync(new DeleteDocumentOrderOverrideCommand { Id = id });
            result.Outcome.Should().Be(DeleteDocumentOrderOverrideOutcome.Deleted);
        }

        await using var verify = NewContext(db);
        (await verify.DocumentOrderOverrides.AnyAsync(o => o.Id == id)).Should().BeFalse();
    }

    [Fact]
    public async Task AC6_Delete_NonExistent_ReturnsNotFound()
    {
        await using var ctx = NewContext(NewDbName());
        var handler = new DeleteDocumentOrderOverrideHandler(new DocumentOrderOverrideRepository(ctx));

        var result = await handler.HandleAsync(new DeleteDocumentOrderOverrideCommand { Id = Guid.NewGuid() });

        result.Outcome.Should().Be(DeleteDocumentOrderOverrideOutcome.NotFound);
    }

    // ---------- Helpers ----------

    private static CreateDocumentOrderOverrideHandler Create(FlitDbContext ctx) =>
        new(
            new DocumentOrderOverrideRepository(ctx),
            new ProcedureTypeCatalog(ctx),
            new DocumentTypeRepository(ctx),
            new ProcedureDocumentRequirementRepository(ctx),
            new StaticTransitOfficeCatalog(),
            new TenantCatalog(ctx));

    private static string NewDbName() => $"flit-docoverride-{Guid.NewGuid()}";

    private static FlitDbContext NewContext(string dbName) =>
        new(new DbContextOptionsBuilder<FlitDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options);

    /// <summary>
    /// Siembra trámite, cliente, dos documentos y la asociación trámite↔documento de
    /// <see cref="DocId"/> (requisito para crear overrides). <see cref="SecondDocId"/>
    /// existe pero queda sin asociar para probar el 422 de "no asociado".
    /// </summary>
    private static async Task SeedCatalogAsync(string dbName)
    {
        await using var ctx = NewContext(dbName);
        ctx.ProcedureTypes.Add(new ProcedureType { Id = ProcedureTypeId, Code = "TRASPASO", Name = "Traspaso", IsActive = true });
        ctx.Tenants.Add(new Tenant { Id = ClienteId, Code = "CLI-1", LegalName = "Cliente Uno", TaxId = "900111222", TenantType = "RENTING", IsActive = true, CreatedAt = DateTimeOffset.UtcNow });
        ctx.DocumentTypes.AddRange(
            new DocumentType { Id = DocId, Code = "RUT", Name = "Registro Único Tributario", IsActive = true, CreatedAt = DateTimeOffset.UtcNow },
            new DocumentType { Id = SecondDocId, Code = "CC", Name = "Cédula de ciudadanía", IsActive = true, CreatedAt = DateTimeOffset.UtcNow });
        ctx.ProcedureDocumentRequirements.Add(new ProcedureDocumentRequirement
        {
            Id = Guid.NewGuid(),
            ProcedureTypeId = ProcedureTypeId,
            DocumentTypeId = DocId,
            IsMandatory = true,
            DefaultSortOrder = 0,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await ctx.SaveChangesAsync();
    }
}
