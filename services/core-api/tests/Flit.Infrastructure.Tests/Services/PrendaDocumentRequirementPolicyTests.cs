using Flit.Admin.Domain.DocumentRequirementOverrides;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Tramites;
using Flit.Infrastructure.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Flit.Infrastructure.Tests.Services;

/// <summary>
/// CF-06 (HU #10881) — override OT del documento de prenda: AC1 (bloquea cuando el override
/// REQUIRED del OT ya estaba activo) y AC2 (SNAPSHOT: un override activado DESPUÉS de crear el
/// trámite NO lo afecta). Ejercita la implementación EF real sobre InMemory, igual patrón que
/// <c>ResolvedDocumentMatrixHandlerTests</c>.
/// </summary>
public sealed class PrendaDocumentRequirementPolicyTests
{
    private static readonly Guid ProcedureTypeId = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private static readonly Guid TransitOfficeId = Guid.Parse("aaaaaaaa-0002-4000-8000-000000000002");
    private static readonly Guid PrendaDocumentTypeId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
    private static readonly Guid OtherDocumentTypeId = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");

    [Fact]
    public async Task Ac1_OverrideRequiredYaActivo_DevuelveTrue()
    {
        var db = NewDbName();
        await SeedCatalogAsync(db);
        var createdAt = DateTimeOffset.UtcNow;
        await using (var seed = NewContext(db))
        {
            seed.DocumentRequirementOverrides.Add(Override(
                DocumentRequirementState.Required, createdAt: createdAt.AddDays(-1)));
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var ctx = NewContext(db);
        var required = await new PrendaDocumentRequirementPolicy(ctx).IsRequiredAsync(
            ProcedureTypeId, TransitOfficeId, createdAt, TestContext.Current.CancellationToken);

        required.Should().BeTrue();
    }

    [Fact]
    public async Task Ac2_OverrideActivadoDespuesDeCrearElTramite_DevuelveFalse()
    {
        // Snapshot — el trámite se creó ANTES de que el admin activara el override: el trámite en
        // curso no debe verse afectado.
        var db = NewDbName();
        await SeedCatalogAsync(db);
        var procedureCreatedAt = DateTimeOffset.UtcNow;
        var overrideCreatedAt = procedureCreatedAt.AddMinutes(5); // activado DESPUÉS de crear el trámite.
        await using (var seed = NewContext(db))
        {
            seed.DocumentRequirementOverrides.Add(Override(
                DocumentRequirementState.Required, createdAt: overrideCreatedAt));
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var ctx = NewContext(db);
        var required = await new PrendaDocumentRequirementPolicy(ctx).IsRequiredAsync(
            ProcedureTypeId, TransitOfficeId, procedureCreatedAt, TestContext.Current.CancellationToken);

        required.Should().BeFalse();
    }

    [Fact]
    public async Task SinOverride_DevuelveFalse()
    {
        var db = NewDbName();
        await SeedCatalogAsync(db);

        await using var ctx = NewContext(db);
        var required = await new PrendaDocumentRequirementPolicy(ctx).IsRequiredAsync(
            ProcedureTypeId, TransitOfficeId, DateTimeOffset.UtcNow, TestContext.Current.CancellationToken);

        required.Should().BeFalse();
    }

    [Fact]
    public async Task OverrideOptional_NoActivaElRequisito()
    {
        var db = NewDbName();
        await SeedCatalogAsync(db);
        var createdAt = DateTimeOffset.UtcNow;
        await using (var seed = NewContext(db))
        {
            seed.DocumentRequirementOverrides.Add(Override(
                DocumentRequirementState.Optional, createdAt: createdAt.AddDays(-1)));
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var ctx = NewContext(db);
        var required = await new PrendaDocumentRequirementPolicy(ctx).IsRequiredAsync(
            ProcedureTypeId, TransitOfficeId, createdAt, TestContext.Current.CancellationToken);

        required.Should().BeFalse();
    }

    [Fact]
    public async Task OverrideDeOtroDocumento_NoActivaElRequisitoDePrenda()
    {
        var db = NewDbName();
        await SeedCatalogAsync(db);
        var createdAt = DateTimeOffset.UtcNow;
        await using (var seed = NewContext(db))
        {
            seed.DocumentRequirementOverrides.Add(new DocumentRequirementOverride
            {
                Id = Guid.NewGuid(),
                ProcedureTypeId = ProcedureTypeId,
                DocumentTypeId = OtherDocumentTypeId,
                TransitOfficeId = TransitOfficeId,
                RequirementState = DocumentRequirementState.Required,
                CreatedAt = createdAt.AddDays(-1),
            });
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var ctx = NewContext(db);
        var required = await new PrendaDocumentRequirementPolicy(ctx).IsRequiredAsync(
            ProcedureTypeId, TransitOfficeId, createdAt, TestContext.Current.CancellationToken);

        required.Should().BeFalse();
    }

    [Fact]
    public async Task SinTransitOfficeId_DevuelveFalseSinConsultar()
    {
        var db = NewDbName();
        await SeedCatalogAsync(db);
        var createdAt = DateTimeOffset.UtcNow;
        await using (var seed = NewContext(db))
        {
            seed.DocumentRequirementOverrides.Add(Override(
                DocumentRequirementState.Required, createdAt: createdAt.AddDays(-1)));
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var ctx = NewContext(db);
        var required = await new PrendaDocumentRequirementPolicy(ctx).IsRequiredAsync(
            ProcedureTypeId, transitOfficeId: null, createdAt, TestContext.Current.CancellationToken);

        required.Should().BeFalse();
    }

    // ---------- Helpers ----------

    private static DocumentRequirementOverride Override(string state, DateTimeOffset createdAt) => new()
    {
        Id = Guid.NewGuid(),
        ProcedureTypeId = ProcedureTypeId,
        DocumentTypeId = PrendaDocumentTypeId,
        TransitOfficeId = TransitOfficeId,
        RequirementState = state,
        CreatedAt = createdAt,
    };

    private static string NewDbName() => $"flit-prenda-ot-{Guid.NewGuid()}";

    private static FlitDbContext NewContext(string dbName) =>
        new(new DbContextOptionsBuilder<FlitDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options);

    private static async Task SeedCatalogAsync(string dbName)
    {
        await using var ctx = NewContext(dbName);
        ctx.DocumentTypes.AddRange(
            new DocumentType
            {
                Id = PrendaDocumentTypeId,
                Code = PrendaDocumentRequirementPolicy.PrendaDocumentTypeCode,
                Name = "Inscripción / Registro de Prenda",
                IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow,
            },
            new DocumentType
            {
                Id = OtherDocumentTypeId,
                Code = "soat",
                Name = "SOAT",
                IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow,
            });
        await ctx.SaveChangesAsync();
    }
}
