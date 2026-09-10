using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Admin;
using Flit.Infrastructure.Persistence.Entities.Tramites;
using Flit.Infrastructure.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Flit.Infrastructure.Tests.Services;

/// <summary>
/// HU #11184 — el orden que el OT configuró para un tipo de trámite. La lista vacía es la señal de
/// respaldo: sin configuración del organismo, el consolidado conserva su orden por modalidad.
/// Ejercita la implementación EF real sobre InMemory.
/// </summary>
public sealed class OtConfiguredDocumentOrderProviderTests
{
    private static readonly Guid OtTenantId = Guid.Parse("aaaaaaaa-1111-4000-8000-000000000001");
    private static readonly Guid OtraOtTenantId = Guid.Parse("aaaaaaaa-2222-4000-8000-000000000002");
    private static readonly Guid TransitOfficeId = Guid.Parse("bbbbbbbb-1111-4000-8000-000000000001");
    private static readonly Guid OtraTransitOfficeId = Guid.Parse("bbbbbbbb-2222-4000-8000-000000000002");
    private static readonly Guid ProcedureTypeId = Guid.Parse("cccccccc-1111-4000-8000-000000000001");
    private static readonly Guid DocFur = Guid.Parse("dddddddd-1111-4000-8000-000000000001");
    private static readonly Guid DocCompraventa = Guid.Parse("dddddddd-2222-4000-8000-000000000002");
    private static readonly Guid DocSoat = Guid.Parse("dddddddd-3333-4000-8000-000000000003");

    [Fact]
    public async Task ConPrelacionConfigurada_DevuelveLosCodigosEnEseOrden()
    {
        var db = NewDbName();
        await SeedAsync(db);
        await using (var seed = NewContext(db))
        {
            seed.OtDocumentPrecedences.Add(Precedence(OtTenantId, DocCompraventa, 1));
            seed.OtDocumentPrecedences.Add(Precedence(OtTenantId, DocSoat, 2));
            seed.OtDocumentPrecedences.Add(Precedence(OtTenantId, DocFur, 3));
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var ctx = NewContext(db);
        var orden = await new OtConfiguredDocumentOrderProvider(ctx)
            .GetConfiguredOrderAsync(ProcedureTypeId, TransitOfficeId, TestContext.Current.CancellationToken);

        orden.Should().ContainInOrder("compraventa", "soat", "fur");
    }

    [Fact]
    public async Task SinPrelacionConfigurada_DevuelveVacio()
    {
        var db = NewDbName();
        await SeedAsync(db);

        await using var ctx = NewContext(db);
        var orden = await new OtConfiguredDocumentOrderProvider(ctx)
            .GetConfiguredOrderAsync(ProcedureTypeId, TransitOfficeId, TestContext.Current.CancellationToken);

        orden.Should().BeEmpty();
    }

    [Fact]
    public async Task SinOrganismoAsignado_DevuelveVacio()
    {
        var db = NewDbName();
        await SeedAsync(db);
        await using (var seed = NewContext(db))
        {
            seed.OtDocumentPrecedences.Add(Precedence(OtTenantId, DocFur, 1));
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var ctx = NewContext(db);
        var provider = new OtConfiguredDocumentOrderProvider(ctx);

        (await provider.GetConfiguredOrderAsync(ProcedureTypeId, null, TestContext.Current.CancellationToken))
            .Should().BeEmpty();
        (await provider.GetConfiguredOrderAsync(ProcedureTypeId, Guid.Empty, TestContext.Current.CancellationToken))
            .Should().BeEmpty();
    }

    [Fact]
    public async Task NoDevuelveLaPrelacionDeOtroOrganismo()
    {
        var db = NewDbName();
        await SeedAsync(db);
        await using (var seed = NewContext(db))
        {
            seed.OtDocumentPrecedences.Add(Precedence(OtraOtTenantId, DocSoat, 1));
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var ctx = NewContext(db);
        var orden = await new OtConfiguredDocumentOrderProvider(ctx)
            .GetConfiguredOrderAsync(ProcedureTypeId, TransitOfficeId, TestContext.Current.CancellationToken);

        orden.Should().BeEmpty();
    }

    private static OtDocumentPrecedenceEntity Precedence(Guid tenantId, Guid documentTypeId, short sortOrder) =>
        new()
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ProcedureTypeId = ProcedureTypeId,
            DocumentTypeId = documentTypeId,
            SortOrder = sortOrder,
            CreatedAt = DateTimeOffset.UtcNow,
        };

    private static async Task SeedAsync(string db)
    {
        await using var seed = NewContext(db);
        seed.TransitOfficeProfiles.AddRange(
            new TransitOfficeProfile
            {
                Id = Guid.NewGuid(), TenantId = OtTenantId, TransitOfficeId = TransitOfficeId,
                CreatedAt = DateTimeOffset.UtcNow,
            },
            new TransitOfficeProfile
            {
                Id = Guid.NewGuid(), TenantId = OtraOtTenantId, TransitOfficeId = OtraTransitOfficeId,
                CreatedAt = DateTimeOffset.UtcNow,
            });
        seed.DocumentTypes.AddRange(
            new DocumentType { Id = DocFur, Code = "fur", Name = "FUR", IsActive = true, CreatedAt = DateTimeOffset.UtcNow },
            new DocumentType { Id = DocCompraventa, Code = "compraventa", Name = "Compraventa", IsActive = true, CreatedAt = DateTimeOffset.UtcNow },
            new DocumentType { Id = DocSoat, Code = "soat", Name = "SOAT", IsActive = true, CreatedAt = DateTimeOffset.UtcNow });
        await seed.SaveChangesAsync();
    }

    private static string NewDbName() => $"flit-ot-order-{Guid.NewGuid()}";

    private static FlitDbContext NewContext(string dbName) =>
        new(new DbContextOptionsBuilder<FlitDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options);
}
