using Flit.Admin.Application.OtDocumentPrecedence;
using Flit.Admin.Application.OtDocumentPrecedence.ListOtDocumentPrecedence;
using Flit.Admin.Application.OtDocumentPrecedence.UpdateOtDocumentPrecedence;
using Flit.Admin.Application.OtDocumentTags;
using Flit.Admin.Application.OtDocumentTags.CreateOtDocumentTag;
using Flit.Admin.Application.OtDocumentTags.ListOtDocumentTags;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Admin;
using Flit.Infrastructure.Persistence.Entities.Tramites;
using Flit.Infrastructure.Persistence.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Flit.Admin.Tests.OtDocumentPrecedence;

/// <summary>Tests prelación documental y etiquetas OT (HU #10222) — AC1–AC5.</summary>
public sealed class OtDocumentPrecedenceHandlerTests
{
    private static readonly Guid TenantA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid TenantB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid ProcedureType = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid DocA = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid DocB = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid ChangedBy = Guid.Parse("44444444-4444-4444-4444-444444444444");

    [Fact]
    public async Task AC1_ListPrecedence_ReturnsSortedWithDocumentNames()
    {
        var db = NewDbName();
        await SeedPrecedenceAsync(db, TenantA, ProcedureType, DocA, 2, "Cédula");
        await SeedPrecedenceAsync(db, TenantA, ProcedureType, DocB, 1, "SOAT");

        await using var ctx = NewContext(db);
        var handler = new ListOtDocumentPrecedenceHandler(new OtDocumentPrecedenceRepository(ctx));
        var result = await handler.HandleAsync(new ListOtDocumentPrecedenceQuery
        {
            TenantId = TenantA,
            ProcedureTypeId = ProcedureType,
        }, TestContext.Current.CancellationToken);

        result.Data.Should().HaveCount(2);
        result.Data[0].DocumentName.Should().Be("SOAT");
        result.Data[0].SortOrder.Should().Be(1);
        result.Data[1].DocumentName.Should().Be("Cédula");
        result.Data[1].SortOrder.Should().Be(2);
    }

    [Fact]
    public async Task AC2_ReorderBatch_UpdatesAllSortOrdersAtomically()
    {
        var db = NewDbName();
        await SeedPrecedenceAsync(db, TenantA, ProcedureType, DocA, 1, "Cédula");
        await SeedPrecedenceAsync(db, TenantA, ProcedureType, DocB, 2, "SOAT");

        await using var ctx = NewContext(db);
        var handler = new UpdateOtDocumentPrecedenceHandler(new OtDocumentPrecedenceRepository(ctx));
        var result = await handler.HandleAsync(new UpdateOtDocumentPrecedenceCommand
        {
            TenantId = TenantA,
            ChangedBy = ChangedBy,
            Request = new UpdateOtDocumentPrecedenceRequest
            {
                ProcedureTypeId = ProcedureType,
                Items =
                [
                    new OtDocumentPrecedenceOrderRequest { DocumentTypeId = DocA, SortOrder = 2 },
                    new OtDocumentPrecedenceOrderRequest { DocumentTypeId = DocB, SortOrder = 1 },
                ],
            },
        }, TestContext.Current.CancellationToken);

        result.Status.Should().Be(UpdateOtDocumentPrecedenceStatus.Updated);
        result.Data[0].DocumentTypeId.Should().Be(DocB);
        result.Data[0].SortOrder.Should().Be(1);
        result.Data[1].DocumentTypeId.Should().Be(DocA);
        result.Data[1].SortOrder.Should().Be(2);
    }

    [Fact]
    public async Task AC3_CreateTag_WithValidHexColor_PersistsForTenant()
    {
        var db = NewDbName();
        await using var ctx = NewContext(db);

        var handler = new CreateOtDocumentTagHandler(new OtDocumentTagRepository(ctx));
        var result = await handler.HandleAsync(new CreateOtDocumentTagCommand
        {
            TenantId = TenantA,
            CreatedBy = ChangedBy,
            Request = new CreateOtDocumentTagRequest
            {
                Code = "URGENTE",
                Name = "Urgente",
                Color = "#FF0000",
            },
        }, TestContext.Current.CancellationToken);

        result.Status.Should().Be(CreateOtDocumentTagStatus.Created);
        result.Tag!.Color.Should().Be("#FF0000");

        await using var verify = NewContext(db);
        var entity = await verify.OtDocumentTags.SingleAsync(cancellationToken: TestContext.Current.CancellationToken);
        entity.TenantId.Should().Be(TenantA);
        entity.Code.Should().Be("URGENTE");
    }

    [Fact]
    public async Task AC3_InvalidHexColor_ReturnsValidationError()
    {
        var db = NewDbName();
        await using var ctx = NewContext(db);

        var handler = new CreateOtDocumentTagHandler(new OtDocumentTagRepository(ctx));
        var result = await handler.HandleAsync(new CreateOtDocumentTagCommand
        {
            TenantId = TenantA,
            Request = new CreateOtDocumentTagRequest
            {
                Code = "BAD",
                Name = "Bad color",
                Color = "FF0000",
            },
        }, TestContext.Current.CancellationToken);

        result.Status.Should().Be(CreateOtDocumentTagStatus.ValidationFailed);
        result.Errors.Should().Contain(e => e.Field == "color" && e.Message == "INVALID_HEX_COLOR");
    }

    [Fact]
    public async Task AC4_DuplicateTagCode_ReturnsConflict()
    {
        var db = NewDbName();
        await using (var seed = NewContext(db))
        {
            seed.OtDocumentTags.Add(new OtDocumentTagEntity
            {
                Id = Guid.NewGuid(),
                TenantId = TenantA,
                Code = "URGENTE",
                Name = "Urgente",
                Color = "#FF0000",
                CreatedAt = DateTimeOffset.UtcNow,
            });
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var ctx = NewContext(db);
        var handler = new CreateOtDocumentTagHandler(new OtDocumentTagRepository(ctx));
        var result = await handler.HandleAsync(new CreateOtDocumentTagCommand
        {
            TenantId = TenantA,
            Request = new CreateOtDocumentTagRequest
            {
                Code = "URGENTE",
                Name = "Otro",
                Color = "#00FF00",
            },
        }, TestContext.Current.CancellationToken);

        result.Status.Should().Be(CreateOtDocumentTagStatus.DuplicateCode);
    }

    [Fact]
    public async Task AC5_RlsIsolation_OnlyReturnsCurrentTenantRows()
    {
        var db = NewDbName();
        await using (var seed = NewContext(db))
        {
            seed.OtDocumentTags.Add(new OtDocumentTagEntity
            {
                Id = Guid.NewGuid(),
                TenantId = TenantA,
                Code = "A",
                Name = "Tenant A",
                Color = "#111111",
                CreatedAt = DateTimeOffset.UtcNow,
            });
            seed.OtDocumentTags.Add(new OtDocumentTagEntity
            {
                Id = Guid.NewGuid(),
                TenantId = TenantB,
                Code = "B",
                Name = "Tenant B",
                Color = "#222222",
                CreatedAt = DateTimeOffset.UtcNow,
            });
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var ctx = NewContext(db);
        var listTags = new ListOtDocumentTagsHandler(new OtDocumentTagRepository(ctx));
        var tagsA = await listTags.HandleAsync(new ListOtDocumentTagsQuery { TenantId = TenantA }, TestContext.Current.CancellationToken);
        tagsA.Data.Should().ContainSingle().Which.Code.Should().Be("A");

        var listPrec = new ListOtDocumentPrecedenceHandler(new OtDocumentPrecedenceRepository(ctx));
        await SeedPrecedenceAsync(db, TenantA, ProcedureType, DocA, 1, "Doc A");
        await SeedPrecedenceAsync(db, TenantB, ProcedureType, DocB, 1, "Doc B");

        var precA = await listPrec.HandleAsync(new ListOtDocumentPrecedenceQuery
        {
            TenantId = TenantA,
            ProcedureTypeId = ProcedureType,
        }, TestContext.Current.CancellationToken);
        precA.Data.Should().ContainSingle().Which.DocumentTypeId.Should().Be(DocA);
    }

    private static async Task SeedPrecedenceAsync(
        string db,
        Guid tenantId,
        Guid procedureTypeId,
        Guid documentTypeId,
        short sortOrder,
        string documentName)
    {
        await using var seed = NewContext(db);
        if (!await seed.DocumentTypes.AnyAsync(d => d.Id == documentTypeId))
        {
            seed.DocumentTypes.Add(new DocumentType
            {
                Id = documentTypeId,
                Code = documentName.ToUpperInvariant(),
                Name = documentName,
                IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow,
            });
        }

        seed.OtDocumentPrecedences.Add(new OtDocumentPrecedenceEntity
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ProcedureTypeId = procedureTypeId,
            DocumentTypeId = documentTypeId,
            SortOrder = sortOrder,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await seed.SaveChangesAsync();
    }

    private static string NewDbName() => Guid.NewGuid().ToString();

    private static FlitDbContext NewContext(string dbName) =>
        new(new DbContextOptionsBuilder<FlitDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options);
}
