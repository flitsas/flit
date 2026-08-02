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
/// Matriz documental resuelta (HU #10196; RF22) — precedencia <b>OT &gt; Default</b> (el OT es
/// el único nivel que reordena), AC4 (sin overrides → orden Default) y RF22 (las filas legadas
/// con <c>scope_type='CLIENTE'</c> se ignoran). Ejercita el resolutor EF real sobre InMemory.
/// </summary>
public sealed class ResolvedDocumentMatrixHandlerTests
{
    private static readonly Guid ProcedureTypeId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid DocA = Guid.Parse("aaaa1111-1111-1111-1111-111111111111");
    private static readonly Guid DocB = Guid.Parse("bbbb2222-2222-2222-2222-222222222222");
    private static readonly Guid DocC = Guid.Parse("cccc3333-3333-3333-3333-333333333333");
    private static readonly Guid TransitOfficeId = Guid.Parse("aaaaaaaa-0001-4000-8000-000000000001");
    private static readonly Guid ClienteId = Guid.Parse("77777777-7777-7777-7777-777777777777");

    // ---------- RF22: precedencia OT > Default; las filas CLIENTE legadas se ignoran ----------

    [Fact]
    public async Task RF22_Resolve_AppliesPrecedence_OtOverDefault_IgnoresLegacyCliente()
    {
        var db = NewDbName();
        await SeedRequirementsAsync(db);
        await using (var seed = NewContext(db))
        {
            // DocA: override OT (50) + una fila legada CLIENTE (1) → gana OT; CLIENTE se ignora (RF22).
            seed.DocumentOrderOverrides.Add(Override(DocA, "OT", TransitOfficeId, 50));
            seed.DocumentOrderOverrides.Add(Override(DocA, "CLIENTE", ClienteId, 1));
            // DocB: solo override OT → gana OT.
            seed.DocumentOrderOverrides.Add(Override(DocB, "OT", TransitOfficeId, 2));
            // DocC: sin overrides → Default (orden 30 sembrado).
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var ctx = NewContext(db);
        var result = await Handler(ctx).HandleAsync(new GetResolvedDocumentMatrixQuery
        {
            ProcedureTypeId = ProcedureTypeId,
            TransitOfficeId = TransitOfficeId,
        }, TestContext.Current.CancellationToken);

        result.Outcome.Should().Be(GetResolvedDocumentMatrixOutcome.Resolved);
        result.Data.Should().HaveCount(3);

        // DocA: la fila CLIENTE se ignora → gana OT (50), NO el 1 del CLIENTE.
        var docA = result.Data.Single(d => d.DocumentTypeId == DocA);
        docA.OrdenResuelto.Should().Be((short)50);
        docA.NivelAplicado.Should().Be("OT");

        var docB = result.Data.Single(d => d.DocumentTypeId == DocB);
        docB.OrdenResuelto.Should().Be((short)2);
        docB.NivelAplicado.Should().Be("OT");

        var docC = result.Data.Single(d => d.DocumentTypeId == DocC);
        docC.OrdenResuelto.Should().Be((short)30);
        docC.NivelAplicado.Should().Be("DEFAULT");

        // Ningún documento queda en nivel CLIENTE (RF22).
        result.Data.Should().NotContain(d => d.NivelAplicado == "CLIENTE");
        // Orden global por orden resuelto asc: DocB(2), DocC(30), DocA(50).
        result.Data.Select(d => d.DocumentTypeId).Should().ContainInOrder(DocB, DocC, DocA);
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
        }, TestContext.Current.CancellationToken);

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
        }, TestContext.Current.CancellationToken);

        result.Outcome.Should().Be(GetResolvedDocumentMatrixOutcome.ProcedureTypeNotFound);
    }

    [Fact]
    public async Task Resolve_ProcedureWithoutRequirements_Returns200WithEmptyData()
    {
        var db = NewDbName();
        await using (var seed = NewContext(db))
        {
            seed.ProcedureTypes.Add(new ProcedureType { Id = ProcedureTypeId, Code = "X", Name = "X", IsActive = true });
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var ctx = NewContext(db);
        var result = await Handler(ctx).HandleAsync(new GetResolvedDocumentMatrixQuery
        {
            ProcedureTypeId = ProcedureTypeId,
        }, TestContext.Current.CancellationToken);

        result.Outcome.Should().Be(GetResolvedDocumentMatrixOutcome.Resolved);
        result.Data.Should().BeEmpty();
    }

    // ---------- RF22: una fila legada CLIENTE sin OT NO reordena → cae a Default ----------

    [Fact]
    public async Task RF22_LegacyClienteOverride_WithoutOt_FallsBackToDefault()
    {
        var db = NewDbName();
        await SeedRequirementsAsync(db);
        await using (var seed = NewContext(db))
        {
            // DocA: SOLO una fila legada CLIENTE (sin override OT). Tras RF22 se ignora ⇒ Default.
            seed.DocumentOrderOverrides.Add(Override(DocA, "CLIENTE", ClienteId, 1));
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var ctx = NewContext(db);
        var resolved = await Handler(ctx).HandleAsync(new GetResolvedDocumentMatrixQuery
        {
            ProcedureTypeId = ProcedureTypeId,
            TransitOfficeId = TransitOfficeId,
        }, TestContext.Current.CancellationToken);

        var docA = resolved.Data.Single(d => d.DocumentTypeId == DocA);
        docA.NivelAplicado.Should().Be("DEFAULT");
        docA.OrdenResuelto.Should().Be((short)10); // default_sort_order sembrado para DocA.
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
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var ctx = NewContext(db);
        var result = await Handler(ctx).HandleAsync(new GetResolvedDocumentMatrixQuery
        {
            ProcedureTypeId = ProcedureTypeId,
            TransitOfficeId = TransitOfficeId,
        }, TestContext.Current.CancellationToken);

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
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var ctx = NewContext(db);
        var result = await Handler(ctx).HandleAsync(new GetResolvedDocumentMatrixQuery
        {
            ProcedureTypeId = ProcedureTypeId,
        }, TestContext.Current.CancellationToken);

        // Sin OT → los 3 documentos con su obligatoriedad default.
        result.Data.Should().HaveCount(3);
        result.Data.Single(d => d.DocumentTypeId == DocC).Obligatorio.Should().BeTrue();
    }

    // ---------- HU #11181 AC3/AC4: los documentos generados NO entran al checklist ----------

    [Fact]
    public async Task HU11181_GeneratedDocumentTypes_DoNotLeakIntoChecklistMatrix()
    {
        // La marca `is_system_generated` sirve para ORDENAR el expediente, no para pedir documentos:
        // el checklist del gestor sigue saliendo de procedure_document_requirements. Si el resolutor
        // empezara a unir los generados, el gestor vería documentos nuevos que nadie debe adjuntar.
        var db = NewDbName();
        await SeedRequirementsAsync(db);
        await using (var seed = NewContext(db))
        {
            seed.DocumentTypes.Add(new DocumentType
            {
                Id = Guid.Parse("dddd4444-4444-4444-4444-444444444444"),
                Code = "fur",
                Name = "Formulario Único de Registro (FUR)",
                IsActive = true,
                IsSystemGenerated = true,
                GeneratedSortOrder = 1,
                CreatedAt = DateTimeOffset.UtcNow,
            });
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var ctx = NewContext(db);
        var result = await Handler(ctx).HandleAsync(new GetResolvedDocumentMatrixQuery
        {
            ProcedureTypeId = ProcedureTypeId,
            TransitOfficeId = TransitOfficeId,
        }, TestContext.Current.CancellationToken);

        // Los mismos 3 documentos de la matriz base, con su obligatoriedad intacta.
        result.Data.Should().HaveCount(3);
        result.Data.Should().NotContain(d => d.Codigo == "fur");
        result.Data.Single(d => d.DocumentTypeId == DocA).Obligatorio.Should().BeTrue();
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
