using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Identity;
using Flit.Infrastructure.Persistence.Repositories;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Estados;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Flit.Infrastructure.Tests.Persistence;

/// <summary>
/// HU #12187 — la búsqueda de texto libre resuelta en la consulta.
///
/// <para>Hasta ahora este cruce se hacía en el navegador sobre las filas ya traídas, y el listado
/// trae como mucho una página: buscar un trámite que existe pero quedó fuera respondía «sin
/// resultados». Estas pruebas fijan el alcance de la coincidencia y, sobre todo, que el
/// <b>radicado casa exacto</b>: con un consecutivo numérico corto, la subcadena convierte cualquier
/// búsqueda en medio listado.</para>
/// </summary>
public sealed class BusquedaTextoLibreRepositoryTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid OtroTenantId = Guid.NewGuid();
    private static readonly DateTimeOffset Base = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);

    private static FlitDbContext NewContext(string dbName) =>
        new(new DbContextOptionsBuilder<FlitDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options);

    private static ProcedureInstance Tramite(
        string referenceNumber,
        Guid? tenantId = null,
        string? placa = null,
        string? vin = null,
        string? comprador = null,
        string? vendedor = null,
        string? documento = null,
        string? organismo = null,
        bool prioritario = false)
    {
        var instancia = new ProcedureInstance
        {
            ProcedureType = ProcedureTypeFixture.Traspaso,
            Id = Guid.NewGuid(),
            TenantId = tenantId ?? TenantId,
            ProcedureTypeId = Guid.NewGuid(),
            ReferenceNumber = referenceNumber,
            Status = TramiteEstado.Borrador,
            Plate = placa,
            Vin = vin,
            CompradorNombre = comprador,
            VendedorNombre = vendedor,
            CreatedByUserId = Guid.NewGuid(),
            CreatedAt = Base,
            Prioritario = prioritario,
        };

        if (documento is not null)
        {
            instancia.Actors.Add(new ProcedureInstanceActor
            {
                Id = Guid.NewGuid(),
                ActorType = "comprador",
                FullName = comprador ?? "Parte",
                DocumentNumber = documento,
            });
        }

        if (organismo is not null)
        {
            instancia.FieldValues.Add(new ProcedureInstanceFieldValue
            {
                Id = Guid.NewGuid(),
                FieldKey = "transit_office_name",
                ValueText = organismo,
            });
        }

        return instancia;
    }

    private static async Task<IReadOnlyList<string>> BuscarAsync(
        FlitDbContext db, string? texto, bool? prioritario = null, Guid? tenantId = null)
    {
        var (items, _) = await new ProcedureInstanceRepository(db).ListWithSummaryGraphFilteredAsync(
            tenantId ?? TenantId, 0, 100,
            new ProcedureInstanceListFilter { Busqueda = texto, Prioritario = prioritario },
            ProcedureInstanceSortBy.Default, SortDirection.Descending,
            TestContext.Current.CancellationToken);

        return items.Select(i => i.ReferenceNumber).ToList();
    }

    // ── El radicado casa EXACTO ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Radicado_CasaExacto_YNoPorSubcadena()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = NewContext(nameof(Radicado_CasaExacto_YNoPorSubcadena));
        db.ProcedureInstances.AddRange(Tramite("1"), Tramite("10"), Tramite("11"), Tramite("100"));
        await db.SaveChangesAsync(ct);

        // Con subcadena, buscar «1» devolvería los cuatro: un resultado inútil con apariencia de
        // filtro. Quien teclea un radicado quiere ESE trámite.
        (await BuscarAsync(db, "1")).Should().BeEquivalentTo(["1"]);
    }

    [Fact]
    public async Task Radicado_ConEspaciosAlrededor_SigueCasando()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = NewContext(nameof(Radicado_ConEspaciosAlrededor_SigueCasando));
        db.ProcedureInstances.Add(Tramite("4571"));
        await db.SaveChangesAsync(ct);

        (await BuscarAsync(db, "  4571 ")).Should().BeEquivalentTo(["4571"]);
    }

    // ── Alcance de la coincidencia ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Placa_CasaPorSubcadenaSinImportarMayusculas()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = NewContext(nameof(Placa_CasaPorSubcadenaSinImportarMayusculas));
        db.ProcedureInstances.AddRange(Tramite("1", placa: "KYU631"), Tramite("2", placa: "SCG440"));
        await db.SaveChangesAsync(ct);

        (await BuscarAsync(db, "kyu")).Should().BeEquivalentTo(["1"]);
    }

    [Fact]
    public async Task Vin_CasaPorSubcadena()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = NewContext(nameof(Vin_CasaPorSubcadena));
        db.ProcedureInstances.AddRange(
            Tramite("1", vin: "LRWYGCEK7TC769623"),
            Tramite("2", vin: "LRWYGCFJ9TC823137"));
        await db.SaveChangesAsync(ct);

        (await BuscarAsync(db, "769623")).Should().BeEquivalentTo(["1"]);
    }

    [Fact]
    public async Task NombreDeCompradorYDeVendedor_Casan()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = NewContext(nameof(NombreDeCompradorYDeVendedor_Casan));
        db.ProcedureInstances.AddRange(
            Tramite("1", comprador: "Laura Restrepo Ossa"),
            Tramite("2", vendedor: "Comercializadora del Norte S.A.S"),
            Tramite("3", comprador: "Juan Pérez"));
        await db.SaveChangesAsync(ct);

        (await BuscarAsync(db, "restrepo")).Should().BeEquivalentTo(["1"]);
        (await BuscarAsync(db, "norte")).Should().BeEquivalentTo(["2"]);
    }

    [Fact]
    public async Task DocumentoDeUnaParte_Casa()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = NewContext(nameof(DocumentoDeUnaParte_Casa));
        db.ProcedureInstances.AddRange(
            Tramite("1", comprador: "Laura", documento: "1020998455"),
            Tramite("2", comprador: "Juan", documento: "900412887"));
        await db.SaveChangesAsync(ct);

        (await BuscarAsync(db, "1020998455")).Should().BeEquivalentTo(["1"]);
    }

    [Fact]
    public async Task OrganismoDeTransito_Casa()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = NewContext(nameof(OrganismoDeTransito_Casa));
        db.ProcedureInstances.AddRange(
            Tramite("1", organismo: "Tránsito de Funza"),
            Tramite("2", organismo: "Tránsito de Sabaneta"));
        await db.SaveChangesAsync(ct);

        (await BuscarAsync(db, "funza")).Should().BeEquivalentTo(["1"]);
    }

    [Fact]
    public async Task RazonSocialDeLaCompania_Casa()
    {
        // El listado del SuperAdmin muestra la columna Compañía, y el buscador del cliente ya la
        // incluía. Perderla al subir la búsqueda al servidor habría sido una regresión silenciosa.
        var ct = TestContext.Current.CancellationToken;
        await using var db = NewContext(nameof(RazonSocialDeLaCompania_Casa));
        db.Tenants.Add(new Tenant
        {
            Id = TenantId,
            Code = "RENT",
            LegalName = "Renting Colombia S.A.S",
            TaxId = "900123456",
            TenantType = "COMPANY",
        });
        db.ProcedureInstances.Add(Tramite("1"));
        await db.SaveChangesAsync(ct);

        (await BuscarAsync(db, "renting")).Should().BeEquivalentTo(["1"]);
    }

    [Fact]
    public async Task TextoQueNoCasaConNada_DevuelveVacio()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = NewContext(nameof(TextoQueNoCasaConNada_DevuelveVacio));
        db.ProcedureInstances.Add(Tramite("1", placa: "KYU631"));
        await db.SaveChangesAsync(ct);

        (await BuscarAsync(db, "zzzz")).Should().BeEmpty();
    }

    // ── Convivencia y aislamiento ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task NoAtraviesaElAislamientoPorCompania()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = NewContext(nameof(NoAtraviesaElAislamientoPorCompania));
        db.ProcedureInstances.AddRange(
            Tramite("1", placa: "KYU631"),
            Tramite("2", tenantId: OtroTenantId, placa: "KYU631"));
        await db.SaveChangesAsync(ct);

        (await BuscarAsync(db, "KYU631")).Should().BeEquivalentTo(["1"]);
    }

    [Fact]
    public async Task ConvieneConOtroFiltro_SeCumplenLosDos()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = NewContext(nameof(ConvieneConOtroFiltro_SeCumplenLosDos));
        db.ProcedureInstances.AddRange(
            Tramite("1", comprador: "Laura Restrepo", prioritario: true),
            Tramite("2", comprador: "Laura Gómez"));
        await db.SaveChangesAsync(ct);

        (await BuscarAsync(db, "laura", prioritario: true)).Should().BeEquivalentTo(["1"]);
    }

    [Fact]
    public async Task SinTexto_NoFiltraNada()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = NewContext(nameof(SinTexto_NoFiltraNada));
        db.ProcedureInstances.AddRange(Tramite("1"), Tramite("2"));
        await db.SaveChangesAsync(ct);

        (await BuscarAsync(db, null)).Should().HaveCount(2);
        (await BuscarAsync(db, "   ")).Should().HaveCount(2);
    }

    [Fact]
    public async Task SoloPrioritarios_FiltraEnLaConsulta()
    {
        // Este filtro también vivía en el cliente. Con paginación real habría pasado de mirar la
        // ventana de 200 a mirar solo las diez filas de la página a la vista: habría EMPEORADO.
        var ct = TestContext.Current.CancellationToken;
        await using var db = NewContext(nameof(SoloPrioritarios_FiltraEnLaConsulta));
        db.ProcedureInstances.AddRange(
            Tramite("1", prioritario: true),
            Tramite("2"),
            Tramite("3", prioritario: true));
        await db.SaveChangesAsync(ct);

        (await BuscarAsync(db, null, prioritario: true)).Should().BeEquivalentTo(["1", "3"]);
    }
}
