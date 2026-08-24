using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Identity;
using Flit.Infrastructure.Persistence.Repositories;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Enums;
using Flit.Tramites.Domain.Tramites.Estados;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Flit.Infrastructure.Tests.Persistence;

/// <summary>
/// Filtrado/ordenamiento server-side del listado de trámites
/// (<see cref="ProcedureInstanceRepository.ListWithSummaryGraphFilteredAsync"/>) sobre EF InMemory:
/// cada campo permitido de <c>sortBy</c> en ASC/DESC, cada filtro, aislamiento por tenant y el "sortBy
/// inválido" (resuelto en la capa Application) cayendo al orden por defecto sin romper la consulta.
/// </summary>
public sealed class ProcedureInstanceListFilteredRepositoryTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid OtherTenantId = Guid.NewGuid();
    private static readonly DateTimeOffset Base = new(2026, 7, 1, 12, 0, 0, TimeSpan.Zero);

    private static FlitDbContext NewContext(string dbName) =>
        new(new DbContextOptionsBuilder<FlitDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options);

    private static ProcedureInstance Instancia(
        Guid tenantId, string reference, string? vin = null, string? plate = null,
        string? vendedor = null, string? comprador = null, Guid? createdBy = null,
        DateTimeOffset? createdAt = null, DateTimeOffset? updatedAt = null, bool prioritario = false,
        string modalidad = "traspaso") => new()
    {
        ProcedureType = ProcedureTypeFixture.For(modalidad),
        Id = Guid.NewGuid(),
        TenantId = tenantId,
        ProcedureTypeId = Guid.NewGuid(),
        ReferenceNumber = reference,
        ModalidadEntrada = modalidad,
        Vin = vin,
        Plate = plate,
        VendedorNombre = vendedor,
        CompradorNombre = comprador,
        CreatedByUserId = createdBy ?? Guid.NewGuid(),
        CreatedAt = createdAt ?? Base,
        UpdatedAt = updatedAt,
        Prioritario = prioritario,
    };

    private static ProcedureInstanceSignature FirmaCompraventa(string parte, string estado) => new()
    {
        Id = Guid.NewGuid(),
        Parte = parte,
        DocTipo = SignatureDocTipos.Compraventa,
        Estado = estado,
        SolicitadoAt = Base,
        CreatedAt = Base,
    };

    // ── sortBy: cada campo permitido, ASC y DESC ──────────────────────────────────────────

    [Theory]
    [InlineData(ProcedureInstanceSortBy.Vin, SortDirection.Ascending, new[] { "A-VIN1", "B-VIN2", "C-VIN3" })]
    [InlineData(ProcedureInstanceSortBy.Vin, SortDirection.Descending, new[] { "C-VIN3", "B-VIN2", "A-VIN1" })]
    public async Task OrdenaPorVin(ProcedureInstanceSortBy sortBy, SortDirection direction, string[] ordenEsperado)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = NewContext($"vin-{sortBy}-{direction}");
        db.ProcedureInstances.AddRange(
            Instancia(TenantId, "R1", vin: "B-VIN2"),
            Instancia(TenantId, "R2", vin: "A-VIN1"),
            Instancia(TenantId, "R3", vin: "C-VIN3"));
        await db.SaveChangesAsync(ct);
        var repo = new ProcedureInstanceRepository(db);

        var (items, total) = await repo.ListWithSummaryGraphFilteredAsync(
            TenantId, 0, 20, new ProcedureInstanceListFilter(), sortBy, direction, ct);

        total.Should().Be(3);
        items.Select(i => i.Vin).Should().ContainInOrder(ordenEsperado);
    }

    [Theory]
    [InlineData(SortDirection.Ascending, new[] { "AAA111", "BBB222", "CCC333" })]
    [InlineData(SortDirection.Descending, new[] { "CCC333", "BBB222", "AAA111" })]
    public async Task OrdenaPorPlaca(SortDirection direction, string[] ordenEsperado)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = NewContext($"placa-{direction}");
        db.ProcedureInstances.AddRange(
            Instancia(TenantId, "R1", plate: "BBB222"),
            Instancia(TenantId, "R2", plate: "AAA111"),
            Instancia(TenantId, "R3", plate: "CCC333"));
        await db.SaveChangesAsync(ct);
        var repo = new ProcedureInstanceRepository(db);

        var (items, _) = await repo.ListWithSummaryGraphFilteredAsync(
            TenantId, 0, 20, new ProcedureInstanceListFilter(), ProcedureInstanceSortBy.Placa, direction, ct);

        items.Select(i => i.Plate).Should().ContainInOrder(ordenEsperado);
    }

    [Theory]
    [InlineData(SortDirection.Ascending, new[] { "Ana", "Beto", "Carla" })]
    [InlineData(SortDirection.Descending, new[] { "Carla", "Beto", "Ana" })]
    public async Task OrdenaPorComprador(SortDirection direction, string[] ordenEsperado)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = NewContext($"comprador-{direction}");
        db.ProcedureInstances.AddRange(
            Instancia(TenantId, "R1", comprador: "Beto"),
            Instancia(TenantId, "R2", comprador: "Ana"),
            Instancia(TenantId, "R3", comprador: "Carla"));
        await db.SaveChangesAsync(ct);
        var repo = new ProcedureInstanceRepository(db);

        var (items, _) = await repo.ListWithSummaryGraphFilteredAsync(
            TenantId, 0, 20, new ProcedureInstanceListFilter(), ProcedureInstanceSortBy.Comprador, direction, ct);

        items.Select(i => i.CompradorNombre).Should().ContainInOrder(ordenEsperado);
    }

    [Theory]
    [InlineData(SortDirection.Ascending, new[] { "R2", "R1", "R3" })]
    [InlineData(SortDirection.Descending, new[] { "R3", "R1", "R2" })]
    public async Task OrdenaPorFechaCreacion(SortDirection direction, string[] ordenEsperado)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = NewContext($"created-{direction}");
        db.ProcedureInstances.AddRange(
            Instancia(TenantId, "R1", createdAt: Base),
            Instancia(TenantId, "R2", createdAt: Base.AddDays(-1)),
            Instancia(TenantId, "R3", createdAt: Base.AddDays(1)));
        await db.SaveChangesAsync(ct);
        var repo = new ProcedureInstanceRepository(db);

        var (items, _) = await repo.ListWithSummaryGraphFilteredAsync(
            TenantId, 0, 20, new ProcedureInstanceListFilter(), ProcedureInstanceSortBy.CreatedAt, direction, ct);

        items.Select(i => i.ReferenceNumber).Should().ContainInOrder(ordenEsperado);
    }

    [Theory]
    [InlineData(SortDirection.Ascending, new[] { "R2", "R1", "R3" })]
    [InlineData(SortDirection.Descending, new[] { "R3", "R1", "R2" })]
    public async Task OrdenaPorFechaActualizacion(SortDirection direction, string[] ordenEsperado)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = NewContext($"updated-{direction}");
        db.ProcedureInstances.AddRange(
            Instancia(TenantId, "R1", updatedAt: Base),
            Instancia(TenantId, "R2", updatedAt: Base.AddDays(-1)),
            Instancia(TenantId, "R3", updatedAt: Base.AddDays(1)));
        await db.SaveChangesAsync(ct);
        var repo = new ProcedureInstanceRepository(db);

        var (items, _) = await repo.ListWithSummaryGraphFilteredAsync(
            TenantId, 0, 20, new ProcedureInstanceListFilter(), ProcedureInstanceSortBy.UpdatedAt, direction, ct);

        items.Select(i => i.ReferenceNumber).Should().ContainInOrder(ordenEsperado);
    }

    [Theory]
    [InlineData(SortDirection.Ascending, new[] { "Ana Gestora", "Beto Gestor", "Carla Gestora" })]
    [InlineData(SortDirection.Descending, new[] { "Carla Gestora", "Beto Gestor", "Ana Gestora" })]
    public async Task OrdenaPorGestor_ViaJoinAIdentityUsers(SortDirection direction, string[] ordenEsperado)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = NewContext($"gestor-{direction}");
        var (u1, u2, u3) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        db.Users.AddRange(
            new User { Id = u1, Email = "beto@flit.io", DisplayName = "Beto Gestor" },
            new User { Id = u2, Email = "ana@flit.io", DisplayName = "Ana Gestora" },
            new User { Id = u3, Email = "carla@flit.io", DisplayName = "Carla Gestora" });
        db.ProcedureInstances.AddRange(
            Instancia(TenantId, "R1", createdBy: u1),
            Instancia(TenantId, "R2", createdBy: u2),
            Instancia(TenantId, "R3", createdBy: u3));
        await db.SaveChangesAsync(ct);
        var repo = new ProcedureInstanceRepository(db);

        var (items, _) = await repo.ListWithSummaryGraphFilteredAsync(
            TenantId, 0, 20, new ProcedureInstanceListFilter(), ProcedureInstanceSortBy.Gestor, direction, ct);

        var displayNames = items
            .Select(i => db.Users.Single(u => u.Id == i.CreatedByUserId).DisplayName)
            .ToArray();
        displayNames.Should().ContainInOrder(ordenEsperado);
    }

    [Fact] // sortBy inválido lo resuelve la capa Application a Default ANTES de llegar aquí; el
           // repositorio nunca ve un valor fuera del enum, así que no hay forma de "romper" el ORDER BY.
    public async Task Default_OrdenaPorPrioritarioLuegoCreacionDescendente()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = NewContext("default-order");
        db.ProcedureInstances.AddRange(
            Instancia(TenantId, "Vieja-NoPrioritaria", createdAt: Base.AddDays(-2)),
            Instancia(TenantId, "Nueva-NoPrioritaria", createdAt: Base),
            Instancia(TenantId, "Vieja-Prioritaria", createdAt: Base.AddDays(-3), prioritario: true));
        await db.SaveChangesAsync(ct);
        var repo = new ProcedureInstanceRepository(db);

        var (items, _) = await repo.ListWithSummaryGraphFilteredAsync(
            TenantId, 0, 20, new ProcedureInstanceListFilter(),
            ProcedureInstanceSortBy.Default, SortDirection.Descending, ct);

        items.Select(i => i.ReferenceNumber).Should().ContainInOrder(
            "Vieja-Prioritaria", "Nueva-NoPrioritaria", "Vieja-NoPrioritaria");
    }

    // ── filtros ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task FiltraPorVin_IgualdadCaseInsensitive()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = NewContext("filtro-vin");
        db.ProcedureInstances.AddRange(
            Instancia(TenantId, "R1", vin: "abc123"),
            Instancia(TenantId, "R2", vin: "XYZ999"));
        await db.SaveChangesAsync(ct);
        var repo = new ProcedureInstanceRepository(db);

        var (items, total) = await repo.ListWithSummaryGraphFilteredAsync(
            TenantId, 0, 20, new ProcedureInstanceListFilter { Vin = "ABC123" },
            ProcedureInstanceSortBy.Default, SortDirection.Descending, ct);

        total.Should().Be(1);
        items.Should().ContainSingle(i => i.ReferenceNumber == "R1");
    }

    [Fact]
    public async Task FiltraPorPlaca_IgualdadCaseInsensitive()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = NewContext("filtro-placa");
        db.ProcedureInstances.AddRange(
            Instancia(TenantId, "R1", plate: "aaa111"),
            Instancia(TenantId, "R2", plate: "bbb222"));
        await db.SaveChangesAsync(ct);
        var repo = new ProcedureInstanceRepository(db);

        var (items, total) = await repo.ListWithSummaryGraphFilteredAsync(
            TenantId, 0, 20, new ProcedureInstanceListFilter { Placa = "AAA111" },
            ProcedureInstanceSortBy.Default, SortDirection.Descending, ct);

        total.Should().Be(1);
        items.Should().ContainSingle(i => i.ReferenceNumber == "R1");
    }

    [Fact]
    public async Task FiltraPorVendedor_SubcadenaCaseInsensitive()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = NewContext("filtro-vendedor");
        db.ProcedureInstances.AddRange(
            Instancia(TenantId, "R1", vendedor: "María Pérez"),
            Instancia(TenantId, "R2", vendedor: "Carlos Gómez"));
        await db.SaveChangesAsync(ct);
        var repo = new ProcedureInstanceRepository(db);

        var (items, total) = await repo.ListWithSummaryGraphFilteredAsync(
            TenantId, 0, 20, new ProcedureInstanceListFilter { Vendedor = "pérez" },
            ProcedureInstanceSortBy.Default, SortDirection.Descending, ct);

        total.Should().Be(1);
        items.Should().ContainSingle(i => i.ReferenceNumber == "R1");
    }

    [Fact]
    public async Task FiltraPorComprador_SubcadenaCaseInsensitive()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = NewContext("filtro-comprador");
        db.ProcedureInstances.AddRange(
            Instancia(TenantId, "R1", comprador: "Juan Comprador"),
            Instancia(TenantId, "R2", comprador: "Ana Otra"));
        await db.SaveChangesAsync(ct);
        var repo = new ProcedureInstanceRepository(db);

        var (items, total) = await repo.ListWithSummaryGraphFilteredAsync(
            TenantId, 0, 20, new ProcedureInstanceListFilter { Comprador = "JUAN" },
            ProcedureInstanceSortBy.Default, SortDirection.Descending, ct);

        total.Should().Be(1);
        items.Should().ContainSingle(i => i.ReferenceNumber == "R1");
    }

    [Fact]
    public async Task FiltraPorGestor_SubcadenaViaJoin()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = NewContext("filtro-gestor");
        var (u1, u2) = (Guid.NewGuid(), Guid.NewGuid());
        db.Users.AddRange(
            new User { Id = u1, Email = "ana@flit.io", DisplayName = "Ana Gestora" },
            new User { Id = u2, Email = "beto@flit.io", DisplayName = "Beto Gestor" });
        db.ProcedureInstances.AddRange(
            Instancia(TenantId, "R1", createdBy: u1),
            Instancia(TenantId, "R2", createdBy: u2));
        await db.SaveChangesAsync(ct);
        var repo = new ProcedureInstanceRepository(db);

        var (items, total) = await repo.ListWithSummaryGraphFilteredAsync(
            TenantId, 0, 20, new ProcedureInstanceListFilter { Gestor = "gestora" },
            ProcedureInstanceSortBy.Default, SortDirection.Descending, ct);

        total.Should().Be(1);
        items.Should().ContainSingle(i => i.ReferenceNumber == "R1");
    }

    [Fact]
    public async Task FiltraPorFirmado_Completo_TraspasoExigeAmbasPartes()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = NewContext("filtro-firmado-completo");
        var completo = Instancia(TenantId, "Completo");
        completo.Signatures.Add(FirmaCompraventa(SignatureRules.ParteComprador, SignatureEstados.Firmada));
        completo.Signatures.Add(FirmaCompraventa(SignatureRules.ParteVendedor, SignatureEstados.Firmada));

        var soloComprador = Instancia(TenantId, "SoloComprador");
        soloComprador.Signatures.Add(FirmaCompraventa(SignatureRules.ParteComprador, SignatureEstados.Firmada));

        db.ProcedureInstances.AddRange(completo, soloComprador);
        await db.SaveChangesAsync(ct);
        var repo = new ProcedureInstanceRepository(db);

        var (items, total) = await repo.ListWithSummaryGraphFilteredAsync(
            TenantId, 0, 20, new ProcedureInstanceListFilter { Firmado = true },
            ProcedureInstanceSortBy.Default, SortDirection.Descending, ct);

        total.Should().Be(1);
        items.Should().ContainSingle(i => i.ReferenceNumber == "Completo");
    }

    [Fact]
    public async Task FiltraPorFirmado_Pendiente_DevuelveLosIncompletos()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = NewContext("filtro-firmado-pendiente");
        var completo = Instancia(TenantId, "Completo");
        completo.Signatures.Add(FirmaCompraventa(SignatureRules.ParteComprador, SignatureEstados.Firmada));
        completo.Signatures.Add(FirmaCompraventa(SignatureRules.ParteVendedor, SignatureEstados.Firmada));

        var pendiente = Instancia(TenantId, "Pendiente");

        db.ProcedureInstances.AddRange(completo, pendiente);
        await db.SaveChangesAsync(ct);
        var repo = new ProcedureInstanceRepository(db);

        var (items, total) = await repo.ListWithSummaryGraphFilteredAsync(
            TenantId, 0, 20, new ProcedureInstanceListFilter { Firmado = false },
            ProcedureInstanceSortBy.Default, SortDirection.Descending, ct);

        total.Should().Be(1);
        items.Should().ContainSingle(i => i.ReferenceNumber == "Pendiente");
    }

    [Fact]
    public async Task FiltraPorFirmado_MatriculaInicial_NoExigeVendedor()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = NewContext("filtro-firmado-matricula");
        var matricula = Instancia(TenantId, "Matricula", modalidad: TramiteModalidadEntradaCodes.MatriculaInicial);
        matricula.Signatures.Add(FirmaCompraventa(SignatureRules.ParteComprador, SignatureEstados.Firmada));

        db.ProcedureInstances.Add(matricula);
        await db.SaveChangesAsync(ct);
        var repo = new ProcedureInstanceRepository(db);

        var (items, total) = await repo.ListWithSummaryGraphFilteredAsync(
            TenantId, 0, 20, new ProcedureInstanceListFilter { Firmado = true },
            ProcedureInstanceSortBy.Default, SortDirection.Descending, ct);

        total.Should().Be(1);
        items.Should().ContainSingle(i => i.ReferenceNumber == "Matricula");
    }

    [Fact]
    public async Task FiltraPorRangoDeFechaDeCreacion()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = NewContext("filtro-created-range");
        db.ProcedureInstances.AddRange(
            Instancia(TenantId, "Antes", createdAt: Base.AddDays(-10)),
            Instancia(TenantId, "Dentro", createdAt: Base),
            Instancia(TenantId, "Despues", createdAt: Base.AddDays(10)));
        await db.SaveChangesAsync(ct);
        var repo = new ProcedureInstanceRepository(db);

        var (items, total) = await repo.ListWithSummaryGraphFilteredAsync(
            TenantId, 0, 20,
            new ProcedureInstanceListFilter { CreatedFrom = Base.AddDays(-1), CreatedTo = Base.AddDays(1) },
            ProcedureInstanceSortBy.Default, SortDirection.Descending, ct);

        total.Should().Be(1);
        items.Should().ContainSingle(i => i.ReferenceNumber == "Dentro");
    }

    [Fact]
    public async Task FiltraPorRangoDeFechaDeActualizacion_ExcluyeNulos()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = NewContext("filtro-updated-range");
        db.ProcedureInstances.AddRange(
            Instancia(TenantId, "SinActualizar", updatedAt: null),
            Instancia(TenantId, "Dentro", updatedAt: Base),
            Instancia(TenantId, "Despues", updatedAt: Base.AddDays(10)));
        await db.SaveChangesAsync(ct);
        var repo = new ProcedureInstanceRepository(db);

        var (items, total) = await repo.ListWithSummaryGraphFilteredAsync(
            TenantId, 0, 20,
            new ProcedureInstanceListFilter { UpdatedFrom = Base.AddDays(-1), UpdatedTo = Base.AddDays(1) },
            ProcedureInstanceSortBy.Default, SortDirection.Descending, ct);

        total.Should().Be(1);
        items.Should().ContainSingle(i => i.ReferenceNumber == "Dentro");
    }

    [Fact]
    public async Task AislamientoPorTenant_NoDevuelveFilasDeOtroTenant()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = NewContext("aislamiento-tenant");
        db.ProcedureInstances.AddRange(
            Instancia(TenantId, "Mio", vin: "ABC123"),
            Instancia(OtherTenantId, "DeOtroTenant", vin: "ABC123"));
        await db.SaveChangesAsync(ct);
        var repo = new ProcedureInstanceRepository(db);

        var (items, total) = await repo.ListWithSummaryGraphFilteredAsync(
            TenantId, 0, 20, new ProcedureInstanceListFilter { Vin = "ABC123" },
            ProcedureInstanceSortBy.Default, SortDirection.Descending, ct);

        total.Should().Be(1);
        items.Should().ContainSingle(i => i.ReferenceNumber == "Mio");
    }

    [Fact]
    public async Task TenantNull_ListaTodosLosTenants_SoloSuperAdmin()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = NewContext("tenant-null-superadmin");
        db.ProcedureInstances.AddRange(
            Instancia(TenantId, "DeTenantA"),
            Instancia(OtherTenantId, "DeTenantB"));
        await db.SaveChangesAsync(ct);
        var repo = new ProcedureInstanceRepository(db);

        var (items, total) = await repo.ListWithSummaryGraphFilteredAsync(
            null, 0, 20, new ProcedureInstanceListFilter(), ProcedureInstanceSortBy.Default, SortDirection.Descending, ct);

        total.Should().Be(2);
        items.Select(i => i.ReferenceNumber).Should().BeEquivalentTo(["DeTenantA", "DeTenantB"]);
    }

    [Fact]
    public async Task PaginaConSkipYTake()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = NewContext("paginacion");
        db.ProcedureInstances.AddRange(
            Instancia(TenantId, "R1", vin: "A"),
            Instancia(TenantId, "R2", vin: "B"),
            Instancia(TenantId, "R3", vin: "C"));
        await db.SaveChangesAsync(ct);
        var repo = new ProcedureInstanceRepository(db);

        var (items, total) = await repo.ListWithSummaryGraphFilteredAsync(
            TenantId, 1, 1, new ProcedureInstanceListFilter(), ProcedureInstanceSortBy.Vin, SortDirection.Ascending, ct);

        total.Should().Be(3); // el total NO se pagina, es el conteo del filtro completo.
        items.Should().ContainSingle(i => i.Vin == "B");
    }
}
