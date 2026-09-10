using Flit.Admin.Application.DocumentRequirementOverrides.ListDocumentRequirementOverrides;
using Flit.Admin.Application.DocumentRequirementOverrides.SetDocumentRequirementOverride;
using Flit.Admin.Domain.DocumentRequirementOverrides;
using Flit.Admin.Tests.TestDoubles;
using Flit.Infrastructure.Persistence.Entities.Identity;
using Flit.Infrastructure.Persistence.Entities.Tramites;
using Flit.Infrastructure.Persistence.Repositories;
using Flit.Infrastructure.Persistence;
using Flit.Tramites.Domain.Entities;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Flit.Admin.Tests.DocumentRequirementOverrides;

/// <summary>
/// API de obligatoriedad documental por OT (HU #10198): upsert/limpiar de los 3 estados
/// (REQUIRED / OPTIONAL / NOT_APPLICABLE), reglas de negocio (404/422) y listado por OT.
/// Ejercita handlers reales sobre repositorios EF reales con proveedor InMemory. El efecto
/// sobre la matriz (exclusión + obligatoriedad) se prueba en
/// <see cref="ResolvedDocumentMatrixHandlerTests"/>; la seguridad en
/// <see cref="AdminDocumentRequirementOverridesAuthorizationTests"/>.
/// </summary>
public sealed class DocumentRequirementOverrideHandlerTests
{
    private static readonly Guid Actor = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid ProcedureTypeId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid DocId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid SecondDocId = Guid.Parse("66666666-6666-6666-6666-666666666666");

    // Organismo de tránsito existente en el catálogo estático (HU #10192).
    private static readonly Guid TransitOfficeId = Guid.Parse("aaaaaaaa-0001-4000-8000-000000000001");

    [Fact]
    public async Task Set_Required_UpsertsAndPersistsState()
    {
        var db = NewDbName();
        await SeedCatalogAsync(db);

        await using (var act = NewContext(db))
        {
            var result = await SetHandler(act).HandleAsync(Command(DocumentRequirementState.Required), TestContext.Current.CancellationToken);

            result.Outcome.Should().Be(SetDocumentRequirementOverrideOutcome.Set);
            result.Response!.Estado.Should().Be("REQUIRED");
            result.Response.Documento.Codigo.Should().Be("RUT");
        }

        await using var verify = NewContext(db);
        var row = await verify.DocumentRequirementOverrides.SingleAsync(o => o.ProcedureTypeId == ProcedureTypeId && o.DocumentTypeId == DocId && o.TransitOfficeId == TransitOfficeId, cancellationToken: TestContext.Current.CancellationToken);
        row.RequirementState.Should().Be("REQUIRED");
        row.CreatedBy.Should().Be(Actor);
    }

    [Fact]
    public async Task Set_Twice_UpdatesInPlace_WithoutDuplicating()
    {
        var db = NewDbName();
        await SeedCatalogAsync(db);

        await using (var first = NewContext(db))
        {
            await SetHandler(first).HandleAsync(Command(DocumentRequirementState.Required), TestContext.Current.CancellationToken);
        }

        await using (var second = NewContext(db))
        {
            var result = await SetHandler(second).HandleAsync(Command(DocumentRequirementState.NotApplicable), TestContext.Current.CancellationToken);
            result.Outcome.Should().Be(SetDocumentRequirementOverrideOutcome.Set);
            result.Response!.Estado.Should().Be("NOT_APPLICABLE");
        }

        await using var verify = NewContext(db);
        (await verify.DocumentRequirementOverrides.CountAsync(cancellationToken: TestContext.Current.CancellationToken)).Should().Be(1);
        (await verify.DocumentRequirementOverrides.SingleAsync(cancellationToken: TestContext.Current.CancellationToken)).RequirementState.Should().Be("NOT_APPLICABLE");
    }

    [Fact]
    public async Task Set_Default_ClearsExistingOverride()
    {
        var db = NewDbName();
        await SeedCatalogAsync(db);
        await using (var seed = NewContext(db))
        {
            await SetHandler(seed).HandleAsync(Command(DocumentRequirementState.Optional), TestContext.Current.CancellationToken);
        }

        await using (var act = NewContext(db))
        {
            var result = await SetHandler(act).HandleAsync(Command(DocumentRequirementState.Default), TestContext.Current.CancellationToken);
            result.Outcome.Should().Be(SetDocumentRequirementOverrideOutcome.Cleared);
        }

        await using var verify = NewContext(db);
        (await verify.DocumentRequirementOverrides.AnyAsync(cancellationToken: TestContext.Current.CancellationToken)).Should().BeFalse();
    }

    [Fact]
    public async Task Set_Default_WhenNoOverride_IsClearedNoop()
    {
        var db = NewDbName();
        await SeedCatalogAsync(db);

        await using var ctx = NewContext(db);
        var result = await SetHandler(ctx).HandleAsync(Command(DocumentRequirementState.Default), TestContext.Current.CancellationToken);

        result.Outcome.Should().Be(SetDocumentRequirementOverrideOutcome.Cleared);
        (await ctx.DocumentRequirementOverrides.AnyAsync(cancellationToken: TestContext.Current.CancellationToken)).Should().BeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("WHATEVER")]
    public async Task Set_InvalidState_Returns422(string? state)
    {
        var db = NewDbName();
        await SeedCatalogAsync(db);

        await using var ctx = NewContext(db);
        var result = await SetHandler(ctx).HandleAsync(Command(state), TestContext.Current.CancellationToken);

        result.Outcome.Should().Be(SetDocumentRequirementOverrideOutcome.ValidationFailed);
        result.Error.Should().NotBeNullOrWhiteSpace();
        (await ctx.DocumentRequirementOverrides.AnyAsync(cancellationToken: TestContext.Current.CancellationToken)).Should().BeFalse();
    }

    [Fact]
    public async Task Set_DocumentNotAssociatedToProcedure_Returns422()
    {
        var db = NewDbName();
        await SeedCatalogAsync(db);

        // SecondDocId existe pero NO está asociado al trámite.
        await using var ctx = NewContext(db);
        var result = await SetHandler(ctx).HandleAsync(new SetDocumentRequirementOverrideCommand
        {
            Actor = Actor,
            Request = new SetDocumentRequirementOverrideRequest(
                ProcedureTypeId, SecondDocId, TransitOfficeId, DocumentRequirementState.Required),
        }, TestContext.Current.CancellationToken);

        result.Outcome.Should().Be(SetDocumentRequirementOverrideOutcome.ValidationFailed);
        result.Error.Should().Contain("no está asociado");
    }

    [Fact]
    public async Task Set_NonExistentProcedure_Returns404()
    {
        var db = NewDbName();
        await SeedCatalogAsync(db);

        await using var ctx = NewContext(db);
        var result = await SetHandler(ctx).HandleAsync(new SetDocumentRequirementOverrideCommand
        {
            Request = new SetDocumentRequirementOverrideRequest(
                Guid.NewGuid(), DocId, TransitOfficeId, DocumentRequirementState.Required),
        }, TestContext.Current.CancellationToken);

        result.Outcome.Should().Be(SetDocumentRequirementOverrideOutcome.ProcedureTypeNotFound);
    }

    [Fact]
    public async Task Set_NonExistentDocument_Returns404()
    {
        var db = NewDbName();
        await SeedCatalogAsync(db);

        await using var ctx = NewContext(db);
        var result = await SetHandler(ctx).HandleAsync(new SetDocumentRequirementOverrideCommand
        {
            Request = new SetDocumentRequirementOverrideRequest(
                ProcedureTypeId, Guid.NewGuid(), TransitOfficeId, DocumentRequirementState.Required),
        }, TestContext.Current.CancellationToken);

        result.Outcome.Should().Be(SetDocumentRequirementOverrideOutcome.DocumentTypeNotFound);
    }

    [Fact]
    public async Task Set_NonExistentTransitOffice_Returns404()
    {
        var db = NewDbName();
        await SeedCatalogAsync(db);

        await using var ctx = NewContext(db);
        var result = await SetHandler(ctx).HandleAsync(new SetDocumentRequirementOverrideCommand
        {
            Request = new SetDocumentRequirementOverrideRequest(
                ProcedureTypeId, DocId, Guid.NewGuid(), DocumentRequirementState.Required),
        }, TestContext.Current.CancellationToken);

        result.Outcome.Should().Be(SetDocumentRequirementOverrideOutcome.TransitOfficeNotFound);
    }

    [Fact]
    public async Task List_ReturnsOnlyOverridesOfThatOt()
    {
        var db = NewDbName();
        await SeedCatalogAsync(db);
        var otherOt = Guid.Parse("aaaaaaaa-0001-4000-8000-000000000002");
        await using (var seed = NewContext(db))
        {
            seed.DocumentRequirementOverrides.AddRange(
                new DocumentRequirementOverride { Id = Guid.NewGuid(), ProcedureTypeId = ProcedureTypeId, DocumentTypeId = DocId, TransitOfficeId = TransitOfficeId, RequirementState = "REQUIRED", CreatedAt = DateTimeOffset.UtcNow },
                // Otro OT — no debe aparecer.
                new DocumentRequirementOverride { Id = Guid.NewGuid(), ProcedureTypeId = ProcedureTypeId, DocumentTypeId = DocId, TransitOfficeId = otherOt, RequirementState = "OPTIONAL", CreatedAt = DateTimeOffset.UtcNow });
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var ctx = NewContext(db);
        var handler = new ListDocumentRequirementOverridesHandler(new DocumentRequirementOverrideRepository(ctx));
        var result = await handler.HandleAsync(new ListDocumentRequirementOverridesQuery
        {
            ProcedureTypeId = ProcedureTypeId,
            TransitOfficeId = TransitOfficeId,
        }, TestContext.Current.CancellationToken);

        result.Data.Should().HaveCount(1);
        result.Data[0].Estado.Should().Be("REQUIRED");
        result.Data[0].TransitOfficeId.Should().Be(TransitOfficeId);
    }

    // ---------- Helpers ----------

    private static SetDocumentRequirementOverrideCommand Command(string? state) =>
        new()
        {
            Actor = Actor,
            Request = new SetDocumentRequirementOverrideRequest(ProcedureTypeId, DocId, TransitOfficeId, state),
        };

    private static SetDocumentRequirementOverrideHandler SetHandler(FlitDbContext ctx) =>
        new(
            new DocumentRequirementOverrideRepository(ctx),
            new ProcedureTypeCatalog(ctx),
            new DocumentTypeRepository(ctx),
            new ProcedureDocumentRequirementRepository(ctx),
            new StaticTransitOfficeCatalog());

    private static string NewDbName() => $"flit-reqoverride-{Guid.NewGuid()}";

    private static FlitDbContext NewContext(string dbName) =>
        new(new DbContextOptionsBuilder<FlitDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options);

    private static async Task SeedCatalogAsync(string dbName)
    {
        await using var ctx = NewContext(dbName);
        ctx.ProcedureTypes.Add(new ProcedureType {
        Family = "MATRICULAS", Id = ProcedureTypeId, Code = "traspaso", Name = "Traspaso", IsActive = true });
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
