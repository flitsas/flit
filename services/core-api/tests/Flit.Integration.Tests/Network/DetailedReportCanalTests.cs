using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Flit.Analytics.Application.Dtos;
using Flit.Analytics.Application.Queries.Network;
using Flit.Infrastructure.Documents;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Repositories;
using Flit.Integration.Tests.Postgres;
using Flit.Integration.Tests.Tenancy;
using Flit.Tramites.Domain.Tramites.Enums;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Flit.Integration.Tests.Network;

/// <summary>
/// HU #14319 (Feature #14316, Épica #14296) y HU #14320 — canal en el reporte detallado y modo «todas las compañías»,
/// contra PostgreSQL real sobre <see cref="HierarchyScenario"/>. Nadie asigna canal todavía (HU #14318), así que las
/// pruebas lo siembran directo en <c>tramites.procedure_instances</c>:
/// <list type="bullet">
///   <item>P: entregado sin canal (histórico) y borrador sin canal migrado de FLIT 1.0.</item>
///   <item>C1: entregado <c>pn_web</c>; borrador <c>pn_punto</c> del organismo Ot1.</item>
///   <item>C2: entregado <c>pn_punto</c> del organismo Ot2; borrador <c>integracion</c> (ICT).</item>
///   <item>X: entregado <c>tramites</c>; borrador <c>pn_web</c>. S: sin canal.</item>
/// </list>
/// Nadie tiene <c>aplicacion</c>. Cubre #14319 AC1–AC6 y AC8 (ruta de red y Excel); AC7 y AC9 en
/// <c>DetailedReportCanalEndpointsTests</c> (capa HTTP); #14320 AC1–AC3 (AC4/AC5 en la capa HTTP).
/// </summary>
public sealed class DetailedReportCanalTests(PostgresDatabaseFixture fixture) : PostgresTestBase(fixture)
{
    private static readonly DateOnly From = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-10));
    private static readonly DateOnly To = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1));

    private static readonly IReadOnlySet<Guid> Red = new HashSet<Guid> { HierarchyScenario.P, HierarchyScenario.C1, HierarchyScenario.C2 };

    // ── #14319 AC1 / AC2 — fila con canal e históricos ───────────────────────────────────────

    [PostgresFact]
    public async Task AC1_AC2_cada_fila_trae_el_canal_y_el_OT_y_los_historicos_incluido_el_migrado_son_Tramites()
    {
        await SeedAsync();
        await using var ctx = NewContext();
        var viejo = new DetailedReportReadRepository(ctx);

        var p = (await viejo.GetProceduresAsync(OldFilter(HierarchyScenario.P), 1, 100)).Items;
        p.Should().HaveCount(2).And.OnlyContain(r => r.Channel == TramiteCanal.Tramites && r.ChannelTransitOfficeId == null && r.ChannelTransitOfficeName == null,
            "una fila sin canal, incluida la migrada de FLIT 1.0, se reporta como Trámites");

        var c1 = (await viejo.GetProceduresAsync(OldFilter(HierarchyScenario.C1), 1, 100)).Items;
        c1.Single(r => r.Id == HierarchyScenario.DeliveredProcedureOf(HierarchyScenario.C1)).Should().Match<DetailedProcedureRowDto>(
            r => r.Channel == TramiteCanal.PersonaNaturalWeb && r.ChannelTransitOfficeId == null && r.ChannelTransitOfficeName == null);
        var punto = c1.Single(r => r.Id == HierarchyScenario.DraftProcedureOf(HierarchyScenario.C1));
        punto.Channel.Should().Be(TramiteCanal.PersonaNaturalOt);
        punto.ChannelTransitOfficeId.Should().Be(HierarchyScenario.Ot1);
        punto.ChannelTransitOfficeName.Should().Be(await OfficeNameAsync(HierarchyScenario.Ot1));

        // El migrado de verdad está marcado como tal y sin canal.
        await using var check = NewContext();
        var migrado = await check.ProcedureInstances.AsNoTracking().SingleAsync(p0 => p0.Id == HierarchyScenario.DraftProcedureOf(HierarchyScenario.P));
        migrado.IsMigrated.Should().BeTrue();
        migrado.Channel.Should().BeNull();
    }

    // ── #14319 AC3 — ICT solo al filtrar Integración ─────────────────────────────────────────

    [PostgresTheory]
    [InlineData(TramiteCanal.Tramites)]
    [InlineData(TramiteCanal.PersonaNaturalWeb)]
    [InlineData(TramiteCanal.PersonaNaturalOt)]
    public async Task AC3_el_trámite_de_integracion_no_aparece_al_filtrar_otro_canal(string canal)
    {
        await SeedAsync();
        await using var ctx = NewContext();
        var ict = HierarchyScenario.DraftProcedureOf(HierarchyScenario.C2);

        var viejo = await new DetailedReportReadRepository(ctx).GetProceduresAsync(OldFilter(HierarchyScenario.C2) with { Channel = canal }, 1, 100);
        var red = await new DetailedReportNetworkReadRepository(ctx).GetNetworkProceduresAsync(NetFilter(Red) with { Channel = canal }, 1, 100);

        viejo.Items.Select(r => r.Id).Should().NotContain(ict);
        red.Items.Items.Select(r => r.Id).Should().NotContain(ict);
    }

    [PostgresFact]
    public async Task AC3_al_filtrar_Integracion_si_aparece()
    {
        await SeedAsync();
        await using var ctx = NewContext();

        var viejo = await new DetailedReportReadRepository(ctx).GetProceduresAsync(OldFilter(HierarchyScenario.C2) with { Channel = TramiteCanal.Integracion }, 1, 100);
        var red = await new DetailedReportNetworkReadRepository(ctx).GetNetworkProceduresAsync(NetFilter(Red) with { Channel = TramiteCanal.Integracion }, 1, 100);

        viejo.Items.Should().ContainSingle().Which.Id.Should().Be(HierarchyScenario.DraftProcedureOf(HierarchyScenario.C2));
        red.Items.Items.Should().ContainSingle().Which.Channel.Should().Be(TramiteCanal.Integracion);
    }

    // ── #14319 AC4 — filtro por canal; mismo predicado en listado, conteo, resumen y export ──

    [PostgresFact]
    public async Task AC4_filtrar_por_PersonaNaturalWeb_solo_trae_ese_canal_en_listado_conteo_resumen_y_export()
    {
        await SeedAsync();
        await using var ctx = NewContext();
        var filtro = OldFilter(HierarchyScenario.C1) with { Channel = TramiteCanal.PersonaNaturalWeb };
        var viejo = new DetailedReportReadRepository(ctx);

        var pagina = await viejo.GetProceduresAsync(filtro, 1, 100);
        var exportadas = new List<DetailedProcedureRowDto>();
        await viejo.ExportProceduresAsync(filtro, (r, _) => { exportadas.Add(r); return Task.CompletedTask; });

        pagina.Items.Should().ContainSingle().Which.Id.Should().Be(HierarchyScenario.DeliveredProcedureOf(HierarchyScenario.C1));
        pagina.TotalCount.Should().Be(1);
        pagina.Summary.TotalCount.Should().Be(1);
        pagina.Summary.ByStatus.Should().ContainSingle();
        exportadas.Select(r => r.Id).Should().Equal(pagina.Items.Select(i => i.Id));

        // «Trámites» sobre P (históricos): ambos, porque NULL es Trámites.
        var tramites = await viejo.GetProceduresAsync(OldFilter(HierarchyScenario.P) with { Channel = TramiteCanal.Tramites }, 1, 100);
        tramites.TotalCount.Should().Be(2);

        // En la red, a través de varios clientes: pn_web = C1 entregado + X borrador no (X no está en el conjunto).
        var red = await new DetailedReportNetworkReadRepository(ctx).GetNetworkProceduresAsync(NetFilter(Red) with { Channel = TramiteCanal.PersonaNaturalWeb }, 1, 100);
        red.Items.Items.Should().ContainSingle().Which.TenantId.Should().Be(HierarchyScenario.C1);
        red.Items.Summary.TotalCount.Should().Be(1);
        red.Items.Summary.ByTenant.Should().ContainSingle();
    }

    // ── #14319 AC5 — filtro por OT del canal ─────────────────────────────────────────────────

    [PostgresFact]
    public async Task AC5_filtrar_por_el_OT_del_canal_solo_trae_los_de_ese_organismo()
    {
        await SeedAsync();
        await using var ctx = NewContext();
        var red = new DetailedReportNetworkReadRepository(ctx);

        var ot1 = await red.GetNetworkProceduresAsync(NetFilter(Red) with { Channel = TramiteCanal.PersonaNaturalOt, ChannelTransitOfficeId = HierarchyScenario.Ot1 }, 1, 100);
        var ot2 = await red.GetNetworkProceduresAsync(NetFilter(Red) with { Channel = TramiteCanal.PersonaNaturalOt, ChannelTransitOfficeId = HierarchyScenario.Ot2 }, 1, 100);
        var soloOt = await red.GetNetworkProceduresAsync(NetFilter(Red) with { ChannelTransitOfficeId = HierarchyScenario.Ot2 }, 1, 100);

        ot1.Items.Items.Should().ContainSingle().Which.Id.Should().Be(HierarchyScenario.DraftProcedureOf(HierarchyScenario.C1));
        ot2.Items.Items.Should().ContainSingle().Which.Id.Should().Be(HierarchyScenario.DeliveredProcedureOf(HierarchyScenario.C2));
        soloOt.Items.Items.Select(i => i.Id).Should().Equal(ot2.Items.Items.Select(i => i.Id), "el OT por sí solo ya acota");
        ot1.Items.Items[0].ChannelTransitOfficeName.Should().Be(await OfficeNameAsync(HierarchyScenario.Ot1));
    }

    // ── #14319 AC6 — Aplicación vacío sin error ──────────────────────────────────────────────

    [PostgresFact]
    public async Task AC6_filtrar_por_Aplicacion_responde_vacio_sin_error_en_todas_las_rutas()
    {
        await SeedAsync();
        await using var ctx = NewContext();

        var viejo = await new DetailedReportReadRepository(ctx).GetProceduresAsync(OldFilter(HierarchyScenario.C1) with { Channel = TramiteCanal.Aplicacion }, 1, 100);
        var red = await new DetailedReportNetworkReadRepository(ctx).GetNetworkProceduresAsync(NetFilter(Red) with { Channel = TramiteCanal.Aplicacion }, 1, 100);

        viejo.Items.Should().BeEmpty();
        viejo.TotalCount.Should().Be(0);
        viejo.Summary.TotalCount.Should().Be(0);
        red.Items.Items.Should().BeEmpty();
        red.Items.TotalCount.Should().Be(0);
        red.ReachedTenantIds.Should().BeEmpty();
    }

    // ── #14319 AC8 — red y Excel traen el mismo canal y OT y respetan el filtro ──────────────

    [PostgresFact]
    public async Task AC8_el_Excel_de_red_y_el_de_siempre_traen_Canal_y_OT_y_respetan_el_filtro()
    {
        await SeedAsync();
        await using var ctx = NewContext();
        var nombreOt1 = await OfficeNameAsync(HierarchyScenario.Ot1);
        var filtroRed = NetFilter(Red) with { Channel = TramiteCanal.PersonaNaturalOt };

        var listado = await new DetailedReportNetworkReadRepository(ctx).GetNetworkProceduresAsync(filtroRed, 1, 100);
        var exporter = new DetailedReportExcelExporter(new DetailedReportReadRepository(ctx), new DetailedReportNetworkReadRepository(ctx));

        using var redOut = new MemoryStream();
        await exporter.ExportNetworkAsync(redOut, filtroRed);
        var hojaRed = ReadSheet(redOut.ToArray());
        hojaRed[0].Should().Equal(DetailedReportExcelExporter.NetworkHeaders);
        hojaRed[0].TakeLast(2).Should().Equal("Canal", "OT del canal");
        hojaRed.Skip(1).Should().HaveCount(listado.Items.TotalCount).And.HaveCount(2, "C1 borrador (Ot1) y C2 entregado (Ot2)");
        hojaRed.Skip(1).Select(r => r[^2]).Should().OnlyContain(c => c == TramiteCanal.NombreVisible(TramiteCanal.PersonaNaturalOt));
        hojaRed.Skip(1).Select(r => r[^1]).Should().Contain(nombreOt1);

        using var viejoOut = new MemoryStream();
        await exporter.ExportAsync(viejoOut, OldFilter(HierarchyScenario.C1) with { Channel = TramiteCanal.PersonaNaturalOt });
        var hojaViejo = ReadSheet(viejoOut.ToArray());
        hojaViejo[0].TakeLast(2).Should().Equal("Canal", "OT del canal");
        var fila = hojaViejo.Skip(1).Should().ContainSingle().Subject;
        fila[^2].Should().Be("Persona Natural OT");
        fila[^1].Should().Be(nombreOt1);

        // Canal sin dato: Trámites en la celda, OT vacío.
        using var historicoOut = new MemoryStream();
        await exporter.ExportAsync(historicoOut, OldFilter(HierarchyScenario.P));
        ReadSheet(historicoOut.ToArray()).Skip(1).Should().OnlyContain(r => r[r.Count - 2] == "Trámites" && r[r.Count - 1] == string.Empty);
    }

    // ── #14320 AC1 / AC2 / AC3 — todas las compañías ─────────────────────────────────────────

    [PostgresFact]
    public async Task H14320_AC1_todas_las_companias_trae_trámites_de_todas_con_el_nombre_de_la_compania_por_fila()
    {
        await SeedAsync();
        await using var ctx = NewContext();
        var repo = new DetailedReportNetworkReadRepository(ctx);

        var (resultado, error) = await new GetAllCompaniesDetailedProceduresHandler(repo)
            .HandleAsync(new NetworkDetailedReportRequest(null, From, To), 1, 100);

        error.Should().BeNull();
        var items = resultado!.Response.Items;
        items.Should().HaveCount(10, "dos trámites por cada uno de los cinco clientes, entre ellos X y S que no son de la red de P");
        items.Select(i => i.TenantId).Distinct().Should().BeEquivalentTo(HierarchyScenario.Clients);
        items.Should().OnlyContain(i => i.TenantName == $"Cliente de integración IT-{HierarchyScenario.CodeOf(i.TenantId)}");
        resultado.Response.TotalCount.Should().Be(10);
        resultado.Response.Summary.ByTenant.Should().HaveCount(5);
        resultado.ReachedTenantIds.Should().BeEquivalentTo(HierarchyScenario.Clients);
        (await repo.ListAllTenantIdsAsync()).Should().Contain(HierarchyScenario.Clients);
    }

    [PostgresFact]
    public async Task H14320_AC2_el_filtro_por_canal_entre_empresas_trae_ese_canal_de_cualquier_compania()
    {
        await SeedAsync();
        await using var ctx = NewContext();

        var (resultado, error) = await new GetAllCompaniesDetailedProceduresHandler(new DetailedReportNetworkReadRepository(ctx))
            .HandleAsync(new NetworkDetailedReportRequest(null, From, To) { Channel = TramiteCanal.PersonaNaturalWeb }, 1, 100);

        error.Should().BeNull();
        resultado!.Response.Items.Select(i => (i.TenantId, i.Id)).Should().BeEquivalentTo(
        [
            (HierarchyScenario.C1, HierarchyScenario.DeliveredProcedureOf(HierarchyScenario.C1)),
            (HierarchyScenario.X, HierarchyScenario.DraftProcedureOf(HierarchyScenario.X)),
        ]);
        resultado.ReachedTenantIds.Should().BeEquivalentTo([HierarchyScenario.C1, HierarchyScenario.X]);
    }

    [PostgresFact]
    public async Task H14320_AC3_el_Excel_trae_las_mismas_filas_que_el_listado_e_incluye_la_compania()
    {
        await SeedAsync();
        await using var ctx = NewContext();
        var repo = new DetailedReportNetworkReadRepository(ctx);
        var request = new NetworkDetailedReportRequest(null, From, To) { Channel = TramiteCanal.Tramites };

        var (listFilter, _) = await new ExportAllCompaniesDetailedProceduresHandler(repo).ResolveAsync(request);
        var (listado, _) = await new GetAllCompaniesDetailedProceduresHandler(repo).HandleAsync(request, 1, 100);
        var exporter = new DetailedReportExcelExporter(new DetailedReportReadRepository(ctx), repo);
        using var salida = new MemoryStream();
        var alcanzadas = await exporter.ExportNetworkAsync(salida, listFilter!);
        var hoja = ReadSheet(salida.ToArray());

        hoja[0][0].Should().Be(DetailedReportExcelExporter.NetworkCompanyHeader);
        hoja.Skip(1).Select(r => r[1]).Should().Equal(listado!.Response.Items.Select(i => i.ReferenceNumber), "mismo conjunto y mismo orden");
        hoja.Skip(1).Select(r => r[0]).Should().Equal(listado.Response.Items.Select(i => i.TenantName));
        alcanzadas.Should().BeEquivalentTo(listado.ReachedTenantIds);
        // «Trámites» no deja entrar pn_web, pn_punto ni integración, de ninguna compañía.
        listado.Response.Items.Should().NotContain(i => i.Channel != TramiteCanal.Tramites);
    }

    [PostgresFact]
    public async Task H14320_el_rango_invalido_se_rechaza_sin_consultar_y_un_universo_vacio_no_es_sin_filtro()
    {
        await SeedAsync();
        await using var ctx = NewContext();
        var repo = new DetailedReportNetworkReadRepository(ctx);

        var (r, error) = await new GetAllCompaniesDetailedProceduresHandler(repo)
            .HandleAsync(new NetworkDetailedReportRequest(null, To, From), 1, 100);
        r.Should().BeNull();
        error.Should().Be(NetworkAnalyticsScope.InvalidRange);

        var vacio = await repo.GetNetworkProceduresAsync(NetFilter(new HashSet<Guid>()), 1, 100);
        vacio.Items.Items.Should().BeEmpty();
    }

    // ── helpers ──────────────────────────────────────────────────────────────────────────────

    private async Task SeedAsync()
    {
        await HierarchyScenario.SeedAsync(Fixture);
        await using var ctx = NewContext();
        await Set(ctx, HierarchyScenario.DraftProcedureOf(HierarchyScenario.P), null, null, migrado: true);
        await Set(ctx, HierarchyScenario.DeliveredProcedureOf(HierarchyScenario.C1), TramiteCanal.PersonaNaturalWeb, null);
        await Set(ctx, HierarchyScenario.DraftProcedureOf(HierarchyScenario.C1), TramiteCanal.PersonaNaturalOt, HierarchyScenario.Ot1);
        await Set(ctx, HierarchyScenario.DeliveredProcedureOf(HierarchyScenario.C2), TramiteCanal.PersonaNaturalOt, HierarchyScenario.Ot2);
        await Set(ctx, HierarchyScenario.DraftProcedureOf(HierarchyScenario.C2), TramiteCanal.Integracion, null);
        await Set(ctx, HierarchyScenario.DeliveredProcedureOf(HierarchyScenario.X), TramiteCanal.Tramites, null);
        await Set(ctx, HierarchyScenario.DraftProcedureOf(HierarchyScenario.X), TramiteCanal.PersonaNaturalWeb, null);
    }

    /// <summary>El canal es inmutable para EF (HU #14317): la prueba lo siembra directo en la columna, parametrizado.</summary>
    private static async Task Set(FlitDbContext ctx, Guid id, string? canal, Guid? ot, bool migrado = false) =>
        await ctx.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE tramites.procedure_instances SET channel = {canal}, channel_transit_office_id = {ot}, is_migrated = {migrado} WHERE id = {id}");

    private async Task<string> OfficeNameAsync(Guid id)
    {
        await using var ctx = NewContext();
        return await ctx.TransitOffices.AsNoTracking().Where(o => o.Id == id).Select(o => o.Name).SingleAsync();
    }

    private static NetworkDetailedReportFilter NetFilter(IReadOnlySet<Guid> tenants) =>
        new(tenants, From, To, null, null, null, null, null, null, null, null, null);

    private static DetailedReportFilter OldFilter(Guid tenant) =>
        new(tenant, From, To, null, null, null, null, null, null, null, null, null);

    private static List<List<string>> ReadSheet(byte[] xlsx)
    {
        using var stream = new MemoryStream(xlsx);
        using var document = SpreadsheetDocument.Open(stream, false);
        var sheet = document.WorkbookPart!.WorksheetParts.Single().Worksheet.GetFirstChild<SheetData>()!;
        return sheet.Elements<Row>()
            .Select(r => r.Elements<Cell>().Select(c => c.InlineString?.Text?.Text ?? c.CellValue?.Text ?? string.Empty).ToList())
            .ToList();
    }
}
