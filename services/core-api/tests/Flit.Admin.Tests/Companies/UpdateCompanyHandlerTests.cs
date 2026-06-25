using Flit.Admin.Application.Companies.UpdateCompany;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Identity;
using Flit.Infrastructure.Persistence.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Flit.Admin.Tests.Companies;

/// <summary>
/// Tests de la edición de compañías (botón "Editar", #10118). Ejercitan el handler real
/// sobre el repositorio EF real con proveedor InMemory: validación 422 sin persistir,
/// 404 cuando el tenant no existe, inmutabilidad del code y persistencia real de los
/// campos editables (razón social, NIT, tipo, estado).
/// </summary>
public sealed class UpdateCompanyHandlerTests
{
    private static readonly Guid Operator = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public async Task ValidInput_UpdatesEditableFields_AndKeepsCodeImmutable()
    {
        var db = NewDbName();
        var tenantId = await SeedTenantAsync(db);

        await using (var act = NewContext(db))
        {
            var handler = new UpdateCompanyHandler(new CompanyWriteRepository(act));
            var result = await handler.HandleAsync(new UpdateCompanyCommand
            {
                TenantId = tenantId,
                ChangedBy = Operator,
                Request = new UpdateCompanyRequest(
                    RazonSocial: "Renting Andino S.A.S. (editada)",
                    Nit: "900999999-9",
                    TenantType: "CONCESIONARIO",
                    EstadoActivo: false),
            }, TestContext.Current.CancellationToken);

            result.Outcome.Should().Be(UpdateCompanyOutcome.Updated);
            result.Errors.Should().BeEmpty();
            result.Company.Should().NotBeNull();
            result.Company!.RazonSocial.Should().Be("Renting Andino S.A.S. (editada)");
            result.Company.Nit.Should().Be("900999999-9");
            result.Company.TenantType.Should().Be("CONCESIONARIO");
            result.Company.EstadoActivo.Should().BeFalse();
            // El code es inmutable: la proyección sigue devolviendo el original.
            result.Company.Code.Should().Be("RENTANDINO");
        }

        await using var verify = NewContext(db);
        var tenant = await verify.Tenants.SingleAsync(t => t.Id == tenantId, cancellationToken: TestContext.Current.CancellationToken);
        tenant.LegalName.Should().Be("Renting Andino S.A.S. (editada)");
        tenant.TaxId.Should().Be("900999999-9");
        tenant.TenantType.Should().Be("CONCESIONARIO");
        tenant.IsActive.Should().BeFalse();
        tenant.Code.Should().Be("RENTANDINO");
        tenant.UpdatedBy.Should().Be(Operator);
    }

    [Fact]
    public async Task TenantType_IsNormalizedToUpper()
    {
        var db = NewDbName();
        var tenantId = await SeedTenantAsync(db);

        await using var ctx = NewContext(db);
        var handler = new UpdateCompanyHandler(new CompanyWriteRepository(ctx));

        var result = await handler.HandleAsync(new UpdateCompanyCommand
        {
            TenantId = tenantId,
            Request = new UpdateCompanyRequest("Renting Andino S.A.S.", "900123456-1", "concesionario", true),
        }, TestContext.Current.CancellationToken);

        result.Outcome.Should().Be(UpdateCompanyOutcome.Updated);
        (await ctx.Tenants.SingleAsync(t => t.Id == tenantId, cancellationToken: TestContext.Current.CancellationToken))
            .TenantType.Should().Be("CONCESIONARIO");
    }

    [Fact]
    public async Task UnknownTenant_Returns404()
    {
        await using var ctx = NewContext(NewDbName());
        var handler = new UpdateCompanyHandler(new CompanyWriteRepository(ctx));

        var result = await handler.HandleAsync(new UpdateCompanyCommand
        {
            TenantId = Guid.NewGuid(),
            Request = new UpdateCompanyRequest("Inexistente", "900000000-0", "FLIT", true),
        }, TestContext.Current.CancellationToken);

        result.Outcome.Should().Be(UpdateCompanyOutcome.NotFound);
        result.Company.Should().BeNull();
    }

    [Fact]
    public async Task MissingRequiredFields_Return422_AndPersistNothing()
    {
        var db = NewDbName();
        var tenantId = await SeedTenantAsync(db);

        await using (var act = NewContext(db))
        {
            var handler = new UpdateCompanyHandler(new CompanyWriteRepository(act));
            var result = await handler.HandleAsync(new UpdateCompanyCommand
            {
                TenantId = tenantId,
                Request = new UpdateCompanyRequest(RazonSocial: "  ", Nit: "", TenantType: null, EstadoActivo: null),
            }, TestContext.Current.CancellationToken);

            result.Outcome.Should().Be(UpdateCompanyOutcome.Invalid);
            result.Company.Should().BeNull();
            result.Errors.Select(e => e.Field).Should().Contain(["razonSocial", "nit", "tenantType"]);
        }

        // Nada se persistió: el tenant conserva sus valores originales.
        await using var verify = NewContext(db);
        var tenant = await verify.Tenants.SingleAsync(t => t.Id == tenantId, cancellationToken: TestContext.Current.CancellationToken);
        tenant.LegalName.Should().Be("Renting Andino S.A.S.");
        tenant.TaxId.Should().Be("900123456-1");
    }

    [Fact]
    public async Task InvalidTenantType_Returns422()
    {
        var db = NewDbName();
        var tenantId = await SeedTenantAsync(db);

        await using var ctx = NewContext(db);
        var handler = new UpdateCompanyHandler(new CompanyWriteRepository(ctx));

        var result = await handler.HandleAsync(new UpdateCompanyCommand
        {
            TenantId = tenantId,
            Request = new UpdateCompanyRequest("Renting Andino", "900123456-1", "BANCO", true),
        }, TestContext.Current.CancellationToken);

        result.Outcome.Should().Be(UpdateCompanyOutcome.Invalid);
        result.Errors.Should().ContainSingle(e => e.Field == "tenantType");
    }

    [Fact]
    public async Task RazonSocialTooLong_Returns422()
    {
        var db = NewDbName();
        var tenantId = await SeedTenantAsync(db);

        await using var ctx = NewContext(db);
        var handler = new UpdateCompanyHandler(new CompanyWriteRepository(ctx));

        var result = await handler.HandleAsync(new UpdateCompanyCommand
        {
            TenantId = tenantId,
            Request = new UpdateCompanyRequest(new string('X', 256), "900123456-1", "RENTING", true),
        }, TestContext.Current.CancellationToken);

        result.Outcome.Should().Be(UpdateCompanyOutcome.Invalid);
        result.Errors.Should().ContainSingle(e => e.Field == "razonSocial");
    }

    [Fact]
    public async Task LegacyTenantType_OutsideB2BCatalog_IsPreserved_WhenUnchanged()
    {
        // Un tenant con tipo heredado ('standard') aparece en el listado; editar solo la
        // razón social no debe bloquearse ni mutar el tipo a un valor del catálogo B2B.
        var db = NewDbName();
        var tenantId = await SeedTenantAsync(db, tenantType: "standard");

        await using (var act = NewContext(db))
        {
            var handler = new UpdateCompanyHandler(new CompanyWriteRepository(act));
            var result = await handler.HandleAsync(new UpdateCompanyCommand
            {
                TenantId = tenantId,
                ChangedBy = Operator,
                Request = new UpdateCompanyRequest(
                    RazonSocial: "Empresa Demo FLIT (editada)",
                    Nit: "900123456-1",
                    TenantType: "standard",
                    EstadoActivo: true),
            }, TestContext.Current.CancellationToken);

            result.Outcome.Should().Be(UpdateCompanyOutcome.Updated);
            result.Company!.RazonSocial.Should().Be("Empresa Demo FLIT (editada)");
            // El tipo heredado se conserva EXACTAMENTE (sin normalizar a mayúsculas).
            result.Company.TenantType.Should().Be("standard");
        }

        await using var verify = NewContext(db);
        (await verify.Tenants.SingleAsync(t => t.Id == tenantId, cancellationToken: TestContext.Current.CancellationToken))
            .TenantType.Should().Be("standard");
    }

    [Fact]
    public async Task LegacyTenantType_CanBeChangedToB2BCatalogValue()
    {
        // El admin sí puede reclasificar un tenant heredado a un tipo B2B válido.
        var db = NewDbName();
        var tenantId = await SeedTenantAsync(db, tenantType: "standard");

        await using var ctx = NewContext(db);
        var handler = new UpdateCompanyHandler(new CompanyWriteRepository(ctx));

        var result = await handler.HandleAsync(new UpdateCompanyCommand
        {
            TenantId = tenantId,
            Request = new UpdateCompanyRequest("Empresa Demo FLIT", "900123456-1", "FLIT", true),
        }, TestContext.Current.CancellationToken);

        result.Outcome.Should().Be(UpdateCompanyOutcome.Updated);
        (await ctx.Tenants.SingleAsync(t => t.Id == tenantId, cancellationToken: TestContext.Current.CancellationToken))
            .TenantType.Should().Be("FLIT");
    }

    [Fact]
    public async Task StaleRowVersion_Returns409Conflict_AndPersistsNothing()
    {
        // El cliente abrió la edición con una versión que ya quedó vieja (otra persona
        // guardó antes) → 409, sin tocar la BD.
        var db = NewDbName();
        var tenantId = await SeedTenantAsync(db, rowVersion: 7);

        await using (var act = NewContext(db))
        {
            var handler = new UpdateCompanyHandler(new CompanyWriteRepository(act));
            var result = await handler.HandleAsync(new UpdateCompanyCommand
            {
                TenantId = tenantId,
                Request = new UpdateCompanyRequest(
                    "Renting Andino S.A.S. (editada)", "900123456-1", "RENTING", true, RowVersion: 3),
            }, TestContext.Current.CancellationToken);

            result.Outcome.Should().Be(UpdateCompanyOutcome.Conflict);
            result.Company.Should().BeNull();
        }

        await using var verify = NewContext(db);
        (await verify.Tenants.SingleAsync(t => t.Id == tenantId, cancellationToken: TestContext.Current.CancellationToken))
            .LegalName.Should().Be("Renting Andino S.A.S.");
    }

    [Fact]
    public async Task MatchingRowVersion_AppliesUpdate()
    {
        var db = NewDbName();
        var tenantId = await SeedTenantAsync(db, rowVersion: 7);

        await using var ctx = NewContext(db);
        var handler = new UpdateCompanyHandler(new CompanyWriteRepository(ctx));

        var result = await handler.HandleAsync(new UpdateCompanyCommand
        {
            TenantId = tenantId,
            Request = new UpdateCompanyRequest("Renting Andino S.A.S. (ed)", "900123456-1", "RENTING", true, RowVersion: 7),
        }, TestContext.Current.CancellationToken);

        result.Outcome.Should().Be(UpdateCompanyOutcome.Updated);
        result.Company!.RazonSocial.Should().Be("Renting Andino S.A.S. (ed)");
    }

    // ---------- Helpers ----------

    private static async Task<Guid> SeedTenantAsync(string db, string tenantType = "RENTING", long rowVersion = 0)
    {
        var tenantId = Guid.NewGuid();
        await using var seed = NewContext(db);
        seed.Tenants.Add(new Tenant
        {
            Id = tenantId,
            Code = "RENTANDINO",
            LegalName = "Renting Andino S.A.S.",
            TaxId = "900123456-1",
            TenantType = tenantType,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
            RowVersion = rowVersion,
        });
        await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        return tenantId;
    }

    private static string NewDbName() => $"flit-update-company-{Guid.NewGuid()}";

    private static FlitDbContext NewContext(string dbName) =>
        new(new DbContextOptionsBuilder<FlitDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options);
}
