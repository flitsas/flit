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
            Consecutivo = RadicadoFixture.ConsecutivoDe(referenceNumber),
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

    // ── El radicado casa EXACTO, leído como radicado (HU #12371 AC7) ─────────────────────────

    [Fact]
    public async Task Radicado_CasaExacto_YNoPorSubcadena()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = NewContext(nameof(Radicado_CasaExacto_YNoPorSubcadena));
        db.ProcedureInstances.AddRange(
            Tramite("FT1-0000001"), Tramite("FT1-0000010"), Tramite("FT2-0000011"), Tramite("FT1-0000100"));
        await db.SaveChangesAsync(ct);

        // Con subcadena, buscar «1» devolvería los cuatro: un resultado inútil con apariencia de
        // filtro. Quien teclea un radicado quiere ESE trámite.
        (await BuscarAsync(db, "1")).Should().BeEquivalentTo(["FT1-0000001"]);
    }

    [Theory]
    [InlineData("12")]
    [InlineData("0000012")]
    [InlineData("FT1-0000012")]
    [InlineData("ft1-0000012")]
    [InlineData("ft1 12")]
    [InlineData("  FT1-0000012 ")]
    public async Task Radicado_SeLeeComoLoEscribeElUsuario(string termino)
    {
        // El usuario lo copia de un correo (FT1-0000012), lo dicta por teléfono (12) o lo teclea a
        // medias (ft1 12): todos son el mismo trámite.
        var ct = TestContext.Current.CancellationToken;
        await using var db = NewContext($"{nameof(Radicado_SeLeeComoLoEscribeElUsuario)}-{termino}");
        db.ProcedureInstances.AddRange(Tramite("FT1-0000012"), Tramite("FT1-0000120"));
        await db.SaveChangesAsync(ct);

        (await BuscarAsync(db, termino)).Should().BeEquivalentTo(["FT1-0000012"]);
    }

    [Fact]
    public async Task Radicado_ConPrefijoDeOtraFamilia_NoCasa()
    {
        // El prefijo, cuando el usuario lo escribe, se respeta: FT2-0000012 no es la matrícula 12.
        var ct = TestContext.Current.CancellationToken;
        await using var db = NewContext(nameof(Radicado_ConPrefijoDeOtraFamilia_NoCasa));
        db.ProcedureInstances.Add(Tramite("FT1-0000012"));
        await db.SaveChangesAsync(ct);

        (await BuscarAsync(db, "FT2-0000012")).Should().BeEmpty();
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

    // ── Varios valores separados por coma (HU #14747) ─────────────────────────────────────────

    private static async Task<FlitDbContext> SembrarMultivalorAsync(string nombre)
    {
        var db = NewContext(nombre);
        db.ProcedureInstances.AddRange(
            Tramite("FT1-0000102", placa: "KYU631", vin: "LRWYGCEK7TC769623", comprador: "Juan Pérez", documento: "1020998455"),
            Tramite("FT1-0000103", placa: "SCG440", vin: "LRWYGCFJ9TC823137"),
            Tramite("FT1-0000104", placa: "BBB222", vin: "WDD2040011A000004"));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return db;
    }

    [Fact]
    public async Task AC1_Multivalor_VariasPlacas()
    {
        await using var db = await SembrarMultivalorAsync(nameof(AC1_Multivalor_VariasPlacas));
        (await BuscarAsync(db, "kyu631, scg440")).Should().BeEquivalentTo(["FT1-0000102", "FT1-0000103"]);
    }

    [Theory]
    [InlineData("scg440, FT1-0000102")]
    [InlineData("SCG440,102")]
    [InlineData("scg440 , 0000102")]
    [InlineData("scg440, ft1 102")]
    public async Task AC2_Multivalor_PlacaMasId(string texto)
    {
        await using var db = await SembrarMultivalorAsync($"{nameof(AC2_Multivalor_PlacaMasId)}-{texto}");
        (await BuscarAsync(db, texto)).Should().BeEquivalentTo(["FT1-0000102", "FT1-0000103"]);
    }

    [Fact]
    public async Task AC3_Multivalor_VinMasPlaca()
    {
        await using var db = await SembrarMultivalorAsync(nameof(AC3_Multivalor_VinMasPlaca));
        (await BuscarAsync(db, "lrwygcfj9tc823137, BBB222")).Should().BeEquivalentTo(["FT1-0000103", "FT1-0000104"]);
    }

    [Fact]
    public async Task AC4_SinComa_IgualQueAntes()
    {
        await using var db = await SembrarMultivalorAsync(nameof(AC4_SinComa_IgualQueAntes));
        (await BuscarAsync(db, "scg")).Should().BeEquivalentTo(["FT1-0000103"]);
        (await BuscarAsync(db, "769623")).Should().BeEquivalentTo(["FT1-0000102"]);
        (await BuscarAsync(db, "102")).Should().BeEquivalentTo(["FT1-0000102"]);
        (await BuscarAsync(db, "pérez")).Should().BeEquivalentTo(["FT1-0000102"]);
        (await BuscarAsync(db, "1020998455")).Should().BeEquivalentTo(["FT1-0000102"]);
    }

    [Fact]
    public async Task AC5_Multivalor_ComasDoblesYFinales_SeIgnoran()
    {
        await using var db = await SembrarMultivalorAsync(nameof(AC5_Multivalor_ComasDoblesYFinales_SeIgnoran));
        (await BuscarAsync(db, "aaa111 ,, BBB222,")).Should().BeEquivalentTo(["FT1-0000104"]);
        // Con coma siempre es multivalor, aunque quede un solo tramo: no busca por nombre.
        (await BuscarAsync(db, "pérez,")).Should().BeEmpty();
        (await BuscarAsync(db, " , ,")).Should().BeEmpty();
    }

    [Fact]
    public async Task AC6_Multivalor_UnTramoInexistente_NoEscondeALosDemas()
    {
        await using var db = await SembrarMultivalorAsync(nameof(AC6_Multivalor_UnTramoInexistente_NoEscondeALosDemas));
        (await BuscarAsync(db, "ZZZ999, KYU631, 999")).Should().BeEquivalentTo(["FT1-0000102"]);
    }

    [Fact]
    public async Task AC7_Multivalor_SinDuplicadosYTotalCuentaUnaVez()
    {
        await using var db = await SembrarMultivalorAsync(nameof(AC7_Multivalor_SinDuplicadosYTotalCuentaUnaVez));
        var (items, total) = await new ProcedureInstanceRepository(db).ListWithSummaryGraphFilteredAsync(
            TenantId, 0, 100,
            new ProcedureInstanceListFilter { Busqueda = "KYU631, LRWYGCEK7TC769623, 102, FT1-0000102, kyu631" },
            ProcedureInstanceSortBy.Default, SortDirection.Descending,
            TestContext.Current.CancellationToken);

        items.Select(i => i.ReferenceNumber).Should().Equal("FT1-0000102");
        total.Should().Be(1);
    }

    [Fact]
    public async Task AC8_Multivalor_NoBuscaPorNombreDocumentoNiCompania()
    {
        await using var db = await SembrarMultivalorAsync(nameof(AC8_Multivalor_NoBuscaPorNombreDocumentoNiCompania));
        (await BuscarAsync(db, "Juan Pérez, 1020998455")).Should().BeEmpty();
    }

    // ── Helper de lectura ─────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("KYU631")]
    [InlineData("FT1-0000102")]
    public void Helper_SinComa_DevuelveNulo(string? texto) =>
        ProcedureInstanceFiltroSql.LeerBusquedaMultivalor(texto).Should().BeNull();

    [Fact]
    public void Helper_ConComa_RecortaDescartaVaciosYRepetidos()
    {
        var tramos = ProcedureInstanceFiltroSql.LeerBusquedaMultivalor(" kyu631 ,, FT1-102 , 12, KYU631,")!;

                tramos.Select(t => t.Mayusculas).Should().Equal("KYU631", "FT1-102", "12");
        tramos[0].Should().Be(new ProcedureInstanceFiltroSql.TramoBusqueda(null, null, "KYU631"));
        tramos[1].Should().Be(new ProcedureInstanceFiltroSql.TramoBusqueda(null, "FT10000102", "FT1-102"));
        tramos[2].Should().Be(new ProcedureInstanceFiltroSql.TramoBusqueda(12, null, "12"));
    }

    [Fact]
    public void Helper_UnSoloTramoConComa_SigueSiendoMultivalor() =>
        ProcedureInstanceFiltroSql.LeerBusquedaMultivalor("kyu631,")!.Should().HaveCount(1);

    [Fact]
    public void Helper_SoloComas_DevuelveListaVacia() =>
        ProcedureInstanceFiltroSql.LeerBusquedaMultivalor(", ,,")!.Should().BeEmpty();

    [Fact]
    public void Helper_LimitaLosTramos()
    {
        var texto = string.Join(",", Enumerable.Range(1, 120).Select(i => $"P{i:000}"));
        ProcedureInstanceFiltroSql.LeerBusquedaMultivalor(texto)!
            .Should().HaveCount(ProcedureInstanceFiltroSql.MaxTramosBusqueda);
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
