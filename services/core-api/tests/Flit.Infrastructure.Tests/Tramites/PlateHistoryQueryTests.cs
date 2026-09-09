using Flit.Api.Endpoints.Tramites;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Identity;
using Flit.Infrastructure.Persistence.Repositories;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Tramites.Estados;
using Flit.Tramites.Domain.Tramites.ValueObjects;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Flit.Infrastructure.Tests.Tramites;

/// <summary>
/// Historial operativo por placa (Feature #12189, HU #12192) — el resultado REAL de la consulta que
/// arma el endpoint, ejecutada por el mismo handler y el mismo repositorio que usa producción sobre
/// EF InMemory.
///
/// <para>
/// Uso de ejemplo (lo que hace el endpoint, sin HTTP):
/// <code>
/// var alcance = PlateHistoryScope.Resolve(tenantDelUsuario, isSuperAdmin: false);
/// var request = PlateHistoryScope.BuildRequest(alcance, PlacaNormalizer.Normalize(placa), skip, take);
/// var (items, total) = await new ListProcedureInstancesFilteredHandler(repo).HandleAsync(request, ct);
/// </code>
/// </para>
///
/// <para>
/// Se ejercita la cadena completa a propósito: el aislamiento por compañía y la exclusión de los
/// borrados lógicos no viven en el endpoint sino en el <c>WHERE</c> del repositorio, y una prueba
/// que solo mirara el objeto de consulta no detectaría que ese <c>WHERE</c> se rompa.
/// </para>
/// </summary>
public sealed class PlateHistoryQueryTests
{
    private static readonly Guid TenantPropio = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid TenantAjeno = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");
    private static readonly DateTimeOffset Base = new(2026, 7, 1, 12, 0, 0, TimeSpan.Zero);

    private const string Placa = "ABC123";

    private static FlitDbContext NewContext(string dbName) =>
        new(new DbContextOptionsBuilder<FlitDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options);

    private static ProcedureInstance Instancia(
        Guid tenantId,
        string reference,
        string? plate = Placa,
        DateTimeOffset? createdAt = null,
        DateTimeOffset? deletedAt = null) => new()
        {
            ProcedureType = ProcedureTypeFixture.Traspaso,
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ProcedureTypeId = ProcedureTypeFixture.Traspaso.Id,
            ReferenceNumber = reference,
            Plate = plate,
            Status = TramiteEstado.Entregado,
            CreatedByUserId = Guid.NewGuid(),
            CreatedAt = createdAt ?? Base,
            DeletedAt = deletedAt,
        };

    private static Tenant Compania(Guid id, string razonSocial) => new()
    {
        Id = id,
        Code = razonSocial[..3].ToUpperInvariant(),
        LegalName = razonSocial,
        TaxId = "900123456",
        TenantType = "empresa",
        IsActive = true,
        CreatedAt = Base,
    };

    /// <summary>Ejecuta el historial exactamente como lo compone el endpoint.</summary>
    private static async Task<(IReadOnlyList<InstanceSummaryDto> Items, int Total)> HistorialAsync(
        FlitDbContext db,
        Guid? contextTenantId,
        bool isSuperAdmin,
        string placa,
        int? skip = null,
        int? take = null,
        CancellationToken ct = default)
    {
        var alcance = PlateHistoryScope.Resolve(contextTenantId, isSuperAdmin);
        alcance.Allowed.Should().BeTrue("el caso de alcance rechazado se cubre en PlateHistoryScopeTests");

        var placaNormalizada = PlacaNormalizer.NormalizeOrNull(placa);
        placaNormalizada.Should().NotBeNull();

        var request = PlateHistoryScope.BuildRequest(alcance, placaNormalizada!, skip, take);
        var handler = new ListProcedureInstancesFilteredHandler(new ProcedureInstanceRepository(db));
        return await handler.HandleAsync(request, ct);
    }

    // ── AC — un AdminCompany solo ve los trámites de SU compañía ──────────────────────────────

    [Fact]
    public async Task AdminCompany_ConLaPlacaEnOtraCompania_SoloRecibeLosSuyos()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = NewContext("plate-history-scope-company");
        db.Tenants.AddRange(Compania(TenantPropio, "Compañía Propia SAS"), Compania(TenantAjeno, "Compañía Ajena SAS"));
        db.ProcedureInstances.AddRange(
            Instancia(TenantPropio, "PROPIO-1", createdAt: Base),
            Instancia(TenantPropio, "PROPIO-2", createdAt: Base.AddDays(1)),
            Instancia(TenantAjeno, "AJENO-1", createdAt: Base.AddDays(2)));
        await db.SaveChangesAsync(ct);

        var (items, total) = await HistorialAsync(db, TenantPropio, isSuperAdmin: false, Placa, ct: ct);

        total.Should().Be(2);
        items.Should().OnlyContain(i => i.TenantId == TenantPropio);
        items.Select(i => i.ReferenceNumber).Should().BeEquivalentTo(["PROPIO-1", "PROPIO-2"]);
        items.Should().NotContain(i => i.ReferenceNumber == "AJENO-1",
            "el trámite de otra compañía es una fuga cross-tenant, no un resultado");
    }

    // ── AC — el mismo caso como SuperAdmin devuelve todos, con companiaNombre ──────────────────

    [Fact]
    public async Task SuperAdmin_RecibeLaPlacaEnTodasLasCompanias_ConSuRazonSocial()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = NewContext("plate-history-scope-superadmin");
        db.Tenants.AddRange(Compania(TenantPropio, "Compañía Propia SAS"), Compania(TenantAjeno, "Compañía Ajena SAS"));
        db.ProcedureInstances.AddRange(
            Instancia(TenantPropio, "PROPIO-1", createdAt: Base),
            Instancia(TenantPropio, "PROPIO-2", createdAt: Base.AddDays(1)),
            Instancia(TenantAjeno, "AJENO-1", createdAt: Base.AddDays(2)));
        await db.SaveChangesAsync(ct);

        var (items, total) = await HistorialAsync(db, contextTenantId: null, isSuperAdmin: true, Placa, ct: ct);

        total.Should().Be(3);
        items.Select(i => i.ReferenceNumber).Should().BeEquivalentTo(["PROPIO-1", "PROPIO-2", "AJENO-1"]);
        items.Should().OnlyContain(i => !string.IsNullOrWhiteSpace(i.CompaniaNombre),
            "sin la razón social el SuperAdmin no puede saber de quién es cada fila");
        items.Single(i => i.ReferenceNumber == "AJENO-1").CompaniaNombre.Should().Be("Compañía Ajena SAS");
    }

    // ── AC — placa inexistente: 200 con lista vacía, nunca 404 ────────────────────────────────

    [Fact]
    public async Task PlacaSinTramites_DevuelveListaVaciaYTotalCero()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = NewContext("plate-history-vacio");
        db.Tenants.Add(Compania(TenantPropio, "Compañía Propia SAS"));
        db.ProcedureInstances.Add(Instancia(TenantPropio, "OTRA-PLACA", plate: "XYZ789"));
        await db.SaveChangesAsync(ct);

        var (items, total) = await HistorialAsync(db, TenantPropio, isSuperAdmin: false, "ZZZ000", ct: ct);

        items.Should().BeEmpty();
        total.Should().Be(0);
    }

    // ── AC — placa en minúsculas o con espacios: mismos resultados ────────────────────────────

    [Theory]
    [InlineData("ABC123")]
    [InlineData("abc123")]
    [InlineData("  abc123  ")]
    [InlineData("AbC123")]
    public async Task PlacaConCajaOEspaciosDistintos_DevuelveLoMismo(string consultada)
    {
        var ct = TestContext.Current.CancellationToken;
        // Base propia por caso: varias variantes de la MISMA placa normalizan al mismo texto y
        // compartirían el almacén InMemory si el nombre se derivara de ella.
        await using var db = NewContext($"plate-history-caja-{Guid.NewGuid()}");
        db.Tenants.Add(Compania(TenantPropio, "Compañía Propia SAS"));
        db.ProcedureInstances.AddRange(
            Instancia(TenantPropio, "R-1", createdAt: Base),
            Instancia(TenantPropio, "R-2", createdAt: Base.AddDays(1)),
            Instancia(TenantPropio, "OTRA", plate: "XYZ789"));
        await db.SaveChangesAsync(ct);

        var (items, total) = await HistorialAsync(db, TenantPropio, isSuperAdmin: false, consultada, ct: ct);

        total.Should().Be(2);
        items.Select(i => i.ReferenceNumber).Should().BeEquivalentTo(["R-1", "R-2"]);
    }

    [Fact]
    public async Task PlacaGuardadaEnMinusculas_TambienSeEncuentra()
    {
        // La columna se denormaliza desde los field_values: no hay garantía dura de que llegue
        // siempre en mayúsculas, así que la comparación tiene que ser insensible a la caja en
        // AMBOS lados, no solo en el término consultado.
        var ct = TestContext.Current.CancellationToken;
        await using var db = NewContext("plate-history-caja-columna");
        db.Tenants.Add(Compania(TenantPropio, "Compañía Propia SAS"));
        db.ProcedureInstances.Add(Instancia(TenantPropio, "R-1", plate: "abc123"));
        await db.SaveChangesAsync(ct);

        var (items, _) = await HistorialAsync(db, TenantPropio, isSuperAdmin: false, "ABC123", ct: ct);

        items.Should().ContainSingle().Which.ReferenceNumber.Should().Be("R-1");
    }

    // ── AC — orden cronológico descendente ────────────────────────────────────────────────────

    [Fact]
    public async Task ElHistorial_LlegaDelMasRecienteAlMasAntiguo()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = NewContext("plate-history-orden");
        db.Tenants.Add(Compania(TenantPropio, "Compañía Propia SAS"));
        // Insertados en desorden a propósito: el orden no puede depender del de inserción.
        db.ProcedureInstances.AddRange(
            Instancia(TenantPropio, "MEDIO", createdAt: Base.AddDays(5)),
            Instancia(TenantPropio, "VIEJO", createdAt: Base),
            Instancia(TenantPropio, "NUEVO", createdAt: Base.AddDays(10)));
        await db.SaveChangesAsync(ct);

        var (items, _) = await HistorialAsync(db, TenantPropio, isSuperAdmin: false, Placa, ct: ct);

        items.Select(i => i.ReferenceNumber).Should().ContainInOrder("NUEVO", "MEDIO", "VIEJO");
        items.Select(i => i.CreatedAt).Should().BeInDescendingOrder();
    }

    // ── AC — los borrados lógicos no aparecen (decisión D2) ───────────────────────────────────

    [Fact]
    public async Task TramiteConBorradoLogico_NoApareceNiCuentaEnElTotal()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = NewContext("plate-history-borrado");
        db.Tenants.Add(Compania(TenantPropio, "Compañía Propia SAS"));
        db.ProcedureInstances.AddRange(
            Instancia(TenantPropio, "VIVO", createdAt: Base),
            Instancia(TenantPropio, "BORRADO", createdAt: Base.AddDays(1), deletedAt: Base.AddDays(2)));
        await db.SaveChangesAsync(ct);

        var (items, total) = await HistorialAsync(db, TenantPropio, isSuperAdmin: false, Placa, ct: ct);

        total.Should().Be(1, "el borrado lógico tampoco cuenta para la paginación");
        items.Should().ContainSingle().Which.ReferenceNumber.Should().Be("VIVO");
    }

    [Fact]
    public async Task ElBorradoLogico_TampocoLoVeElSuperAdmin()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = NewContext("plate-history-borrado-superadmin");
        db.Tenants.AddRange(Compania(TenantPropio, "Compañía Propia SAS"), Compania(TenantAjeno, "Compañía Ajena SAS"));
        db.ProcedureInstances.AddRange(
            Instancia(TenantPropio, "VIVO", createdAt: Base),
            Instancia(TenantAjeno, "BORRADO", createdAt: Base.AddDays(1), deletedAt: Base.AddDays(2)));
        await db.SaveChangesAsync(ct);

        var (items, total) = await HistorialAsync(db, null, isSuperAdmin: true, Placa, ct: ct);

        total.Should().Be(1);
        items.Should().ContainSingle().Which.ReferenceNumber.Should().Be("VIVO");
    }

    // ── Paginación: el total es el del universo, no el de la página ───────────────────────────

    [Fact]
    public async Task Paginacion_DevuelveLaVentanaPedidaYElTotalCompleto()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = NewContext("plate-history-paginacion");
        db.Tenants.Add(Compania(TenantPropio, "Compañía Propia SAS"));
        for (var i = 0; i < 5; i++)
            db.ProcedureInstances.Add(Instancia(TenantPropio, $"R-{i}", createdAt: Base.AddDays(i)));
        await db.SaveChangesAsync(ct);

        var (primera, total) = await HistorialAsync(db, TenantPropio, false, Placa, skip: 0, take: 2, ct: ct);
        var (segunda, totalSegunda) = await HistorialAsync(db, TenantPropio, false, Placa, skip: 2, take: 2, ct: ct);

        total.Should().Be(5);
        totalSegunda.Should().Be(5, "el total describe el universo, no la página");
        primera.Select(i => i.ReferenceNumber).Should().ContainInOrder("R-4", "R-3");
        segunda.Select(i => i.ReferenceNumber).Should().ContainInOrder("R-2", "R-1");
        primera.Select(i => i.Id).Should().NotIntersectWith(segunda.Select(i => i.Id));
    }
}
