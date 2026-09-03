using Flit.Admin.Application.DocumentTypes;
using Flit.Admin.Application.DocumentTypes.CreateDocumentType;
using Flit.Admin.Application.DocumentTypes.DeleteDocumentType;
using Flit.Admin.Application.DocumentTypes.ListDocumentTypes;
using Flit.Admin.Application.DocumentTypes.PurgeDocumentType;
using Flit.Admin.Application.DocumentTypes.ReactivateDocumentType;
using Flit.Admin.Application.DocumentTypes.UpdateDocumentType;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Tramites;
using Flit.Infrastructure.Persistence.Repositories;
// ProcedureType se reubicó al dominio del rework (#10128); el DbSet ProcedureTypes
// ahora es de este tipo. Antes vivía en Persistence.Entities.Tramites.
using Flit.Tramites.Domain.Entities;
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
            }, TestContext.Current.CancellationToken);

            result.IsValid.Should().BeTrue();
            result.Document.Should().NotBeNull();
            created = result.Document!;
            created.Codigo.Should().Be("REGISTRO-UNICO-TRIBUTARIO");
            created.Nombre.Should().Be("Registro Único Tributario");
            created.Estado.Should().Be(DocumentTypeResponse.EstadoActivo);
            created.FechaCreacion.Should().NotBe(default);
            created.EsAutogenerado.Should().BeFalse();
        }

        await using var verify = NewContext(db);
        var row = await verify.DocumentTypes.SingleAsync(d => d.Id == created.Id, cancellationToken: TestContext.Current.CancellationToken);
        row.Code.Should().Be("REGISTRO-UNICO-TRIBUTARIO");
        row.IsActive.Should().BeTrue();
        row.IsSystemGenerated.Should().BeFalse();
        row.CreatedBy.Should().Be(Actor);
    }

    [Fact]
    public async Task AC1_Create_Autogenerado_PersistsIsSystemGenerated()
    {
        var db = NewDbName();

        await using var act = NewContext(db);
        var handler = new CreateDocumentTypeHandler(new DocumentTypeRepository(act));
        var result = await handler.HandleAsync(new CreateDocumentTypeCommand
        {
            CreatedBy = Actor,
            Request = new CreateDocumentTypeRequest(
                "MANDATO", "Mandato", null, null, EsAutogenerado: true),
        }, TestContext.Current.CancellationToken);

        result.IsValid.Should().BeTrue();
        result.Document!.EsAutogenerado.Should().BeTrue();

        var row = await act.DocumentTypes.SingleAsync(
            d => d.Id == result.Document.Id, cancellationToken: TestContext.Current.CancellationToken);
        row.IsSystemGenerated.Should().BeTrue();
        row.GeneratedSortOrder.Should().Be(99);
    }

    [Fact]
    public async Task AC1_Create_IgnoresClientCode_AndSlugsFromName()
    {
        await using var act = NewContext(NewDbName());
        var handler = new CreateDocumentTypeHandler(new DocumentTypeRepository(act));
        var result = await handler.HandleAsync(new CreateDocumentTypeCommand
        {
            CreatedBy = Actor,
            Request = new CreateDocumentTypeRequest("RUT", "Cédula de extranjería", null, null),
        }, TestContext.Current.CancellationToken);

        result.IsValid.Should().BeTrue();
        result.Document!.Codigo.Should().Be("CEDULA-DE-EXTRANJERIA");
    }

    [Fact]
    public async Task AC1_Create_DuplicateName_AllocatesNumericSuffix()
    {
        var db = NewDbName();
        await SeedAsync(db, new DocumentType { Id = Guid.NewGuid(), Code = "MANDATO", Name = "Mandato", IsActive = true, CreatedAt = DateTimeOffset.UtcNow });

        await using var ctx = NewContext(db);
        var handler = new CreateDocumentTypeHandler(new DocumentTypeRepository(ctx));

        var result = await handler.HandleAsync(new CreateDocumentTypeCommand
        {
            Request = new CreateDocumentTypeRequest(null, "Mandato", null, null),
        }, TestContext.Current.CancellationToken);

        result.IsValid.Should().BeTrue();
        result.Document!.Codigo.Should().Be("MANDATO-2");
        (await ctx.DocumentTypes.CountAsync(d => d.Name == "Mandato", cancellationToken: TestContext.Current.CancellationToken)).Should().Be(2);
    }

    [Fact]
    public async Task AC1_Create_ClientCodeCollision_DoesNotFail_WhenNameDiffers()
    {
        var db = NewDbName();
        await SeedAsync(db, new DocumentType { Id = Guid.NewGuid(), Code = "RUT", Name = "Existente", IsActive = true, CreatedAt = DateTimeOffset.UtcNow });

        await using var ctx = NewContext(db);
        var handler = new CreateDocumentTypeHandler(new DocumentTypeRepository(ctx));

        var result = await handler.HandleAsync(new CreateDocumentTypeCommand
        {
            Request = new CreateDocumentTypeRequest("RUT", "Otro documento", null, null),
        }, TestContext.Current.CancellationToken);

        result.IsValid.Should().BeTrue();
        result.Document!.Codigo.Should().Be("OTRO-DOCUMENTO");
        (await ctx.DocumentTypes.CountAsync(cancellationToken: TestContext.Current.CancellationToken)).Should().Be(2);
    }

    [Fact]
    public async Task AC1_Create_InvalidPayload_Returns422()
    {
        await using var ctx = NewContext(NewDbName());
        var handler = new CreateDocumentTypeHandler(new DocumentTypeRepository(ctx));

        var result = await handler.HandleAsync(new CreateDocumentTypeCommand
        {
            Request = new CreateDocumentTypeRequest(null, "", null, null),
        }, TestContext.Current.CancellationToken);

        result.IsValid.Should().BeFalse();
        result.Error.Should().NotBeNullOrWhiteSpace();
        (await ctx.DocumentTypes.CountAsync(cancellationToken: TestContext.Current.CancellationToken)).Should().Be(0);
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

        var result = await handler.HandleAsync(new ListDocumentTypesQuery { Page = 1, PageSize = 20 }, TestContext.Current.CancellationToken);

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

        var page2 = await handler.HandleAsync(new ListDocumentTypesQuery { Page = 2, PageSize = 2 }, TestContext.Current.CancellationToken);

        page2.TotalCount.Should().Be(5);
        page2.Data.Should().HaveCount(2);
        page2.Data.Select(d => d.Nombre).Should().ContainInOrder("Doc 03", "Doc 04");
    }

    [Fact]
    public async Task AC2_List_Search_FiltersByNameOrCode()
    {
        var db = NewDbName();
        await SeedAsync(db,
            new DocumentType { Id = Guid.NewGuid(), Code = "SOAT", Name = "Seguro obligatorio", IsActive = true, CreatedAt = DateTimeOffset.UtcNow },
            new DocumentType { Id = Guid.NewGuid(), Code = "RUT", Name = "Registro tributario", IsActive = true, CreatedAt = DateTimeOffset.UtcNow });

        await using var ctx = NewContext(db);
        var handler = new ListDocumentTypesHandler(new DocumentTypeRepository(ctx));
        var result = await handler.HandleAsync(
            new ListDocumentTypesQuery { Search = "soat", IncludeInactive = true },
            TestContext.Current.CancellationToken);

        result.TotalCount.Should().Be(1);
        result.Data.Single().Codigo.Should().Be("SOAT");
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
            }, TestContext.Current.CancellationToken);

            result.Outcome.Should().Be(UpdateDocumentTypeOutcome.Updated);
            result.Document!.Nombre.Should().Be("Nombre Nuevo");
            result.Document.EsAutogenerado.Should().BeFalse();
        }

        await using var verify = NewContext(db);
        var row = await verify.DocumentTypes.SingleAsync(d => d.Id == id, cancellationToken: TestContext.Current.CancellationToken);
        row.Name.Should().Be("Nombre Nuevo");
        row.Description.Should().Be("Desc nueva");
        row.IsSystemGenerated.Should().BeFalse();
        row.UpdatedBy.Should().Be(Actor);
        row.UpdatedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task AC3_Update_IgnoresClientCode_KeepsSystemCode()
    {
        var db = NewDbName();
        var id = Guid.NewGuid();
        await SeedAsync(db, new DocumentType { Id = id, Code = "RUT", Name = "Viejo", IsActive = true, CreatedAt = DateTimeOffset.UtcNow });

        await using var act = NewContext(db);
        var handler = new UpdateDocumentTypeHandler(new DocumentTypeRepository(act));
        var result = await handler.HandleAsync(new UpdateDocumentTypeCommand
        {
            Id = id,
            UpdatedBy = Actor,
            Request = new UpdateDocumentTypeRequest("HACKED", "Nombre Nuevo", null),
        }, TestContext.Current.CancellationToken);

        result.Outcome.Should().Be(UpdateDocumentTypeOutcome.Updated);
        result.Document!.Codigo.Should().Be("RUT");
        (await act.DocumentTypes.SingleAsync(d => d.Id == id, cancellationToken: TestContext.Current.CancellationToken))
            .Code.Should().Be("RUT");
    }

    [Fact]
    public async Task AC3_Update_EsAutogenerado_PersistsFlag()
    {
        var db = NewDbName();
        var id = Guid.NewGuid();
        await SeedAsync(db, new DocumentType
        {
            Id = id, Code = "SOAT", Name = "SOAT", IsActive = true, CreatedAt = DateTimeOffset.UtcNow,
        });

        await using var act = NewContext(db);
        var handler = new UpdateDocumentTypeHandler(new DocumentTypeRepository(act));
        var result = await handler.HandleAsync(new UpdateDocumentTypeCommand
        {
            Id = id,
            UpdatedBy = Actor,
            Request = new UpdateDocumentTypeRequest("SOAT", "SOAT", null, EsAutogenerado: true),
        }, TestContext.Current.CancellationToken);

        result.Outcome.Should().Be(UpdateDocumentTypeOutcome.Updated);
        result.Document!.EsAutogenerado.Should().BeTrue();

        var row = await act.DocumentTypes.SingleAsync(d => d.Id == id, cancellationToken: TestContext.Current.CancellationToken);
        row.IsSystemGenerated.Should().BeTrue();
        row.GeneratedSortOrder.Should().Be(99);
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
        }, TestContext.Current.CancellationToken);

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
            var result = await handler.HandleAsync(new DeleteDocumentTypeCommand { Id = id, DeletedBy = Actor }, TestContext.Current.CancellationToken);
            result.Outcome.Should().Be(DeleteDocumentTypeOutcome.Deleted);
        }

        await using var verify = NewContext(db);
        var row = await verify.DocumentTypes.SingleAsync(d => d.Id == id, cancellationToken: TestContext.Current.CancellationToken); // sigue existiendo físicamente
        row.IsActive.Should().BeFalse();
        row.UpdatedBy.Should().Be(Actor);
    }

    [Fact]
    public async Task AC4_Delete_NonExistent_ReturnsNotFound()
    {
        await using var ctx = NewContext(NewDbName());
        var handler = new DeleteDocumentTypeHandler(new DocumentTypeRepository(ctx));

        var result = await handler.HandleAsync(new DeleteDocumentTypeCommand { Id = Guid.NewGuid() }, TestContext.Current.CancellationToken);

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
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using (var act = NewContext(db))
        {
            var handler = new DeleteDocumentTypeHandler(new DocumentTypeRepository(act));
            var result = await handler.HandleAsync(new DeleteDocumentTypeCommand { Id = id }, TestContext.Current.CancellationToken);
            result.Outcome.Should().Be(DeleteDocumentTypeOutcome.HasAssociations);
        }

        await using var verify = NewContext(db);
        (await verify.DocumentTypes.SingleAsync(d => d.Id == id, cancellationToken: TestContext.Current.CancellationToken)).IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task Purge_RemovesRowAndRequirementAssociations()
    {
        var db = NewDbName();
        var id = Guid.NewGuid();

        await using (var seed = NewContext(db))
        {
            seed.DocumentTypes.Add(new DocumentType { Id = id, Code = "TMP", Name = "Temporal", IsActive = true, CreatedAt = DateTimeOffset.UtcNow });
            seed.ProcedureDocumentRequirements.Add(new ProcedureDocumentRequirement
            {
                Id = Guid.NewGuid(),
                ProcedureTypeId = Guid.NewGuid(),
                DocumentTypeId = id,
                IsMandatory = true,
                DefaultSortOrder = 0,
                CreatedAt = DateTimeOffset.UtcNow,
            });
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var act = NewContext(db);
        var handler = new PurgeDocumentTypeHandler(new DocumentTypeRepository(act));
        var result = await handler.HandleAsync(
            new PurgeDocumentTypeCommand { Id = id },
            TestContext.Current.CancellationToken);

        result.Outcome.Should().Be(PurgeDocumentTypeOutcome.Purged);
        (await act.DocumentTypes.CountAsync(d => d.Id == id, cancellationToken: TestContext.Current.CancellationToken)).Should().Be(0);
        (await act.ProcedureDocumentRequirements.CountAsync(r => r.DocumentTypeId == id, cancellationToken: TestContext.Current.CancellationToken)).Should().Be(0);
    }

    [Fact]
    public async Task Delete_WithAssociations_IncludesTramiteNames_InResult()
    {
        var db = NewDbName();
        var docId = Guid.NewGuid();
        var procId = Guid.NewGuid();

        await using (var seed = NewContext(db))
        {
            seed.DocumentTypes.Add(new DocumentType { Id = docId, Code = "RUT", Name = "Doc", IsActive = true, CreatedAt = DateTimeOffset.UtcNow });
            seed.ProcedureTypes.Add(new ProcedureType {
            Family = "MATRICULAS", Id = procId, Code = "traspaso", Name = "Traspaso", IsActive = true });
            seed.ProcedureDocumentRequirements.Add(new ProcedureDocumentRequirement
            {
                Id = Guid.NewGuid(),
                ProcedureTypeId = procId,
                DocumentTypeId = docId,
                IsMandatory = true,
                DefaultSortOrder = 0,
                CreatedAt = DateTimeOffset.UtcNow,
            });
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var act = NewContext(db);
        var handler = new DeleteDocumentTypeHandler(new DocumentTypeRepository(act));
        var result = await handler.HandleAsync(new DeleteDocumentTypeCommand { Id = docId }, TestContext.Current.CancellationToken);

        result.Outcome.Should().Be(DeleteDocumentTypeOutcome.HasAssociations);
        result.Associations.Should().ContainSingle();
        result.Associations[0].Codigo.Should().Be("traspaso");
        result.Associations[0].Nombre.Should().Be("Traspaso");
    }

    // ---------- Reactivación (contraparte del soft-delete) ----------

    [Fact]
    public async Task Reactivate_RestoresInactiveDocument_ToActive()
    {
        var db = NewDbName();
        var id = Guid.NewGuid();
        await SeedAsync(db, new DocumentType { Id = id, Code = "RUT", Name = "Doc", IsActive = false, CreatedAt = DateTimeOffset.UtcNow });

        await using (var act = NewContext(db))
        {
            var handler = new ReactivateDocumentTypeHandler(new DocumentTypeRepository(act));
            var result = await handler.HandleAsync(new ReactivateDocumentTypeCommand { Id = id, UpdatedBy = Actor }, TestContext.Current.CancellationToken);
            result.Outcome.Should().Be(ReactivateDocumentTypeOutcome.Reactivated);
        }

        await using var verify = NewContext(db);
        var row = await verify.DocumentTypes.SingleAsync(d => d.Id == id, cancellationToken: TestContext.Current.CancellationToken);
        row.IsActive.Should().BeTrue();
        row.UpdatedBy.Should().Be(Actor);
    }

    [Fact]
    public async Task Reactivate_AlreadyActive_IsIdempotent()
    {
        var db = NewDbName();
        var id = Guid.NewGuid();
        await SeedAsync(db, new DocumentType { Id = id, Code = "RUT", Name = "Doc", IsActive = true, CreatedAt = DateTimeOffset.UtcNow });

        await using var ctx = NewContext(db);
        var handler = new ReactivateDocumentTypeHandler(new DocumentTypeRepository(ctx));

        var result = await handler.HandleAsync(new ReactivateDocumentTypeCommand { Id = id }, TestContext.Current.CancellationToken);

        result.Outcome.Should().Be(ReactivateDocumentTypeOutcome.Reactivated);
        (await ctx.DocumentTypes.SingleAsync(d => d.Id == id, cancellationToken: TestContext.Current.CancellationToken)).IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task Reactivate_NonExistent_ReturnsNotFound()
    {
        await using var ctx = NewContext(NewDbName());
        var handler = new ReactivateDocumentTypeHandler(new DocumentTypeRepository(ctx));

        var result = await handler.HandleAsync(new ReactivateDocumentTypeCommand { Id = Guid.NewGuid() }, TestContext.Current.CancellationToken);

        result.Outcome.Should().Be(ReactivateDocumentTypeOutcome.NotFound);
    }

    // ---------- HU #12065: instrucción de cargue ----------

    /// <summary>AC1 — el alta persiste la instrucción y la devuelve en la respuesta.</summary>
    [Fact]
    public async Task HU12065_Create_PersistsUploadInstructions()
    {
        var db = NewDbName();
        const string Instruccion =
            "Sube el Paz y Salvo de impuestos vehiculares expedido por la Secretaría de Hacienda.";

        DocumentTypeResponse created;
        await using (var act = NewContext(db))
        {
            var handler = new CreateDocumentTypeHandler(new DocumentTypeRepository(act));
            var result = await handler.HandleAsync(new CreateDocumentTypeCommand
            {
                CreatedBy = Actor,
                Request = new CreateDocumentTypeRequest(
                    "PAZ", "Paz y Salvo de Impuestos", "Paz y salvo de impuestos del vehículo", null,
                    InstruccionCargue: Instruccion),
            }, TestContext.Current.CancellationToken);

            result.IsValid.Should().BeTrue();
            created = result.Document!;
            created.InstruccionCargue.Should().Be(Instruccion);
            // La nota interna del admin y el texto del gestor son campos distintos.
            created.Descripcion.Should().Be("Paz y salvo de impuestos del vehículo");
        }

        await using var verify = NewContext(db);
        var row = await verify.DocumentTypes.SingleAsync(d => d.Id == created.Id, cancellationToken: TestContext.Current.CancellationToken);
        row.UploadInstructions.Should().Be(Instruccion);
    }

    /// <summary>AC3 — la instrucción es opcional: sin texto el tipo se crea igual, en null.</summary>
    [Fact]
    public async Task HU12065_Create_WithoutUploadInstructions_LeavesItNull()
    {
        var db = NewDbName();

        await using var act = NewContext(db);
        var handler = new CreateDocumentTypeHandler(new DocumentTypeRepository(act));
        var result = await handler.HandleAsync(new CreateDocumentTypeCommand
        {
            Request = new CreateDocumentTypeRequest("OTRO", "Otro documento", null, null),
        }, TestContext.Current.CancellationToken);

        result.IsValid.Should().BeTrue();
        result.Document!.InstruccionCargue.Should().BeNull();
    }

    /// <summary>AC2 — editar la instrucción no toca la descripción interna.</summary>
    [Fact]
    public async Task HU12065_Update_ReplacesUploadInstructions_AndKeepsDescription()
    {
        var db = NewDbName();
        var id = Guid.NewGuid();
        await SeedAsync(db, new DocumentType
        {
            Id = id,
            Code = "paz_salvo",
            Name = "Paz y Salvo de Impuestos",
            Description = "Nota interna del administrador",
            UploadInstructions = "Texto viejo.",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        });

        await using (var act = NewContext(db))
        {
            var handler = new UpdateDocumentTypeHandler(new DocumentTypeRepository(act));
            var result = await handler.HandleAsync(new UpdateDocumentTypeCommand
            {
                Id = id,
                UpdatedBy = Actor,
                Request = new UpdateDocumentTypeRequest(
                    null, "Paz y Salvo de Impuestos", "Nota interna del administrador",
                    InstruccionCargue: "Texto nuevo del gestor."),
            }, TestContext.Current.CancellationToken);

            result.Outcome.Should().Be(UpdateDocumentTypeOutcome.Updated);
            result.Document!.InstruccionCargue.Should().Be("Texto nuevo del gestor.");
        }

        await using var verify = NewContext(db);
        var row = await verify.DocumentTypes.SingleAsync(d => d.Id == id, cancellationToken: TestContext.Current.CancellationToken);
        row.UploadInstructions.Should().Be("Texto nuevo del gestor.");
        row.Description.Should().Be("Nota interna del administrador");
    }

    /// <summary>
    /// Vaciar el campo borra el texto: el PUT manda el estado completo, igual que con la
    /// descripción. Sin esto el admin no podría quitar una instrucción una vez escrita.
    /// </summary>
    [Fact]
    public async Task HU12065_Update_WithBlankUploadInstructions_ClearsIt()
    {
        var db = NewDbName();
        var id = Guid.NewGuid();
        await SeedAsync(db, new DocumentType
        {
            Id = id,
            Code = "paz_salvo",
            Name = "Paz y Salvo de Impuestos",
            UploadInstructions = "Texto que el admin quiere quitar.",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        });

        await using var act = NewContext(db);
        var handler = new UpdateDocumentTypeHandler(new DocumentTypeRepository(act));
        var result = await handler.HandleAsync(new UpdateDocumentTypeCommand
        {
            Id = id,
            Request = new UpdateDocumentTypeRequest(
                null, "Paz y Salvo de Impuestos", null, InstruccionCargue: "   "),
        }, TestContext.Current.CancellationToken);

        result.Outcome.Should().Be(UpdateDocumentTypeOutcome.Updated);
        result.Document!.InstruccionCargue.Should().BeNull();
    }

    /// <summary>AC4/AC5 — el texto inválido se rechaza y nada se persiste.</summary>
    [Fact]
    public async Task HU12065_Create_WithInvalidUploadInstructions_IsRejected()
    {
        var db = NewDbName();

        await using var act = NewContext(db);
        var handler = new CreateDocumentTypeHandler(new DocumentTypeRepository(act));
        var result = await handler.HandleAsync(new CreateDocumentTypeCommand
        {
            Request = new CreateDocumentTypeRequest(
                "PAZ", "Paz y Salvo", null, null,
                InstruccionCargue: new string('a', DocumentTypeValidator.UploadInstructionsMaxLength + 1)),
        }, TestContext.Current.CancellationToken);

        result.IsValid.Should().BeFalse();
        result.Error.Should().Contain("instrucción de cargue");
        (await act.DocumentTypes.CountAsync(TestContext.Current.CancellationToken)).Should().Be(0);
    }

    /// <summary>El listado también entrega la instrucción (la pantalla de admin la muestra).</summary>
    [Fact]
    public async Task HU12065_List_ReturnsUploadInstructions()
    {
        var db = NewDbName();
        await SeedAsync(db, new DocumentType
        {
            Id = Guid.NewGuid(),
            Code = "paz_salvo",
            Name = "Paz y Salvo de Impuestos",
            UploadInstructions = "Sube el Paz y Salvo de impuestos vehiculares.",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        });

        await using var ctx = NewContext(db);
        var handler = new ListDocumentTypesHandler(new DocumentTypeRepository(ctx));
        var result = await handler.HandleAsync(new ListDocumentTypesQuery(), TestContext.Current.CancellationToken);

        result.Data.Should().ContainSingle()
            .Which.InstruccionCargue.Should().Be("Sube el Paz y Salvo de impuestos vehiculares.");
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
