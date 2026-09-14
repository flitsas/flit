using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Flit.Analytics.Application.Dtos;
using Flit.Analytics.Application.Queries;
using Flit.Analytics.Application.Queries.Network;
using Flit.Infrastructure.Documents;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Repositories;
using Flit.Integration.Tests.Postgres;
using Flit.Integration.Tests.Tenancy;
using Flit.Queries.Domain.Tenancy;
using Flit.Tramites.Domain.Tramites.Estados;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using NpgsqlTypes;
using Xunit;

namespace Flit.Integration.Tests.Network;

/// <summary>
/// HU #12360 (Feature #12257, épica #12235) — reporte detallado de la red contra PostgreSQL real sobre
/// <see cref="HierarchyScenario"/> (P cabeza; C1/C2 hijos; X ajeno; S aislado; dos trámites por cliente:
/// uno <c>entregado</c> y un <c>borrador</c>). Consultas Q36 (listado) y Q37 (exportación) con datos de
/// tres clientes:
/// <list type="bullet">
///   <item>AC1 — Group(P,[C1,C2]) = exactamente las filas de P, C1 y C2 (nunca X ni S), cada una con su cliente.</item>
///   <item>AC2 — <c>childTenantId = C1</c> ⇒ solo C1, y los totales son solo de C1.</item>
///   <item>AC3 — hijo ajeno ⇒ <c>network_child_out_of_scope</c> en memoria, sin conjunto ni consulta.</item>
///   <item>AC4 — conjunto vacío ⇒ 0 filas SIN ir a la base (y en la base, <c>= ANY('{}')</c> ⇒ 0).</item>
///   <item>AC5 — el XLSX exportado con un filtro contiene exactamente las filas del listado con el mismo filtro.</item>
///   <item>AC6 — ni el DTO ni el XLSX llevan documentos ni enlaces.</item>
///   <item>AC7 — S por la ruta vieja (<c>DetailedReportReadRepository</c>) obtiene lo mismo que antes.</item>
/// </list>
/// Uso de ejemplo: <c>await new DetailedReportNetworkReadRepository(ctx).GetNetworkProceduresAsync(Filter(GroupP().ReadTenantIds), 1, 100)</c>.
/// </summary>
public sealed class NetworkReportsTests(PostgresDatabaseFixture fixture) : PostgresTestBase(fixture)
{
    private static readonly DateOnly From = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-10));
    private static readonly DateOnly To = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1));
    private static readonly IReadOnlySet<Guid> Nadie = new HashSet<Guid>();

    private static TenantScope GroupP() =>
        TenantScope.Group(HierarchyScenario.P, [HierarchyScenario.C1, HierarchyScenario.C2], GroupKind.Concesion);

    // ── Q36 — listado (AC1 / AC2) ─────────────────────────────────────────────────────────────

    [PostgresFact]
    public async Task Q36_Listado_a_Group_son_las_filas_de_P_C1_y_C2_cada_una_con_su_cliente_y_nunca_X_ni_S()
    {
        await HierarchyScenario.SeedAsync(Fixture);
        await using var ctx = NewContext();
        var red = new DetailedReportNetworkReadRepository(ctx);
        var viejo = new DetailedReportReadRepository(ctx);

        var grupo = await red.GetNetworkProceduresAsync(Filter(GroupP().ReadTenantIds), 1, 100);

        var esperados = new List<Guid>();
        foreach (var t in new[] { HierarchyScenario.P, HierarchyScenario.C1, HierarchyScenario.C2 })
            esperados.AddRange((await viejo.GetProceduresAsync(OldFilter(t), 1, 100)).Items.Select(i => i.Id));

        grupo.Items.Items.Select(i => i.Id).Should().BeEquivalentTo(esperados, "la red es exactamente la unión de las rutas viejas de P, C1 y C2");
        grupo.Items.Items.Should().HaveCount(6).And.OnlyContain(i => i.TenantId == HierarchyScenario.OwnerOf(i.Id), "cada fila identifica a su dueño real");
        grupo.Items.Items.Should().OnlyContain(i => i.TenantName == $"Cliente de integración IT-{HierarchyScenario.CodeOf(i.TenantId)}");
        grupo.Items.TotalCount.Should().Be(6);
        grupo.Items.Summary.TotalCount.Should().Be(6);
        grupo.Items.Summary.ByTenant.Select(t => (t.TenantId, t.Count)).Should().BeEquivalentTo(
            [(HierarchyScenario.P, 2), (HierarchyScenario.C1, 2), (HierarchyScenario.C2, 2)]);
        grupo.Items.Summary.ByStatus.Should().BeEquivalentTo([new StatusCountDto(TramiteEstado.Entregado, 3), new StatusCountDto(TramiteEstado.Borrador, 3)]);
        grupo.Items.Scope.TenantIds.Should().BeEquivalentTo(GroupP().ReadTenantIds);
        grupo.ReachedTenantIds.Should().BeEquivalentTo([HierarchyScenario.P, HierarchyScenario.C1, HierarchyScenario.C2]);
        LeakAssert.NoForeignRows("Q36 network report Group(P)", HierarchyScenario.P, GroupP().ReadTenantIds, grupo.Items.Items, i => i.TenantId, i => i.Id);
    }

    [PostgresFact]
    public async Task Q36_Listado_b_childTenantId_C1_es_exactamente_lo_de_C1_y_los_totales_son_solo_de_C1()
    {
        await HierarchyScenario.SeedAsync(Fixture);
        await using var ctx = NewContext();
        var (filter, error) = NetworkDetailedReportScope.Resolve(GroupP(), new NetworkDetailedReportRequest(HierarchyScenario.C1, From, To));
        error.Should().BeNull();

        var soloC1 = await new DetailedReportNetworkReadRepository(ctx).GetNetworkProceduresAsync(filter!, 1, 100);
        var viejoC1 = await new DetailedReportReadRepository(ctx).GetProceduresAsync(OldFilter(HierarchyScenario.C1), 1, 100);

        soloC1.Items.Items.Select(i => i.ToRow()).Should().BeEquivalentTo(viejoC1.Items, "las columnas del trámite son las de siempre");
        soloC1.Items.Items.Should().HaveCount(2).And.OnlyContain(i => i.TenantId == HierarchyScenario.C1);
        soloC1.Items.TotalCount.Should().Be(2);
        soloC1.Items.Summary.TotalCount.Should().Be(viejoC1.Summary.TotalCount);
        soloC1.Items.Summary.ByStatus.Should().BeEquivalentTo(viejoC1.Summary.ByStatus);
        soloC1.Items.Summary.ByCategory.Should().BeEquivalentTo(viejoC1.Summary.ByCategory);
        soloC1.Items.Summary.ByProcedureType.Should().BeEquivalentTo(viejoC1.Summary.ByProcedureType);
        soloC1.Items.Summary.ByTenant.Should().ContainSingle().Which.TenantId.Should().Be(HierarchyScenario.C1);
        soloC1.ReachedTenantIds.Should().Equal(HierarchyScenario.C1);

        // AC3 — hijo ajeno: rechazado en memoria, el conjunto no llega a existir ni hay consulta.
        var (ajeno, errorAjeno) = NetworkDetailedReportScope.Resolve(GroupP(), new NetworkDetailedReportRequest(HierarchyScenario.X, From, To));
        ajeno.Should().BeNull();
        errorAjeno.Should().Be(NetworkScopePolicy.ChildOutOfScope);
    }

    [PostgresFact]
    public async Task Q36_Listado_c_conjunto_vacio_devuelve_cero_filas_sin_ir_a_la_base()
    {
        await HierarchyScenario.SeedAsync(Fixture);

        await using var sinBase = UnreachableContext();
        var vacio = await new DetailedReportNetworkReadRepository(sinBase).GetNetworkProceduresAsync(Filter(Nadie), 1, 100);
        vacio.Items.Items.Should().BeEmpty();
        vacio.Items.TotalCount.Should().Be(0);
        vacio.Items.Summary.TotalCount.Should().Be(0);
        vacio.Items.Summary.ByTenant.Should().BeEmpty();
        vacio.ReachedTenantIds.Should().BeEmpty();

        // Y si el arreglo vacío llegara al motor, tampoco sería «sin filtro»: = ANY('{}'::uuid[]) ⇒ 0 filas.
        (await CountWithTenantsArrayAsync([])).Should().Be(0);
        (await CountWithTenantsArrayAsync([HierarchyScenario.P, HierarchyScenario.C1, HierarchyScenario.C2])).Should().Be(6);
        (await CountWithTenantsArrayAsync(HierarchyScenario.Clients.ToArray())).Should().Be(10, "la vista sigue teniendo a X y S");
    }

    [PostgresFact]
    public async Task Q36_Listado_d_los_filtros_de_siempre_acotan_dentro_del_conjunto_y_la_paginacion_respeta_el_orden()
    {
        await HierarchyScenario.SeedAsync(Fixture);
        await using var ctx = NewContext();
        var red = new DetailedReportNetworkReadRepository(ctx);

        var entregados = await red.GetNetworkProceduresAsync(Filter(GroupP().ReadTenantIds) with { Status = TramiteEstado.Entregado }, 1, 100);
        entregados.Items.Items.Should().HaveCount(3).And.OnlyContain(i => i.Status == TramiteEstado.Entregado);
        entregados.Items.Summary.ByTenant.Should().HaveCount(3).And.OnlyContain(t => t.Count == 1);

        var pagina = await red.GetNetworkProceduresAsync(Filter(GroupP().ReadTenantIds), 2, 4);
        pagina.Items.Items.Should().HaveCount(2, "6 filas en páginas de 4: la segunda tiene 2");
        pagina.Items.TotalCount.Should().Be(6);
        pagina.ReachedTenantIds.Should().BeEquivalentTo([HierarchyScenario.P, HierarchyScenario.C1, HierarchyScenario.C2], "los alcanzados son los del conjunto filtrado, no los de la página");

        // El radicado lo asigna un trigger (no el sembrado): se toma de la propia vista.
        var viejo = new DetailedReportReadRepository(ctx);
        var radicadoC2 = (await viejo.GetProceduresAsync(OldFilter(HierarchyScenario.C2), 1, 100)).Items[0].ReferenceNumber;
        var referencia = await red.GetNetworkProceduresAsync(Filter(GroupP().ReadTenantIds) with { ReferenceNumber = radicadoC2 }, 1, 100);
        referencia.Items.Items.Should().ContainSingle().Which.TenantId.Should().Be(HierarchyScenario.C2);

        // El filtro nunca amplía: el radicado de X con el conjunto de la red no devuelve nada.
        var radicadoX = (await viejo.GetProceduresAsync(OldFilter(HierarchyScenario.X), 1, 100)).Items[0].ReferenceNumber;
        var ajena = await red.GetNetworkProceduresAsync(Filter(GroupP().ReadTenantIds) with { ReferenceNumber = radicadoX }, 1, 100);
        ajena.Items.Items.Should().BeEmpty();
        ajena.ReachedTenantIds.Should().BeEmpty();
    }

    // ── Q37 — exportación (AC5 / AC6) ─────────────────────────────────────────────────────────

    [PostgresFact]
    public async Task Q37_Export_a_el_archivo_contiene_exactamente_las_filas_del_listado_con_el_mismo_filtro_y_ningun_ajeno()
    {
        await HierarchyScenario.SeedAsync(Fixture);
        await using var ctx = NewContext();
        var red = new DetailedReportNetworkReadRepository(ctx);
        var request = new NetworkDetailedReportRequest(null, From, To, Status: TramiteEstado.Entregado);

        // La MISMA función de resolución para el listado y la exportación (AC5).
        var (listFilter, _) = NetworkDetailedReportScope.Resolve(GroupP(), request);
        var (exportFilter, _) = ExportNetworkDetailedProceduresHandler.Validate(GroupP(), request);
        exportFilter.Should().BeEquivalentTo(listFilter);

        var listado = await red.GetNetworkProceduresAsync(listFilter!, 1, 100);
        var exportadas = new List<NetworkDetailedProcedureRowDto>();
        var reached = await red.ExportNetworkProceduresAsync(exportFilter!, (row, _) => { exportadas.Add(row); return Task.CompletedTask; });

        exportadas.Select(r => r.Id).Should().Equal(listado.Items.Items.Select(i => i.Id), "mismo conjunto y mismo orden");
        exportadas.Should().HaveCount(3).And.HaveCount(listado.Items.TotalCount);
        exportadas.Should().BeEquivalentTo(listado.Items.Items);
        reached.Should().BeEquivalentTo(listado.ReachedTenantIds);
        LeakAssert.NoForeignRows("Q37 network export Group(P)", HierarchyScenario.P, GroupP().ReadTenantIds, exportadas, r => r.TenantId, r => r.Id);
        exportadas.Should().NotContain(r => r.TenantId == HierarchyScenario.X || r.TenantId == HierarchyScenario.S);

        // Y el XLSX real (mismo generador OpenXml) lleva esas mismas filas con la columna «Compañía».
        var xlsx = await ExportXlsxAsync(ctx, exportFilter!);
        var hojas = ReadSheet(xlsx);
        hojas[0][0].Should().Be(DetailedReportExcelExporter.NetworkCompanyHeader);
        hojas[0].Should().Equal(DetailedReportExcelExporter.NetworkHeaders);
        hojas.Skip(1).Select(r => r[1]).Should().Equal(exportadas.Select(r => r.ReferenceNumber));
        hojas.Skip(1).Select(r => r[0]).Should().Equal(exportadas.Select(r => r.TenantName));
    }

    [PostgresFact]
    public async Task Q37_Export_b_childTenantId_C1_exporta_solo_C1_y_conjunto_vacio_deja_solo_la_cabecera_sin_ir_a_la_base()
    {
        await HierarchyScenario.SeedAsync(Fixture);
        await using var ctx = NewContext();
        var (filter, _) = ExportNetworkDetailedProceduresHandler.Validate(GroupP(), new NetworkDetailedReportRequest(HierarchyScenario.C1, From, To));

        var xlsx = await ExportXlsxAsync(ctx, filter!);
        var hojas = ReadSheet(xlsx);
        hojas.Should().HaveCount(3, "cabecera + los dos trámites de C1");
        hojas.Skip(1).Select(r => r[0]).Should().OnlyContain(n => n == "Cliente de integración IT-C1");
        var viejoC1 = await new DetailedReportReadRepository(ctx).GetProceduresAsync(OldFilter(HierarchyScenario.C1), 1, 100);
        hojas.Skip(1).Select(r => r[1]).Should().Equal(viejoC1.Items.Select(i => i.ReferenceNumber), "los radicados de C1 en el mismo orden que la ruta de siempre");

        // AC4 — conjunto vacío: reporte sin registros, sin consulta.
        await using var sinBase = UnreachableContext();
        var vacio = await ExportXlsxAsync(sinBase, Filter(Nadie));
        ReadSheet(vacio).Should().ContainSingle().Which.Should().Equal(DetailedReportExcelExporter.NetworkHeaders);
    }

    [PostgresFact]
    public async Task Q37_Export_c_AC6_ni_el_DTO_ni_el_XLSX_incluyen_documentos_ni_enlaces()
    {
        await HierarchyScenario.SeedAsync(Fixture);
        await using var ctx = NewContext();
        var filter = Filter(GroupP().ReadTenantIds);

        var listado = await new DetailedReportNetworkReadRepository(ctx).GetNetworkProceduresAsync(filter, 1, 100);
        var hojas = ReadSheet(await ExportXlsxAsync(ctx, filter));

        string[] prohibidas = ["preview", "download", "url", "attachment", "anexo", "enlace", "documento generado"];
        var propiedades = typeof(NetworkDetailedProcedureRowDto).GetProperties().Select(p => p.Name.ToLowerInvariant()).ToList();
        propiedades.Should().NotContain(p => prohibidas.Any(p.Contains));
        hojas[0].Select(h => h.ToLowerInvariant()).Should().NotContain(h => prohibidas.Any(h.Contains));
        hojas.SelectMany(r => r).Should().NotContain(c => c.Contains("http://", StringComparison.OrdinalIgnoreCase) || c.Contains("https://", StringComparison.OrdinalIgnoreCase));
        listado.Items.Items.Should().HaveCount(6);
        hojas.Should().HaveCount(7);
    }

    // ── AC7 — los reportes actuales no cambian ───────────────────────────────────────────────

    [PostgresFact]
    public async Task AC7_S_por_la_ruta_vieja_obtiene_lo_mismo_que_antes_y_la_consulta_nueva_sobre_S_coincide()
    {
        await HierarchyScenario.SeedAsync(Fixture);
        await using var ctx = NewContext();

        var (viejoS, error) = await new GetDetailedProceduresHandler(new DetailedReportReadRepository(ctx))
            .HandleAsync(new GetDetailedProceduresQuery(HierarchyScenario.S, From, To, null, null, null, null, null, null, null, null, null, 1, 100));
        var nuevoS = await new DetailedReportNetworkReadRepository(ctx).GetNetworkProceduresAsync(Filter(new HashSet<Guid> { HierarchyScenario.S }), 1, 100);

        error.Should().BeNull();
        viejoS!.Items.Select(i => i.Id).Should().BeEquivalentTo(HierarchyScenario.ProceduresOf(HierarchyScenario.S), "S sigue viendo sus dos trámites y solo esos");
        viejoS.TotalCount.Should().Be(2);
        viejoS.Summary.TotalCount.Should().Be(2);
        nuevoS.Items.Items.Select(i => i.ToRow()).Should().BeEquivalentTo(viejoS.Items, "la consulta nueva sobre {S} coincide con la vieja");
        LeakAssert.NoForeignProcedures("Q22 detailed-report S (ruta vieja)", HierarchyScenario.S, new HashSet<Guid> { HierarchyScenario.S }, viejoS.Items.Select(i => i.Id));

        // La firma de siempre no recibió parámetros nuevos (AC7): sigue siendo un solo tenant.
        typeof(DetailedReportFilter).GetProperties().Select(p => p.Name).Should().NotContain(["TenantIds", "ChildTenantId", "Scope"]);
        typeof(GetDetailedProceduresQuery).GetProperties().Select(p => p.Name).Should().NotContain(["TenantIds", "ChildTenantId", "Scope"]);
    }

    // ── helpers ──────────────────────────────────────────────────────────────────────────────

    private static NetworkDetailedReportFilter Filter(IReadOnlySet<Guid> tenants) =>
        new(tenants, From, To, null, null, null, null, null, null, null, null, null);

    private static DetailedReportFilter OldFilter(Guid tenant) =>
        new(tenant, From, To, null, null, null, null, null, null, null, null, null);

    private static async Task<byte[]> ExportXlsxAsync(FlitDbContext ctx, NetworkDetailedReportFilter filter)
    {
        var exporter = new DetailedReportExcelExporter(new DetailedReportReadRepository(ctx), new DetailedReportNetworkReadRepository(ctx));
        using var output = new MemoryStream();
        await exporter.ExportNetworkAsync(output, filter);
        return output.ToArray();
    }

    private static List<List<string>> ReadSheet(byte[] xlsx)
    {
        using var stream = new MemoryStream(xlsx);
        using var document = SpreadsheetDocument.Open(stream, false);
        var sheet = document.WorkbookPart!.WorksheetParts.Single().Worksheet.GetFirstChild<SheetData>()!;
        return sheet.Elements<Row>()
            .Select(r => r.Elements<Cell>().Select(c => c.InlineString?.Text?.Text ?? c.CellValue?.Text ?? string.Empty).ToList())
            .ToList();
    }

    /// <summary>Contexto cuyo servidor no existe: cualquier consulta lanza al abrir la conexión.</summary>
    private static FlitDbContext UnreachableContext()
    {
        var options = new DbContextOptionsBuilder<FlitDbContext>()
            .UseNpgsql("Host=127.0.0.1;Port=1;Database=nadie;Username=nadie;Password=nadie;Timeout=1")
            .UseSnakeCaseNamingConvention()
            .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning))
            .Options;
        return new FlitDbContext(options);
    }

    /// <summary>La misma cláusula del repositorio sobre la vista, ejecutada a mano con un <c>uuid[]</c> dado.</summary>
    private async Task<long> CountWithTenantsArrayAsync(Guid[] tenants)
    {
        await using var ctx = NewContext();
        var conn = (NpgsqlConnection)ctx.Database.GetDbConnection();
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT count(*) FROM analytics.v_procedure_detail_report v WHERE v.tenant_id = ANY(@tenants) AND v.created_at::date BETWEEN @from AND @to", conn);
        cmd.Parameters.Add(new NpgsqlParameter("tenants", NpgsqlDbType.Array | NpgsqlDbType.Uuid) { Value = tenants });
        cmd.Parameters.AddWithValue("from", From);
        cmd.Parameters.AddWithValue("to", To);
        return (long)(await cmd.ExecuteScalarAsync())!;
    }
}
