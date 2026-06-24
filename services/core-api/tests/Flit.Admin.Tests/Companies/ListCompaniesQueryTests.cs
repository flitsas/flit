using Flit.Admin.Application.Companies.ListCompanies;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Identity;
using Flit.Infrastructure.Persistence.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Flit.Admin.Tests.Companies;

/// <summary>
/// Tests del listado paginado de compañías (HU #10189) — AC1 (paginación + orden)
/// y AC2 (filtros opcionales e independientes). Ejercitan el handler real sobre el
/// repositorio EF real con proveedor InMemory.
/// </summary>
public sealed class ListCompaniesQueryTests
{
    // ---------- AC1: Listado paginado (RF02) ----------

    [Fact]
    public async Task AC1_ReturnsPagedShape_OrderedByCreatedAtDescending()
    {
        await using var ctx = NewContext();
        Seed(ctx,
            Company("900100001", "Alfa SAS", active: true, created: "2026-01-10"),
            Company("900100002", "Beta SAS", active: true, created: "2026-03-15"),
            Company("900100003", "Gamma SAS", active: false, created: "2026-02-20"));

        var result = await Handler(ctx).HandleAsync(new ListCompaniesQuery { Page = 1, PageSize = 20 }, TestContext.Current.CancellationToken);

        result.Page.Should().Be(1);
        result.PageSize.Should().Be(20);
        result.TotalCount.Should().Be(3);
        result.Data.Should().HaveCount(3);
        result.Data.Select(c => c.RazonSocial)
            .Should().ContainInOrder("Beta SAS", "Gamma SAS", "Alfa SAS");
    }

    [Fact]
    public async Task AC1_AppliesOffsetPagination_PerPage()
    {
        await using var ctx = NewContext();
        Seed(ctx,
            Company("900200001", "Uno", active: true, created: "2026-01-01"),
            Company("900200002", "Dos", active: true, created: "2026-01-02"),
            Company("900200003", "Tres", active: true, created: "2026-01-03"));

        var page1 = await Handler(ctx).HandleAsync(new ListCompaniesQuery { Page = 1, PageSize = 2 }, TestContext.Current.CancellationToken);
        var page2 = await Handler(ctx).HandleAsync(new ListCompaniesQuery { Page = 2, PageSize = 2 }, TestContext.Current.CancellationToken);

        page1.TotalCount.Should().Be(3);
        page1.Data.Select(c => c.RazonSocial).Should().ContainInOrder("Tres", "Dos");
        page2.TotalCount.Should().Be(3);
        page2.Data.Select(c => c.RazonSocial).Should().ContainSingle().Which.Should().Be("Uno");
    }

    [Fact]
    public async Task AC1_NormalizesInvalidPaging_ToDefaults()
    {
        await using var ctx = NewContext();
        Seed(ctx, Company("900300001", "Solo", active: true, created: "2026-01-01"));

        var result = await Handler(ctx).HandleAsync(new ListCompaniesQuery { Page = 0, PageSize = null }, TestContext.Current.CancellationToken);

        result.Page.Should().Be(ListCompaniesHandler.DefaultPage);
        result.PageSize.Should().Be(ListCompaniesHandler.DefaultPageSize);
        result.Data.Should().ContainSingle();
    }

    [Fact]
    public async Task AC1_ClampsPageSize_ToMax()
    {
        await using var ctx = NewContext();
        Seed(ctx, Company("900400001", "Solo", active: true, created: "2026-01-01"));

        var result = await Handler(ctx).HandleAsync(new ListCompaniesQuery { Page = 1, PageSize = ListCompaniesHandler.MaxPageSize + 50 }, TestContext.Current.CancellationToken);

        result.PageSize.Should().Be(ListCompaniesHandler.MaxPageSize);
    }

    // ---------- AC2: Filtros opcionales (RF02) ----------

    [Fact]
    public async Task AC2_FiltersByNit_PartialMatch()
    {
        await using var ctx = NewContext();
        Seed(ctx,
            Company("900111111", "Alfa", active: true, created: "2026-01-01"),
            Company("800222222", "Beta", active: true, created: "2026-01-02"));

        var result = await Handler(ctx).HandleAsync(new ListCompaniesQuery { Nit = "900" }, TestContext.Current.CancellationToken);

        result.TotalCount.Should().Be(1);
        result.Data.Should().ContainSingle().Which.Nit.Should().Be("900111111");
    }

    [Fact]
    public async Task AC2_FiltersByRazonSocial_CaseInsensitivePartial()
    {
        await using var ctx = NewContext();
        Seed(ctx,
            Company("900111111", "FLIT Logística SAS", active: true, created: "2026-01-01"),
            Company("900222222", "Otra Empresa", active: true, created: "2026-01-02"));

        var result = await Handler(ctx).HandleAsync(new ListCompaniesQuery { RazonSocial = "flit" }, TestContext.Current.CancellationToken);

        result.Data.Should().ContainSingle().Which.RazonSocial.Should().Be("FLIT Logística SAS");
    }

    [Fact]
    public async Task AC2_FiltersByEstadoActivo()
    {
        await using var ctx = NewContext();
        Seed(ctx,
            Company("900111111", "Activa", active: true, created: "2026-01-01"),
            Company("900222222", "Inactiva", active: false, created: "2026-01-02"));

        var actives = await Handler(ctx).HandleAsync(new ListCompaniesQuery { EstadoActivo = true }, TestContext.Current.CancellationToken);
        var inactives = await Handler(ctx).HandleAsync(new ListCompaniesQuery { EstadoActivo = false }, TestContext.Current.CancellationToken);

        actives.Data.Should().ContainSingle().Which.RazonSocial.Should().Be("Activa");
        inactives.Data.Should().ContainSingle().Which.RazonSocial.Should().Be("Inactiva");
    }

    [Fact]
    public async Task AC2_FiltersByCreationDateRange_Inclusive()
    {
        await using var ctx = NewContext();
        Seed(ctx,
            Company("900111111", "Antes", active: true, created: "2025-12-31"),
            Company("900222222", "Dentro1", active: true, created: "2026-01-01"),
            Company("900333333", "Dentro2", active: true, created: "2026-12-31"),
            Company("900444444", "Despues", active: true, created: "2027-01-01"));

        var result = await Handler(ctx).HandleAsync(new ListCompaniesQuery
        {
            FechaDesde = new DateOnly(2026, 1, 1),
            FechaHasta = new DateOnly(2026, 12, 31),
        }, TestContext.Current.CancellationToken);

        result.Data.Select(c => c.RazonSocial).Should().BeEquivalentTo("Dentro1", "Dentro2");
    }

    [Fact]
    public async Task AC2_CombinesFilters_WithAnd()
    {
        await using var ctx = NewContext();
        Seed(ctx,
            Company("900111111", "FLIT Activa", active: true, created: "2026-06-01"),
            Company("900222222", "FLIT Inactiva", active: false, created: "2026-06-02"),
            Company("800333333", "Otra Activa", active: true, created: "2026-06-03"));

        var result = await Handler(ctx).HandleAsync(new ListCompaniesQuery
        {
            Nit = "900",
            RazonSocial = "flit",
            EstadoActivo = true,
        }, TestContext.Current.CancellationToken);

        result.Data.Should().ContainSingle().Which.RazonSocial.Should().Be("FLIT Activa");
    }

    [Fact]
    public async Task AC2_NoFilters_ReturnsAll()
    {
        await using var ctx = NewContext();
        Seed(ctx,
            Company("900111111", "A", active: true, created: "2026-01-01"),
            Company("900222222", "B", active: false, created: "2026-01-02"));

        var result = await Handler(ctx).HandleAsync(new ListCompaniesQuery(), TestContext.Current.CancellationToken);

        result.TotalCount.Should().Be(2);
    }

    // ---------- Helpers ----------

    private static FlitDbContext NewContext() =>
        new(new DbContextOptionsBuilder<FlitDbContext>()
            .UseInMemoryDatabase($"flit-admin-{Guid.NewGuid()}")
            .Options);

    private static ListCompaniesHandler Handler(FlitDbContext ctx) =>
        new(new CompanyReadRepository(ctx));

    private static void Seed(FlitDbContext ctx, params Tenant[] tenants)
    {
        ctx.Tenants.AddRange(tenants);
        ctx.SaveChanges();
    }

    private static Tenant Company(string taxId, string legalName, bool active, string created) =>
        new()
        {
            Id = Guid.NewGuid(),
            Code = taxId,
            LegalName = legalName,
            TaxId = taxId,
            TenantType = "company",
            IsActive = active,
            CreatedAt = new DateTimeOffset(DateOnly.Parse(created).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero),
            RowVersion = 0,
        };
}
