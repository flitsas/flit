using Flit.Admin.Application.DocumentTypes;
using Flit.Admin.Application.DocumentTypes.CreateDocumentType;
using Flit.Admin.Application.DocumentTypes.DeleteDocumentType;
using Flit.Admin.Application.DocumentTypes.ListDocumentTypes;
using Flit.Admin.Application.DocumentTypes.UpdateDocumentType;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Tramites;
using Flit.Infrastructure.Persistence.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Flit.Admin.Tests.DocumentTypes;

/// <summary>
/// API CRUD del catálogo de tipos de documento (HU #10193) — AC1 (alta), AC2 (listado
/// paginado por nombre), AC3 (actualización), AC4 (soft-delete) y AC6 (delete con
/// asociaciones → 409). Ejercita handlers reales sobre el repositorio EF real con
/// proveedor InMemory. La seguridad SuperAdmin (AC5) se prueba aparte vía
/// <see cref="AdminDocumentTypesAuthorizationTests"/>.
/// </summary>
public sealed class DocumentTypeHandlerTests
{
    private static readonly Guid Actor = Guid.Parse("11111111-1111-1111-1111-111111111111");

    // ---------- AC1: POST crea tipo de documento ----------

    [Fact]
    public async Task AC1_Create_PersistsActiveDocument_AndReturnsIt()
    {
        var db = NewDbName();

        DocumentTypeResponse created;
        await using (var act = NewContext(db))
        {
            var handler = new CreateDocumentTypeHandler(new DocumentTypeRepository(act));
            var result = await handler.HandleAsync(new CreateDocumentTypeCommand
            {
                CreatedBy = Actor,
                Request = new CreateDocumentTypeRequest("RUT", "Registro Único Tributario", "Documento fiscal", Obligatorio: true),
            });

            result.IsValid.Should().BeTrue();
            result.Document.Should().NotBeNull();
            created = result.Document!;
            created.Codigo.Should().Be("RUT");
            created.Nombre.Should().Be("Registro Único Tributario");
            created.Estado.Should().Be(DocumentTypeResponse.EstadoActivo);
            created.FechaCreacion.Should().NotBe(default);
        }

        await using var verify = NewContext(db);
        var row = await verify.DocumentTypes.SingleAsync(d => d.Id == created.Id);
        row.Code.Should().Be("RUT");
        row.IsActive.Should().BeTrue();
        row.CreatedBy.Should().Be(Actor);
    }

    [Fact]
    public async Task AC1_Create_DuplicateCode_Returns422_WithoutPersisting()
    {
        var db = NewDbName();
        await SeedAsync(db, new DocumentType { Id = Guid.NewGuid(), Code = "RUT", Name = "Existente", IsActive = true, CreatedAt = DateTimeOffset.UtcNow });

        await using var ctx = NewContext(db);
        var handler = new CreateDocumentTypeHandler(new DocumentTypeRepository(ctx));

        var result = await handler.HandleAsync(new CreateDocumentTypeCommand
        {
            Request = new CreateDocumentTypeRequest("RUT", "Otro", null, null),
        });

        result.IsValid.Should().BeFalse();
        result.Error.Should().Contain("RUT");
        (await ctx.DocumentTypes.CountAsync(d => d.Code == "RUT")).Should().Be(1);
    }

    [Theory]
    [InlineData("", "Nombre válido")]          // código vacío
    [InlineData("CON ESPACIO", "Nombre")]      // código con caracteres no permitidos
    [InlineData("OK", "")]                       // nombre vacío
    public async Task AC1_Create_InvalidPayload_Returns422(string codigo, string nombre)
    {
        await using var ctx = NewContext(NewDbName());
        var handler = new CreateDocumentTypeHandler(new DocumentTypeRepository(ctx));

        var result = await handler.HandleAsync(new CreateDocumentTypeCommand
        {
            Request = new CreateDocumentTypeRequest(codigo, nombre, null, null),
        });

        result.IsValid.Should().BeFalse();
        result.Error.Should().NotBeNullOrWhiteSpace();
        (await ctx.DocumentTypes.CountAsync()).Should().Be(0);
    }

    // ---------- AC2: GET listado paginado ordenado por nombre asc ----------

    [Fact]
    public async Task AC2_List_ReturnsActiveOrderedByNameAsc_WithTotalCount()
    {
        var db = NewDbName();
        await SeedAsync(db,
            new DocumentType { Id = Guid.NewGuid(), Code = "C", Name = "Cedula", IsActive = true, CreatedAt = DateTimeOffset.UtcNow },
            new DocumentType { Id = Guid.NewGuid(), Code = "A", Name = "Antecedentes", IsActive = true, CreatedAt = DateTimeOffset.UtcNow },
            new DocumentType { Id = Guid.NewGuid(), Code = "X", Name = "Inactivo", IsActive = false, CreatedAt = DateTimeOffset.UtcNow });

        await using var ctx = NewContext(db);
        var handler = new ListDocumentTypesHandler(new DocumentTypeRepository(ctx));

        var result = await handler.HandleAsync(new ListDocumentTypesQuery { Page = 1, PageSize = 20 });

        result.TotalCount.Should().Be(2); // el inactivo se excluye por defecto
        result.Data.Select(d => d.Nombre).Should().ContainInOrder("Antecedentes", "Cedula");
        result.Page.Should().Be(1);
        result.PageSize.Should().Be(20);
    }

    [Fact]
    public async Task AC2_List_Paginates()
    {
        var db = NewDbName();
        var seed = Enumerable.Range(1, 5)
            .Select(i => new DocumentType
            {
                Id = Guid.NewGuid(),
                Code = $"DOC{i}",
                Name = $"Doc {i:00}",
                IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow,
            })
            .ToArray();
        await SeedAsync(db, seed);

        await using var ctx = NewContext(db);
        var handler = new ListDocumentTypesHandler(new DocumentTypeRepository(ctx));

        var page2 = await handler.HandleAsync(new ListDocumentTypesQuery { Page = 2, PageSize = 2 });

        page2.TotalCount.Should().Be(5);
        page2.Data.Should().HaveCount(2);
        page2.Data.Select(d => d.Nombre).Should().ContainInOrder("Doc 03", "Doc 04");
    }

    // ---------- AC3: PUT actualiza ----------

    [Fact]
    public async Task AC3_Update_PersistsChanges_AndReturnsUpdated()
    {
        var db = NewDbName();
        var id = Guid.NewGuid();
        await SeedAsync(db, new DocumentType { Id = id, Code = "RUT", Name = "Viejo", IsActive = true, CreatedAt = DateTimeOffset.UtcNow });

        await using (var act = NewContext(db))
        {
            var handler = new UpdateDocumentTypeHandler(new DocumentTypeRepository(act));
            var result = await handler.HandleAsync(new UpdateDocumentTypeCommand
            {
                Id = id,
                UpdatedBy = Actor,
                Request = new UpdateDocumentTypeRequest("RUT", "Nombre Nuevo", "Desc nueva"),
            });

            result.Outcome.Should().Be(UpdateDocumentTypeOutcome.Updated);
            result.Document!.Nombre.Should().Be("Nombre Nuevo");
        }

        await using var verify = NewContext(db);
        var row = await verify.DocumentTypes.SingleAsync(d => d.Id == id);
        row.Name.Should().Be("Nombre Nuevo");
        row.Description.Should().Be("Desc nueva");
        row.UpdatedBy.Should().Be(Actor);
        row.UpdatedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task AC3_Update_NonExistent_ReturnsNotFound()
    {
        await using var ctx = NewContext(NewDbName());
        var handler = new UpdateDocumentTypeHandler(new DocumentTypeRepository(ctx));

        var result = await handler.HandleAsync(new UpdateDocumentTypeCommand
        {
            Id = Guid.NewGuid(),
            Request = new UpdateDocumentTypeRequest("RUT", "X", null),
        });

        result.Outcome.Should().Be(UpdateDocumentTypeOutcome.NotFound);
    }

    // ---------- AC4: DELETE soft-delete ----------

    [Fact]
    public async Task AC4_Delete_SoftDeletes_WithoutPhysicalRemoval()
    {
        var db = NewDbName();
        var id = Guid.NewGuid();
        await SeedAsync(db, new DocumentType { Id = id, Code = "RUT", Name = "Doc", IsActive = true, CreatedAt = DateTimeOffset.UtcNow });

        await using (var act = NewContext(db))
        {
            var handler = new DeleteDocumentTypeHandler(new DocumentTypeRepository(act));
            var result = await handler.HandleAsync(new DeleteDocumentTypeCommand { Id = id, DeletedBy = Actor });
            result.Outcome.Should().Be(DeleteDocumentTypeOutcome.Deleted);
        }

        await using var verify = NewContext(db);
        var row = await verify.DocumentTypes.SingleAsync(d => d.Id == id); // sigue existiendo físicamente
        row.IsActive.Should().BeFalse();
        row.UpdatedBy.Should().Be(Actor);
    }

    [Fact]
    public async Task AC4_Delete_NonExistent_ReturnsNotFound()
    {
        await using var ctx = NewContext(NewDbName());
        var handler = new DeleteDocumentTypeHandler(new DocumentTypeRepository(ctx));

        var result = await handler.HandleAsync(new DeleteDocumentTypeCommand { Id = Guid.NewGuid() });

        result.Outcome.Should().Be(DeleteDocumentTypeOutcome.NotFound);
    }

    // ---------- AC6: DELETE con asociaciones → 409 ----------

    [Fact]
    public async Task AC6_Delete_WithAssociations_ReturnsConflict_AndKeepsActive()
    {
        var db = NewDbName();
        var id = Guid.NewGuid();

        await using (var seed = NewContext(db))
        {
            seed.DocumentTypes.Add(new DocumentType { Id = id, Code = "RUT", Name = "Doc", IsActive = true, CreatedAt = DateTimeOffset.UtcNow });
            seed.ProcedureDocumentRequirements.Add(new ProcedureDocumentRequirement
            {
                Id = Guid.NewGuid(),
                ProcedureTypeId = Guid.NewGuid(),
                DocumentTypeId = id,
                IsMandatory = true,
                DefaultSortOrder = 0,
                CreatedAt = DateTimeOffset.UtcNow,
            });
            await seed.SaveChangesAsync();
        }

        await using (var act = NewContext(db))
        {
            var handler = new DeleteDocumentTypeHandler(new DocumentTypeRepository(act));
            var result = await handler.HandleAsync(new DeleteDocumentTypeCommand { Id = id });
            result.Outcome.Should().Be(DeleteDocumentTypeOutcome.HasAssociations);
        }

        await using var verify = NewContext(db);
        (await verify.DocumentTypes.SingleAsync(d => d.Id == id)).IsActive.Should().BeTrue();
    }

    // ---------- Helpers ----------

    private static string NewDbName() => $"flit-doctypes-{Guid.NewGuid()}";

    private static FlitDbContext NewContext(string dbName) =>
        new(new DbContextOptionsBuilder<FlitDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options);

    private static async Task SeedAsync(string dbName, params DocumentType[] documents)
    {
        await using var ctx = NewContext(dbName);
        ctx.DocumentTypes.AddRange(documents);
        await ctx.SaveChangesAsync();
    }
}
