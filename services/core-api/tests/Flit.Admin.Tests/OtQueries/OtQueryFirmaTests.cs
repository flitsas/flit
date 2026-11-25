using Flit.Admin.Domain.OtClientProcedures;
using Flit.Admin.Domain.OtQueries;
using Flit.Analytics.Application.CompanyQueries;
using Flit.Infrastructure.Analytics.Scheduling;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Admin;
using Flit.Infrastructure.Persistence.Entities.Identity;
using Flit.Infrastructure.Persistence.Repositories;
using Flit.Queries.Domain;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Tramites.Estados;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;

namespace Flit.Admin.Tests.OtQueries;

/// <summary>
/// Épica #14666 / HU #14700 (A3) — «Firmado por todas las partes» y «Falta firmar» en el organismo:
/// catálogo de Consultas, catálogo de la bandeja, motor de Consultas y listado/contadores de la bandeja.
/// La regla es la de <see cref="ITramiteFirmaEvaluator"/> (en memoria, ADR-0092): aquí solo se prueba que
/// el organismo la aplique igual que el gestor, sin tocar las consultas anteriores.
/// </summary>
public sealed class OtQueryFirmaTests
{
    private static readonly Guid OtTenant = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid ClientTenant = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid TransitOffice = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");
    private static readonly Guid TipoId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Carla = Guid.Parse("44444444-4444-4444-4444-444444444444");

    /// <summary>Firma = documento del actor en <c>firmados</c>; rechazada si está en <c>rechazados</c>.</summary>
    private sealed class FakeEvaluator(HashSet<string> firmados, HashSet<string>? rechazados = null) : ITramiteFirmaEvaluator
    {
        public int Cargas { get; private set; }

        public Task<TramiteFirmaContexto> CargarContextoAsync(
            IReadOnlyCollection<ProcedureInstance> tramites, DateTimeOffset now, CancellationToken ct = default)
        {
            Cargas++;
            return Task.FromResult(TramiteFirmaContexto.Vacio);
        }

        public TramiteFirmaResultado Evaluar(ProcedureInstance tramite, TramiteFirmaContexto contexto, DateTimeOffset now)
        {
            var pendientes = tramite.Actors
                .Where(a => !firmados.Contains(a.DocumentNumber))
                .OrderBy(a => a.ActorType == "vendedor" ? 0 : 1).ThenBy(a => a.Ordinal)
                .Select(a => new ParteFirmaPendiente(
                    a.ActorType == "vendedor" ? "Vendedor" : "Comprador",
                    a.FullName ?? string.Empty,
                    rechazados?.Contains(a.DocumentNumber) == true))
                .ToList();
            return new TramiteFirmaResultado(pendientes.Count == 0, pendientes);
        }
    }

    // ── Catálogos ─────────────────────────────────────────────────────────────────────────────

    [Fact] // El organismo ve los campos con las mismas ayudas que el gestor, en el grupo Personas.
    public void ElCatalogoDelOrganismo_PublicaLosCamposConLasMismasAyudasQueElGestor()
    {
        foreach (var id in new[] { CompanyQueryFieldCatalog.FirmadoPorTodas, CompanyQueryFieldCatalog.FaltaFirmar })
        {
            var gestor = CompanyQueryFieldCatalog.Fields.Single(f => f.Id == id);
            var organismo = OtQueryFieldCatalog.Fields.Single(f => f.Id == id);

            organismo.Label.Should().Be(gestor.Label);
            organismo.Hint.Should().Be(gestor.Hint);
            organismo.Group.Should().Be(gestor.Group);
            organismo.Kind.Should().Be(gestor.Kind);
            organismo.Operators.Should().Equal(gestor.Operators);
            organismo.Options.Select(o => (o.Value, o.Label)).Should().Equal(gestor.Options.Select(o => (o.Value, o.Label)));
        }

        OtQueryFieldCatalog.Find(OtQueryFieldCatalog.FirmadoPorTodas)!.Operators.Should().Equal(QueryOperator.EsAlguno);
        OtQueryFieldCatalog.Find(OtQueryFieldCatalog.FaltaFirmar)!.Operators.Should().BeEmpty("es solo columna");
    }

    [Fact] // La bandeja de OT (recorte) también los publica, con la misma definición.
    public void LaBandejaDelOrganismo_PublicaLosMismosCampos()
    {
        foreach (var id in new[] { OtQueryFieldCatalog.FirmadoPorTodas, OtQueryFieldCatalog.FaltaFirmar })
        {
            OtBandejaQueryFieldCatalog.Find(id).Should().NotBeNull();
            OtBandejaQueryFieldCatalog.Find(id).Should().BeEquivalentTo(OtQueryFieldCatalog.Find(id));
        }

        OtBandejaQueryFieldCatalog.FirmadoPorTodas.Should().Be(CompanyQueryFieldCatalog.FirmadoPorTodas);
        OtBandejaQueryFieldCatalog.FaltaFirmar.Should().Be(CompanyQueryFieldCatalog.FaltaFirmar);
    }

    [Fact] // «Falta firmar» no admite filtro: una condición sobre él se descarta al normalizar.
    public void FaltaFirmarNoFiltra()
    {
        var def = OtQueryFieldCatalog.Normalize(Definir(Cond(OtQueryFieldCatalog.FaltaFirmar, "x")));
        def.Condiciones.Should().BeEmpty();
    }

    // ── Consultas del organismo ───────────────────────────────────────────────────────────────

    [Fact] // es Sí / es No con contador exacto y paridad entre filtro y columna.
    public async Task Consultas_EsSi_EsNo_ContadorExacto_YParidadConLaColumna()
    {
        var db = await SembrarAsync();
        var evaluador = new FakeEvaluator(["100", "300", "301"], ["201"]);

        var si = await RunAsync(db, evaluador, Definir(Cond(OtQueryFieldCatalog.FirmadoPorTodas, "true")));
        si!.Total.Should().Be(1);
        si.Filas.Select(f => f.ReferenceNumber).Should().Equal("REF-FIRMADO");
        si.Filas[0].FirmadoPorTodas.Should().BeTrue();
        si.Filas[0].FaltaFirmar.Should().BeEmpty();

        var no = await RunAsync(db, evaluador, Definir(Cond(OtQueryFieldCatalog.FirmadoPorTodas, "false")));
        no!.Total.Should().Be(2);
        no.Filas.Should().OnlyContain(f => f.FirmadoPorTodas == false);
        no.Filas.Single(f => f.ReferenceNumber == "REF-PARCIAL").FaltaFirmar
            .Should().Be("Vendedor: Vera Vendedora (rechazada) · Comprador: Carlos Comprador");
        no.Filas.Single(f => f.ReferenceNumber == "REF-NADIE").FaltaFirmar.Should().Be("Comprador: Nora Nadie");

        (si.Total + no.Total).Should().Be(3);
    }

    [Fact] // Mismo valor en gestor y organismo para el mismo trámite (misma regla, mismo evaluador).
    public async Task MismoValorEnGestorYOrganismoParaElMismoTramite()
    {
        var db = await SembrarAsync();
        var evaluador = new FakeEvaluator(["100", "300"], ["201"]);

        var organismo = await RunAsync(db, evaluador, Definir(Cond(OtQueryFieldCatalog.FirmadoPorTodas, "true", "false")));

        CompanyQueryResultDto gestor;
        await using (var ctx = NewContext(db))
        {
            gestor = await new CompanyQueryRepository(ctx, evaluador).ExecuteAsync(
                ClientTenant,
                new QueryRequest(
                    new QueryDefinition(
                        new QueryDateFilter(CompanyQueryDateField.Creacion, QueryRangePreset.Ultimos30),
                        [new QueryCondition(CompanyQueryFieldCatalog.FirmadoPorTodas, QueryOperator.EsAlguno, ["true", "false"])],
                        []),
                    1, 50),
                TestContext.Current.CancellationToken);
        }

        organismo!.Filas.Should().HaveCount(3);
        foreach (var fila in organismo.Filas)
        {
            var delGestor = gestor.Filas.Single(f => f.ReferenceNumber == fila.ReferenceNumber);
            fila.FirmadoPorTodas.Should().Be(delGestor.FirmadoPorTodas);
            fila.FaltaFirmar.Should().Be(delGestor.FaltaFirmar);
        }
    }

    [Fact] // Pedir la columna «Falta firmar» la llena sin filtro; sin pedirla no se calcula nada.
    public async Task LaColumnaSeLlenaSinFiltro_YSinPedirlaNoSeCalcula()
    {
        var db = await SembrarAsync();

        var conColumna = await RunAsync(db, new FakeEvaluator(["100"]), new QueryDefinition(
            new QueryDateFilter(OtQueryDateField.Radicacion, QueryRangePreset.Ultimos30),
            [],
            [OtQueryFieldCatalog.FaltaFirmar]));
        conColumna!.Filas.Single(f => f.ReferenceNumber == "REF-FIRMADO").FaltaFirmar.Should().BeEmpty();
        conColumna.Filas.Single(f => f.ReferenceNumber == "REF-NADIE").FaltaFirmar.Should().Be("Comprador: Nora Nadie");

        var sin = new FakeEvaluator([]);
        var anterior = await RunAsync(db, sin, Definir());
        sin.Cargas.Should().Be(0);
        anterior!.Total.Should().Be(3);
        anterior.Filas.Should().OnlyContain(f => f.FirmadoPorTodas == null && f.FaltaFirmar == null);
    }

    [Fact] // Una consulta guardada anterior (sin los campos nuevos) da lo mismo que antes.
    public async Task UnaConsultaAnteriorSigueIgual()
    {
        var db = await SembrarAsync();

        var resultado = await RunAsync(db, new FakeEvaluator([]), Definir(Cond(OtQueryFieldCatalog.Placa, "AAA111")));

        resultado!.Total.Should().Be(1);
        resultado.Filas.Single().ReferenceNumber.Should().Be("REF-FIRMADO");
    }

    [Fact] // Los permisos no cambian: el trámite de una empresa sin grant no entra ni en Sí ni en No.
    public async Task PermisosSinCambios_ElTramiteDeOtroOrganismoNoEntra()
    {
        var db = await SembrarAsync();

        var todos = await RunAsync(db, new FakeEvaluator(["100", "999"]),
            Definir(Cond(OtQueryFieldCatalog.FirmadoPorTodas, "true", "false")));

        todos!.Filas.Should().NotContain(f => f.ReferenceNumber == "REF-AJENO");
        todos.Total.Should().Be(3);
    }

    [Fact] // La exportación (hoja de Excel) lleva las dos columnas.
    public void LaExportacionIncluyeLasColumnas()
    {
        var fila = new OtQueryRowDto(
            Guid.NewGuid(), "REF-1", null, null, ClientTenant, "Empresa", "Matrícula", "entregado", "en_revision",
            false, false, null, null, false, null, false, [], DateTimeOffset.UtcNow, null, null, null, null, null,
            null, null, null, 0, [],
            FirmadoPorTodas: false, FaltaFirmar: "Comprador: Nora Nadie");

        var hoja = OtQueryReportColumns.BuildSheet(
            "Consulta", [OtQueryFieldCatalog.FirmadoPorTodas, OtQueryFieldCatalog.FaltaFirmar], [fila]);

        hoja.Columns.Select(c => c.Header).Should().Equal("Firmado por todas las partes", "Falta firmar");
        hoja.Rows.Should().HaveCount(1);
    }

    // ── HU #14701 (A4): el Excel de la consulta guardada del organismo ────────────────────────────

    [Fact] // Las dos columnas visibles: mismos valores que la tabla, fila por fila.
    public async Task Excel_ConAmbasColumnas_TraeLosMismosValoresQueLaTabla()
    {
        var db = await SembrarAsync();
        var def = DefinirConColumnas([], "placa", "firmado_por_todas", "falta_firmar");

        var (tabla, hoja) = await TablaYExcelAsync(db, def, ["100", "300", "301"], ["201"]);

        hoja.Headers.Should().Equal("Placa", "Firmado por todas las partes", "Falta firmar");
        hoja.Rows.Should().HaveCount(tabla.Filas.Count).And.HaveCount(3);
        foreach (var fila in tabla.Filas)
        {
            var celdas = hoja.Rows.Single(r => r[0] == fila.Placa);
            celdas[1].Should().Be(fila.FirmadoPorTodas == true ? "Sí" : "No");
            celdas[2].Should().Be(fila.FaltaFirmar ?? string.Empty);
        }

        hoja.Rows.Single(r => r[0] == "AAA111").Should().Equal("AAA111", "Sí", "");
        hoja.Rows.Single(r => r[0] == "BBB222")[2]
            .Should().Be("Vendedor: Vera Vendedora (rechazada) · Comprador: Carlos Comprador");
    }

    [Fact] // Una sola columna visible: solo esa sale.
    public async Task Excel_ConUnaSolaColumna_SoloTraeEsaColumna()
    {
        var db = await SembrarAsync();

        var (_, soloFaltaFirmar) = await TablaYExcelAsync(db, DefinirConColumnas([], "falta_firmar"), ["100"], null);
        soloFaltaFirmar.Headers.Should().Equal("Falta firmar");
        soloFaltaFirmar.Rows.Select(r => r[0]).Should().Contain("Comprador: Nora Nadie");

        var (_, soloFirmado) = await TablaYExcelAsync(db, DefinirConColumnas([], "firmado_por_todas"), ["100"], null);
        soloFirmado.Headers.Should().Equal("Firmado por todas las partes");
        soloFirmado.Rows.Select(r => r[0]).Should().BeEquivalentTo("Sí", "No", "No");
    }

    [Fact] // Filtro «es No»: el archivo trae exactamente las filas del listado (y respeta el alcance del organismo).
    public async Task Excel_ConFiltroEsNo_TraeExactamenteLasMismasFilasQueElListado()
    {
        var db = await SembrarAsync();
        var def = DefinirConColumnas(
            [Cond(OtQueryFieldCatalog.FirmadoPorTodas, "false")], "placa", "firmado_por_todas", "falta_firmar");

        var (tabla, hoja) = await TablaYExcelAsync(db, def, ["100", "300", "301"], ["201"]);

        tabla.Total.Should().Be(2);
        hoja.Rows.Select(r => r[0]).Should().BeEquivalentTo(tabla.Filas.Select(f => f.Placa));
        hoja.Rows.Select(r => r[0]).Should().NotContain("DDD444"); // otro organismo
        hoja.Rows.Should().OnlyContain(r => r[1] == "No");

        var (tablaUna, hojaUna) = await TablaYExcelAsync(db,
            DefinirConColumnas([Cond(OtQueryFieldCatalog.FirmadoPorTodas, "false")], "placa"),
            ["100", "300", "301"], ["201"]);
        hojaUna.Rows.Select(r => r[0]).Should().BeEquivalentTo(tablaUna.Filas.Select(f => f.Placa));
    }

    [Fact] // Volumen: 10.000 filas, el exportador del organismo no llama a nadie y las columnas nuevas no lo frenan >20 %.
    public async Task Excel_Volumen10000Filas_NoLlamaProveedoresYNoSubeMasDel20PorCiento()
    {
        const int N = 10_000;
        var filas = Enumerable.Range(0, N).Select(i => new OtQueryRowDto(
            Guid.NewGuid(), $"REF-{i}", $"P{i:D5}", null, ClientTenant, "Empresa", "Matrícula", "entregado",
            "en_revision", i % 7 == 0, false, null, null, false, null, false, [], DateTimeOffset.UtcNow, null, null,
            null, null, null, null, null, null, 0, [],
            FirmadoPorTodas: i % 3 == 0, FaltaFirmar: i % 3 == 0 ? string.Empty : $"Comprador: Persona {i}")).ToList();

        var repoSintetico = Substitute.For<IOtQueryRepository>();
        repoSintetico.ExecuteAsync(Arg.Any<Guid>(), Arg.Any<QueryRequest>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                var r = ci.ArgAt<QueryRequest>(1);
                return Task.FromResult<OtQueryResultDto?>(new OtQueryResultDto(
                    N, r.Page, r.PageSize, default, default, 0,
                    filas.Skip((r.Page - 1) * r.PageSize).Take(r.PageSize).ToList(), []));
            });

        var conColumnas = new SavedQueryDto(Guid.NewGuid(), "Vol", null, false,
            DefinirConColumnas([], "placa", "estado", "firmado_por_todas", "falta_firmar"), DateTimeOffset.UtcNow, null);
        var sinColumnas = conColumnas with
        {
            Definition = DefinirConColumnas([], "placa", "estado", "prioritario", "tipo_tramite"),
        };

        // Sin proveedores: el exportador solo depende del repositorio de consultas del organismo.
        typeof(OtQueryReportDocumentBuilder).GetConstructors().Single().GetParameters()
            .Select(p => p.ParameterType).Should().Equal(typeof(IOtQueryRepository));

        async Task<double> MedirAsync(SavedQueryDto saved)
        {
            repoSintetico.GetSavedByIdAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<SavedQueryDto?>(saved));
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var r = await new OtQueryReportDocumentBuilder(repoSintetico)
                .BuildAsync(OtTenant, saved.Id, TestContext.Current.CancellationToken);
            sw.Stop();
            r!.Total.Should().Be(N);
            r.Truncated.Should().BeTrue(); // tope del correo: 5.000 filas
            LeerXlsx(r.Bytes).Rows.Should().HaveCount(OtQueryReportDocumentBuilder.RowCap);
            return sw.Elapsed.TotalMilliseconds;
        }

        await MedirAsync(sinColumnas); // calentamiento
        await MedirAsync(conColumnas);
        var baseMs = new List<double>();
        var nuevoMs = new List<double>();
        for (var i = 0; i < 7; i++)
        {
            baseMs.Add(await MedirAsync(sinColumnas));
            nuevoMs.Add(await MedirAsync(conColumnas));
        }

        static double Mediana(List<double> l) => l.Min(); // el mínimo es la medida menos sensible al ruido de CI
        Mediana(nuevoMs).Should().BeLessThanOrEqualTo(Mediana(baseMs) * 1.20,
            $"base {Mediana(baseMs):F0} ms vs con columnas {Mediana(nuevoMs):F0} ms");
    }

    private static QueryDefinition DefinirConColumnas(QueryCondition[] condiciones, params string[] columnas) =>
        new(new QueryDateFilter(OtQueryDateField.Radicacion, QueryRangePreset.Ultimos30), condiciones, columnas);

    private static async Task<(OtQueryResultDto Tabla, Hoja Hoja)> TablaYExcelAsync(
        string db, QueryDefinition def, string[] firmados, string[]? rechazados)
    {
        await using var ctx = NewContext(db);
        var real = new OtQueryRepository(ctx,
            firma: new FakeEvaluator(firmados.ToHashSet(), rechazados?.ToHashSet()));
        var saved = new SavedQueryDto(Guid.NewGuid(), "Q", null, false, def, DateTimeOffset.UtcNow, null);

        var repo = Substitute.For<IOtQueryRepository>();
        repo.GetSavedByIdAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<SavedQueryDto?>(saved));
        repo.ExecuteAsync(Arg.Any<Guid>(), Arg.Any<QueryRequest>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(ci => real.ExecuteAsync(
                ci.ArgAt<Guid>(0), ci.ArgAt<QueryRequest>(1), ci.ArgAt<Guid?>(2), ci.ArgAt<CancellationToken>(3)));

        var resultado = await new OtQueryReportDocumentBuilder(repo)
            .BuildAsync(OtTenant, saved.Id, TestContext.Current.CancellationToken);
        var tabla = await real.ExecuteAsync(
            OtTenant, new QueryRequest(def, 1, 50), cancellationToken: TestContext.Current.CancellationToken);
        return (tabla!, LeerXlsx(resultado!.Bytes));
    }

    private sealed record Hoja(IReadOnlyList<string> Headers, IReadOnlyList<IReadOnlyList<string>> Rows);

    /// <summary>Lee la primera hoja (cadenas en línea, como las escribe TabularWorkbookWriter).</summary>
    private static Hoja LeerXlsx(byte[] bytes)
    {
        using var ms = new MemoryStream(bytes);
        using var doc = DocumentFormat.OpenXml.Packaging.SpreadsheetDocument.Open(ms, false);
        var sheetData = doc.WorkbookPart!.WorksheetParts.First().Worksheet
            .Descendants<DocumentFormat.OpenXml.Spreadsheet.SheetData>().First();
        var shared = doc.WorkbookPart.SharedStringTablePart?.SharedStringTable
            .Elements<DocumentFormat.OpenXml.Spreadsheet.SharedStringItem>().Select(x => x.InnerText).ToList();
        var filas = sheetData.Elements<DocumentFormat.OpenXml.Spreadsheet.Row>()
            .Select(r => (IReadOnlyList<string>)r.Elements<DocumentFormat.OpenXml.Spreadsheet.Cell>().Select(c =>
                c.DataType?.Value == DocumentFormat.OpenXml.Spreadsheet.CellValues.SharedString && shared is not null
                    ? shared[int.Parse(c.CellValue!.Text)]
                    : c.InlineString?.InnerText ?? c.CellValue?.Text ?? string.Empty).ToList())
            .ToList();
        return new Hoja(filas[0], filas.Skip(1).ToList());
    }

    // ── Bandeja del organismo ─────────────────────────────────────────────────────────────────

    [Fact] // La bandeja filtra «es Sí / es No», con total exacto, y sin la condición el listado es el de siempre.
    public async Task Bandeja_FiltraPorFirma_ConTotalExacto()
    {
        var db = await SembrarAsync();
        var evaluador = new FakeEvaluator(["100", "300", "301"], ["201"]);

        var si = await ListarAsync(db, evaluador, Cond(OtQueryFieldCatalog.FirmadoPorTodas, "true"));
        si.TotalCount.Should().Be(1);
        si.Items.Select(i => i.ReferenceNumber).Should().Equal("REF-FIRMADO");

        var no = await ListarAsync(db, evaluador, Cond(OtQueryFieldCatalog.FirmadoPorTodas, "false"));
        no.TotalCount.Should().Be(2);
        no.Items.Select(i => i.ReferenceNumber).Should().BeEquivalentTo("REF-PARCIAL", "REF-NADIE");

        // Sí y No a la vez = universo entero; también se combina con otro filtro (AND).
        (await ListarAsync(db, evaluador, Cond(OtQueryFieldCatalog.FirmadoPorTodas, "true", "false")))
            .TotalCount.Should().Be(3);
        (await ListarAsync(db, evaluador,
            Cond(OtQueryFieldCatalog.FirmadoPorTodas, "false"),
            Cond(OtBandejaQueryFieldCatalog.Placa, "CCC333"))).Items.Select(i => i.ReferenceNumber)
            .Should().Equal("REF-NADIE");

        // Sin la condición: no se evalúa firma y el listado es el de antes.
        var sin = new FakeEvaluator([]);
        (await ListarAsync(db, sin)).TotalCount.Should().Be(3);
        sin.Cargas.Should().Be(0);
    }

    [Fact] // Los contadores de la cabecera respetan el mismo filtro de firma que la tabla.
    public async Task Bandeja_LosContadoresRespetanElFiltroDeFirma()
    {
        var db = await SembrarAsync();
        var evaluador = new FakeEvaluator(["100"]);

        await using var ctx = NewContext(db);
        var repo = new OtClientProcedureRepository(
            ctx, new NullTramiteTransitionPublisher(), firma: evaluador);

        var contadores = await repo.GetBandejaCountersAsync(
            OtTenant,
            new OtClientProcedureFilter { Condiciones = [Cond(OtQueryFieldCatalog.FirmadoPorTodas, "false")] },
            cancellationToken: TestContext.Current.CancellationToken);

        contadores!.PorDecidir.Should().Be(2);
    }

    // ── Infraestructura de prueba ─────────────────────────────────────────────────────────────

    private static async Task<string> SembrarAsync()
    {
        var db = Guid.NewGuid().ToString();
        await using var ctx = NewContext(db);

        ctx.TransitOfficeProfiles.Add(new TransitOfficeProfile
        {
            Id = Guid.NewGuid(),
            TenantId = OtTenant,
            TransitOfficeId = TransitOffice,
            OperationMode = "dashboard",
            QuipuxReadOnly = false,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        ctx.TenantTransitOfficeGrants.Add(new TenantTransitOfficeGrant
        {
            Id = Guid.NewGuid(),
            TenantId = ClientTenant,
            TransitOfficeId = TransitOffice,
            IsEnabled = true,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        ctx.Users.Add(new User { Id = Carla, Email = "carla@ot.local", DisplayName = "Carla Revisora" });
        ctx.ProcedureTypes.Add(new ProcedureType
        {
            Id = TipoId,
            Code = "MATRICULA_NUEVA",
            Name = "Matrícula inicial",
            Family = "MATRICULAS",
            IsActive = true,
        });

        var firmado = Tramite(ctx, "REF-FIRMADO", "AAA111", ClientTenant, 1);
        Actor(ctx, firmado, "comprador", "Carla Compradora", "100");

        var parcial = Tramite(ctx, "REF-PARCIAL", "BBB222", ClientTenant, 2);
        Actor(ctx, parcial, "vendedor", "Vera Vendedora", "201");
        Actor(ctx, parcial, "comprador", "Carlos Comprador", "202");

        var nadie = Tramite(ctx, "REF-NADIE", "CCC333", ClientTenant, 3);
        Actor(ctx, nadie, "comprador", "Nora Nadie", "203");

        // De OTRO organismo: jamás debe aparecer en ninguna superficie (permisos sin cambios).
        var ajeno = Tramite(ctx, "REF-AJENO", "DDD444", ClientTenant, 4, Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff"));
        Actor(ctx, ajeno, "comprador", "Ana Ajena", "999");

        await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);
        return db;
    }

    private static Guid Tramite(FlitDbContext ctx, string reference, string placa, Guid tenant, int consecutivo, Guid? oficina = null)
    {
        var id = Guid.NewGuid();
        var hace3 = DateTimeOffset.UtcNow.AddDays(-3);
        ctx.ProcedureInstances.Add(new ProcedureInstance
        {
            Id = id,
            TenantId = tenant,
            ProcedureTypeId = TipoId,
            ReferenceNumber = reference,
            Consecutivo = consecutivo,
            Status = TramiteEstado.Entregado,
            Plate = placa,
            TransitOfficeId = oficina ?? TransitOffice,
            CreatedByUserId = Carla,
            CreatedAt = hace3,
            UpdatedAt = hace3,
        });
        ctx.ProcedureInstanceStatusHistories.Add(new ProcedureInstanceStatusHistory
        {
            Id = Guid.NewGuid(),
            TenantId = tenant,
            ProcedureInstanceId = id,
            ToStatus = TramiteEstado.Entregado,
            ChangedAt = hace3,
        });
        return id;
    }

    private static void Actor(FlitDbContext ctx, Guid instanceId, string rol, string nombre, string doc) =>
        ctx.ProcedureInstanceActors.Add(new ProcedureInstanceActor
        {
            Id = Guid.NewGuid(),
            TenantId = ClientTenant,
            ProcedureInstanceId = instanceId,
            ProcedureEntityId = Guid.NewGuid(),
            ActorType = rol,
            DocumentType = "CC",
            DocumentNumber = doc,
            FullName = nombre,
            PersonType = "natural",
            Ordinal = 1,
            CreatedAt = DateTimeOffset.UtcNow,
        });

    private static QueryCondition Cond(string fieldId, params string[] values) =>
        new(fieldId, QueryOperator.EsAlguno, values);

    private static QueryDefinition Definir(params QueryCondition[] condiciones) =>
        new(new QueryDateFilter(OtQueryDateField.Radicacion, QueryRangePreset.Ultimos30), condiciones, []);

    private static async Task<OtQueryResultDto?> RunAsync(
        string db, ITramiteFirmaEvaluator evaluador, QueryDefinition definition)
    {
        await using var ctx = NewContext(db);
        var repo = new OtQueryRepository(ctx, firma: evaluador);
        return await repo.ExecuteAsync(
            OtTenant, new QueryRequest(definition, 1, 50),
            cancellationToken: TestContext.Current.CancellationToken);
    }

    private static async Task<Flit.Admin.Domain.Common.PagedResult<OtClientProcedure>> ListarAsync(
        string db, ITramiteFirmaEvaluator evaluador, params QueryCondition[] condiciones)
    {
        await using var ctx = NewContext(db);
        var repo = new OtClientProcedureRepository(
            ctx, new NullTramiteTransitionPublisher(), firma: evaluador);
        return await repo.ListAsync(
            OtTenant,
            new OtClientProcedureFilter { Condiciones = condiciones, PageSize = 50 },
            cancellationToken: TestContext.Current.CancellationToken);
    }

    private static FlitDbContext NewContext(string dbName) =>
        new(new DbContextOptionsBuilder<FlitDbContext>().UseInMemoryDatabase(dbName).Options);
}
