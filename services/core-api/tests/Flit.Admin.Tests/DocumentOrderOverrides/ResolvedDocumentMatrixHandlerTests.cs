using Flit.Admin.Application.DocumentOrderOverrides.DeleteDocumentOrderOverride;
using Flit.Admin.Application.DocumentOrderOverrides.GetResolvedDocumentMatrix;
using Flit.Admin.Domain.DocumentOrderOverrides;
using Flit.Infrastructure.Persistence.Entities.Tramites;
using Flit.Infrastructure.Persistence.Repositories;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Services;
using Flit.Tramites.Domain.Entities;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Flit.Admin.Tests.DocumentOrderOverrides;

/// <summary>
/// Matriz documental resuelta (HU #10196) — AC3 (precedencia Cliente &gt; OT &gt; Default),
/// AC4 (sin overrides → orden Default) y AC6 (tras borrar el override CLIENTE, la fila
/// vuelve al nivel OT o Default). Ejercita el resolutor EF real sobre InMemory.
/// </summary>
public sealed class ResolvedDocumentMatrixHandlerTests
{
    private static readonly Guid ProcedureTypeId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid DocA = Guid.Parse("aaaa1111-1111-1111-1111-111111111111");
    private static readonly Guid DocB = Guid.Parse("bbbb2222-2222-2222-2222-222222222222");
    private static readonly Guid DocC = Guid.Parse("cccc3333-3333-3333-3333-333333333333");
    private static readonly Guid TransitOfficeId = Guid.Parse("aaaaaaaa-0001-4000-8000-000000000001");
    private static readonly Guid ClienteId = Guid.Parse("77777777-7777-7777-7777-777777777777");

    // ---------- AC3: precedencia Cliente > OT > Default ----------

    [Fact]
    public async Task AC3_Resolve_AppliesPrecedence_ClienteOverOtOverDefault()
    {
        var db = NewDbName();
        await SeedRequirementsAsync(db);
        await using (var seed = NewContext(db))
        {
            // DocA: override OT y CLIENTE → gana CLIENTE.
            seed.DocumentOrderOverrides.Add(Override(DocA, "OT", TransitOfficeId, 50));
            seed.DocumentOrderOverrides.Add(Override(DocA, "CLIENTE", ClienteId, 1));
            // DocB: solo override OT → gana OT.
            seed.DocumentOrderOverrides.Add(Override(DocB, "OT", TransitOfficeId, 2));
            // DocC: sin overrides → Default (orden 30 sembrado).
            await seed.SaveChangesAsync();
        }

        await using var ctx = NewContext(db);
        var result = await Handler(ctx).HandleAsync(new GetResolvedDocumentMatrixQuery
        {
            ProcedureTypeId = ProcedureTypeId,
            TransitOfficeId = TransitOfficeId,
            ClienteId = ClienteId,
        });

        result.Outcome.Should().Be(GetResolvedDocumentMatrixOutcome.Resolved);
        result.Data.Should().HaveCount(3);

        var docA = result.Data.Single(d => d.DocumentTypeId == DocA);
        docA.OrdenResuelto.Should().Be((short)1);
        docA.NivelAplicado.Should().Be("CLIENTE");

        var docB = result.Data.Single(d => d.DocumentTypeId == DocB);
        docB.OrdenResuelto.Should().Be((short)2);
        docB.NivelAplicado.Should().Be("OT");

        var docC = result.Data.Single(d => d.DocumentTypeId == DocC);
        docC.OrdenResuelto.Should().Be((short)30);
        docC.NivelAplicado.Should().Be("DEFAULT");

        // Orden global por orden resuelto asc: DocA(1), DocB(2), DocC(30).
        result.Data.Select(d => d.DocumentTypeId).Should().ContainInOrder(DocA, DocB, DocC);
    }

    // ---------- AC4: sin overrides → orden Default ----------

    [Fact]
    public async Task AC4_Resolve_WithoutOverrides_ReturnsDefaultOrder()
    {
        var db = NewDbName();
        await SeedRequirementsAsync(db);

        await using var ctx = NewContext(db);
        var result = await Handler(ctx).HandleAsync(new GetResolvedDocumentMatrixQuery
        {
            ProcedureTypeId = ProcedureTypeId,
        });

        result.Outcome.Should().Be(GetResolvedDocumentMatrixOutcome.Resolved);
        result.Data.Should().OnlyContain(d => d.NivelAplicado == "DEFAULT");
        result.Data.Select(d => d.OrdenResuelto).Should().ContainInOrder((short)10, (short)20, (short)30);
    }

    [Fact]
    public async Task Resolve_NonExistentProcedureType_Returns404()
    {
        await using var ctx = NewContext(NewDbName());

        var result = await Handler(ctx).HandleAsync(new GetResolvedDocumentMatrixQuery
        {
            ProcedureTypeId = Guid.NewGuid(),
        });

        result.Outcome.Should().Be(GetResolvedDocumentMatrixOutcome.ProcedureTypeNotFound);
    }

    [Fact]
    public async Task Resolve_ProcedureWithoutRequirements_Returns200WithEmptyData()
    {
        var db = NewDbName();
        await using (var seed = NewContext(db))
        {
            seed.ProcedureTypes.Add(new ProcedureType { Id = ProcedureTypeId, Code = "X", Name = "X", IsActive = true });
            await seed.SaveChangesAsync();
        }

        await using var ctx = NewContext(db);
        var result = await Handler(ctx).HandleAsync(new GetResolvedDocumentMatrixQuery
        {
            ProcedureTypeId = ProcedureTypeId,
        });

        result.Outcome.Should().Be(GetResolvedDocumentMatrixOutcome.Resolved);
        result.Data.Should().BeEmpty();
    }

    // ---------- AC6: tras borrar override CLIENTE, vuelve a OT o Default ----------

    [Fact]
    public async Task AC6_AfterDeletingClienteOverride_MatrixFallsBackToOtThenDefault()
    {
        var db = NewDbName();
        await SeedRequirementsAsync(db);
        var clienteOverrideId = Guid.NewGuid();
        await using (var seed = NewContext(db))
        {
            // DocA: OT + CLIENTE; DocC sin OT (caerá a Default tras borrar CLIENTE).
            seed.DocumentOrderOverrides.Add(Override(DocA, "OT", TransitOfficeId, 5));
            seed.DocumentOrderOverrides.Add(new DocumentOrderOverride
            {
                Id = clienteOverrideId,
                ProcedureTypeId = ProcedureTypeId,
                DocumentTypeId = DocA,
                ScopeType = "CLIENTE",
                ScopeRefId = ClienteId,
                SortOrder = 1,
                CreatedAt = DateTimeOffset.UtcNow,
            });
            await seed.SaveChangesAsync();
        }

        // Antes del borrado: DocA en nivel CLIENTE.
        await using (var before = NewContext(db))
        {
            var matrix = await Handler(before).HandleAsync(new GetResolvedDocumentMatrixQuery
            {
                ProcedureTypeId = ProcedureTypeId,
                TransitOfficeId = TransitOfficeId,
                ClienteId = ClienteId,
            });
            matrix.Data.Single(d => d.DocumentTypeId == DocA).NivelAplicado.Should().Be("CLIENTE");
        }

        // Borrado del override CLIENTE.
        await using (var act = NewContext(db))
        {
            var delete = new DeleteDocumentOrderOverrideHandler(new DocumentOrderOverrideRepository(act));
            var deleted = await delete.HandleAsync(new DeleteDocumentOrderOverrideCommand { Id = clienteOverrideId });
            deleted.Outcome.Should().Be(DeleteDocumentOrderOverrideOutcome.Deleted);
        }

        // Después: DocA cae al nivel OT.
        await using var after = NewContext(db);
        var resolved = await Handler(after).HandleAsync(new GetResolvedDocumentMatrixQuery
        {
            ProcedureTypeId = ProcedureTypeId,
            TransitOfficeId = TransitOfficeId,
            ClienteId = ClienteId,
        });

        var docA = resolved.Data.Single(d => d.DocumentTypeId == DocA);
        docA.NivelAplicado.Should().Be("OT");
        docA.OrdenResuelto.Should().Be((short)5);
    }

    // ---------- HU #10198: obligatoriedad por OT (3 estados) ----------

    [Fact]
    public async Task Resolve_OtRequirementOverrides_FlipObligatorio_AndExcludeNotApplicable()
    {
        var db = NewDbName();
        await SeedRequirementsAsync(db);
        await using (var seed = NewContext(db))
        {
            // DocA (default obligatorio) → OPTIONAL en este OT.
            seed.DocumentRequirementOverrides.Add(RequirementOverride(DocA, "OPTIONAL"));
            // DocB (default opcional) → REQUIRED en este OT.
            seed.DocumentRequirementOverrides.Add(RequirementOverride(DocB, "REQUIRED"));
            // DocC → NOT_APPLICABLE → se oculta de la matriz de este OT.
            seed.DocumentRequirementOverrides.Add(RequirementOverride(DocC, "NOT_APPLICABLE"));
            await seed.SaveChangesAsync();
        }

        await using var ctx = NewContext(db);
        var result = await Handler(ctx).HandleAsync(new GetResolvedDocumentMatrixQuery
        {
            ProcedureTypeId = ProcedureTypeId,
            TransitOfficeId = TransitOfficeId,
        });

        result.Outcome.Should().Be(GetResolvedDocumentMatrixOutcome.Resolved);
        // DocC excluido → solo DocA y DocB.
        result.Data.Should().HaveCount(2);
        result.Data.Should().NotContain(d => d.DocumentTypeId == DocC);
        result.Data.Single(d => d.DocumentTypeId == DocA).Obligatorio.Should().BeFalse();
        result.Data.Single(d => d.DocumentTypeId == DocB).Obligatorio.Should().BeTrue();
    }

    [Fact]
    public async Task Resolve_WithoutOt_IgnoresRequirementOverrides()
    {
        var db = NewDbName();
        await SeedRequirementsAsync(db);
        await using (var seed = NewContext(db))
        {
            // Aunque exista un NOT_APPLICABLE, sin OT en la consulta no se aplica.
            seed.DocumentRequirementOverrides.Add(RequirementOverride(DocC, "NOT_APPLICABLE"));
            await seed.SaveChangesAsync();
        }

        await using var ctx = NewContext(db);
        var result = await Handler(ctx).HandleAsync(new GetResolvedDocumentMatrixQuery
        {
            ProcedureTypeId = ProcedureTypeId,
        });

        // Sin OT → los 3 documentos con su obligatoriedad default.
        result.Data.Should().HaveCount(3);
        result.Data.Single(d => d.DocumentTypeId == DocC).Obligatorio.Should().BeTrue();
    }

    // ---------- Helpers ----------

    private static Flit.Infrastructure.Persistence.Entities.Tramites.DocumentRequirementOverride RequirementOverride(
        Guid documentTypeId, string state) =>
        new()
        {
            Id = Guid.NewGuid(),
            ProcedureTypeId = ProcedureTypeId,
            DocumentTypeId = documentTypeId,
            TransitOfficeId = TransitOfficeId,
            RequirementState = state,
            CreatedAt = DateTimeOffset.UtcNow,
        };

    private static GetResolvedDocumentMatrixHandler Handler(FlitDbContext ctx) =>
        new(new ProcedureTypeCatalog(ctx), new ResolvedDocumentMatrixResolver(ctx));

    private static DocumentOrderOverride Override(Guid documentTypeId, string scope, Guid scopeRefId, short orden) =>
        new()
        {
            Id = Guid.NewGuid(),
            ProcedureTypeId = ProcedureTypeId,
            DocumentTypeId = documentTypeId,
            ScopeType = scope,
            ScopeRefId = scopeRefId,
            SortOrder = orden,
            CreatedAt = DateTimeOffset.UtcNow,
        };

    private static string NewDbName() => $"flit-matrix-{Guid.NewGuid()}";

    private static FlitDbContext NewContext(string dbName) =>
        new(new DbContextOptionsBuilder<FlitDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options);

    /// <summary>Siembra trámite y 3 documentos asociados con default_sort_order 10/20/30.</summary>
    private static async Task SeedRequirementsAsync(string dbName)
    {
        await using var ctx = NewContext(dbName);
        ctx.ProcedureTypes.Add(new ProcedureType { Id = ProcedureTypeId, Code = "TRASPASO", Name = "Traspaso", IsActive = true });
        ctx.DocumentTypes.AddRange(
            new DocumentType { Id = DocA, Code = "DOCA", Name = "Documento A", IsActive = true, CreatedAt = DateTimeOffset.UtcNow },
            new DocumentType { Id = DocB, Code = "DOCB", Name = "Documento B", IsActive = true, CreatedAt = DateTimeOffset.UtcNow },
            new DocumentType { Id = DocC, Code = "DOCC", Name = "Documento C", IsActive = true, CreatedAt = DateTimeOffset.UtcNow });
        ctx.ProcedureDocumentRequirements.AddRange(
            new ProcedureDocumentRequirement { Id = Guid.NewGuid(), ProcedureTypeId = ProcedureTypeId, DocumentTypeId = DocA, IsMandatory = true, DefaultSortOrder = 10, CreatedAt = DateTimeOffset.UtcNow },
            new ProcedureDocumentRequirement { Id = Guid.NewGuid(), ProcedureTypeId = ProcedureTypeId, DocumentTypeId = DocB, IsMandatory = false, DefaultSortOrder = 20, CreatedAt = DateTimeOffset.UtcNow },
            new ProcedureDocumentRequirement { Id = Guid.NewGuid(), ProcedureTypeId = ProcedureTypeId, DocumentTypeId = DocC, IsMandatory = true, DefaultSortOrder = 30, CreatedAt = DateTimeOffset.UtcNow });
        await ctx.SaveChangesAsync();
    }
}
