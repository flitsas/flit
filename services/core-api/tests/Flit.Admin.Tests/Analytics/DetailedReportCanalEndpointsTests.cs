using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Flit.Analytics.Application.Abstractions;
using Flit.Analytics.Application.Dtos;
using Flit.Queries.Domain.Tenancy;
using Flit.Tramites.Application.Auditing;
using Flit.Tramites.Domain.Tramites.Enums;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using NSubstitute;
using Xunit;

namespace Flit.Admin.Tests.Analytics;

/// <summary>
/// HU #14319 y HU #14320 (Feature #14316, Épica #14296) — capa HTTP del canal en Reportes Detallados y del modo
/// «todas las compañías», sin PostgreSQL (repositorios y writer de auditoría sustituidos; exportador OpenXml REAL).
/// <list type="bullet">
///   <item>#14319 AC7 — un canal fuera del catálogo responde 400 con el motivo en las cuatro rutas.</item>
///   <item>#14319 AC4/AC5 — <c>channel</c> y <c>channelTransitOfficeId</c> llegan al repositorio (de siempre y de red).</item>
///   <item>#14319 AC8 — el Excel de red trae las columnas Canal y OT del canal.</item>
///   <item>#14319 AC9 — sin permiso nuevo: con el enforcement encendido las rutas siguen pidiendo solo
///   <c>reportes.detallados.read/export</c>.</item>
///   <item>#14320 AC1/AC2 — <c>allCompanies=true</c> de SuperAdmin consulta el universo completo con cliente por fila.</item>
///   <item>#14320 AC3 — exportación con los mismos filtros e incluye la compañía.</item>
///   <item>#14320 AC4 — AdminCompany y gestor reciben 403 sin que se consulte nada ni se audite.</item>
///   <item>#14320 AC5 — el acceso (consulta y exportación) queda auditado con el writer de la red.</item>
/// </list>
/// Uso de ejemplo: <c>GET /api/v1/detailed-report/procedures?from=2026-09-01&amp;to=2026-09-14&amp;allCompanies=true</c> con token SuperAdmin.
/// </summary>
public sealed class DetailedReportCanalEndpointsTests : IClassFixture<DetailedReportCanalEndpointsTests.CanalFactory>
{
    private const string Range = "from=2026-09-01&to=2026-09-14";
    private const string Procedures = "/api/v1/detailed-report/procedures";
    private const string NetworkProcedures = "/api/v1/tramites/network/reports/procedures";
    private const string ExcelContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    private static readonly Guid P = Guid.Parse("a0000000-0000-4000-8000-000000000100");
    private static readonly Guid C1 = Guid.Parse("a0000000-0000-4000-8000-000000001100");
    private static readonly Guid C2 = Guid.Parse("a0000000-0000-4000-8000-000000001200");
    private static readonly Guid S = Guid.Parse("a0000000-0000-4000-8000-000000005500");
    private static readonly Guid Admin = Guid.Parse("a0000000-0000-4000-8000-0000000000ad");
    private static readonly Guid Office = Guid.Parse("a0000000-0000-4000-8000-0000000000f1");
    private static readonly Guid UserId = Guid.Parse("11111111-1111-1111-1111-111111111114");

    private readonly CanalFactory _factory;

    public DetailedReportCanalEndpointsTests(CanalFactory factory)
    {
        _factory = factory;
        _factory.Repo.ClearReceivedCalls();
        _factory.StdRepo.ClearReceivedCalls();
        _factory.Writer.ClearReceivedCalls();
        _factory.Writer.WriteAsync(default!, default).ReturnsForAnyArgs(true);
    }

    public static TheoryData<string, string> RutasConCanal => new()
    {
        { "SuperAdmin", $"{Procedures}?{Range}&tenantId=a0000000-0000-4000-8000-000000005500" },
        { "SuperAdmin", $"{Procedures}/export?{Range}&tenantId=a0000000-0000-4000-8000-000000005500" },
        { "AdminCompany", $"{NetworkProcedures}?{Range}" },
        { "AdminCompany", $"{NetworkProcedures}/export?{Range}" },
    };

    // ── #14319 AC7 — canal inválido ──────────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(RutasConCanal))]
    public async Task AC7_un_canal_fuera_del_catalogo_responde_400_con_el_motivo_sin_consultar(string role, string route)
    {
        var response = await ClientFor(role == "AdminCompany" ? P : Admin, role).GetAsync($"{route}&channel=web", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        body.Should().Contain("catálogo").And.Contain("pn_web", "el motivo lista los códigos válidos");
        body.Should().NotContain("web\"", "el valor recibido no se repite");
        await AssertNothingQueried();
    }

    // ── #14319 AC4 / AC5 — los filtros llegan al repositorio ─────────────────────────────────

    [Fact]
    public async Task AC4_AC5_la_ruta_de_siempre_pasa_canal_y_OT_al_filtro()
    {
        var response = await ClientFor(Admin, "SuperAdmin").GetAsync(
            $"{Procedures}?{Range}&tenantId={S}&channel=pn_punto&channelTransitOfficeId={Office}", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        await _factory.StdRepo.Received(1).GetProceduresAsync(
            Arg.Is<DetailedReportFilter>(f => f.TenantId == S && f.Channel == TramiteCanal.PersonaNaturalOt && f.ChannelTransitOfficeId == Office),
            Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var fila = body.RootElement.GetProperty("items")[0];
        fila.GetProperty("channel").GetString().Should().Be(TramiteCanal.PersonaNaturalOt);
        fila.GetProperty("channelTransitOfficeId").GetGuid().Should().Be(Office);
        fila.GetProperty("channelTransitOfficeName").GetString().Should().NotBeNullOrEmpty();
        await _factory.Repo.DidNotReceiveWithAnyArgs().GetNetworkProceduresAsync(default!, default, default, default);
    }

    [Fact]
    public async Task AC4_AC5_AC8_la_ruta_de_red_pasa_canal_y_OT_y_el_Excel_trae_Canal_y_OT_del_canal()
    {
        var client = ClientFor(P, "AdminCompany");

        (await client.GetAsync($"{NetworkProcedures}?{Range}&channel=pn_web&channelTransitOfficeId={Office}", TestContext.Current.CancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        var export = await client.GetAsync($"{NetworkProcedures}/export?{Range}&channel=pn_web&channelTransitOfficeId={Office}", TestContext.Current.CancellationToken);

        export.StatusCode.Should().Be(HttpStatusCode.OK);
        var calls = _factory.Repo.ReceivedCalls().Where(c => c.GetMethodInfo().Name is not (nameof(INetworkDetailedReportReadRepository.ListAllTenantIdsAsync) or nameof(INetworkDetailedReportReadRepository.GetNetworkMaxActoresPorRolAsync))).ToList();
        calls.Should().HaveCount(2);
        var listFilter = (NetworkDetailedReportFilter)calls[0].GetArguments()[0]!;
        var exportFilter = (NetworkDetailedReportFilter)calls[1].GetArguments()[0]!;
        listFilter.Channel.Should().Be(TramiteCanal.PersonaNaturalWeb);
        listFilter.ChannelTransitOfficeId.Should().Be(Office);
        exportFilter.Should().BeEquivalentTo(listFilter, "listado y exportación resuelven el mismo filtro");

        var hoja = ReadSheet(await export.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken));
        // Contrato v3: los grupos de identidad Comprador/Vendedor van DESPUÉS de «OT del canal», así que Canal se busca por
        // posición y no contando desde el final de la fila.
        var canal = hoja[0].IndexOf("Canal");
        canal.Should().BeGreaterThan(0);
        hoja[0].Skip(canal).Take(2).Should().Equal("Canal", "OT del canal");
        hoja[1][canal].Should().Be("Persona Natural Web");
        hoja[1][canal + 1].Should().Be("Organismo de prueba");
    }

    // ── #14319 AC9 — sin permiso nuevo ───────────────────────────────────────────────────────

    [Theory]
    [InlineData($"{Procedures}?{Range}&tenantId=a0000000-0000-4000-8000-000000005500&channel=pn_web", "reportes.detallados.read")]
    [InlineData($"{Procedures}/export?{Range}&tenantId=a0000000-0000-4000-8000-000000005500&channel=pn_web", "reportes.detallados.export")]
    public async Task AC9_el_canal_usa_los_mismos_permisos_de_siempre_y_sin_ellos_sigue_sin_acceso(string route, string slug)
    {
        using var enforced = _factory.WithWebHostBuilder(b => b.UseSetting("Security:ModulePermissionEnforcement:Enabled", "true"));

        var sin = enforced.CreateClient();
        sin.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token(S, "AdminCompany"));
        (await sin.GetAsync(route, TestContext.Current.CancellationToken)).StatusCode.Should().Be(HttpStatusCode.Forbidden, "sin el permiso de reportes sigue sin acceso");

        var con = enforced.CreateClient();
        con.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token(S, "AdminCompany", slug));
        (await con.GetAsync(route, TestContext.Current.CancellationToken)).StatusCode.Should().Be(HttpStatusCode.OK, "con el permiso existente basta: no hay permiso nuevo para el canal");
    }

    // ── #14320 AC1 / AC2 — SuperAdmin, todas las compañías ───────────────────────────────────

    [Fact]
    public async Task H14320_AC1_AC2_SuperAdmin_consulta_todas_las_companias_con_nombre_por_fila_y_el_filtro_de_canal()
    {
        var response = await ClientFor(Admin, "SuperAdmin").GetAsync($"{Procedures}?{Range}&allCompanies=true&channel=pn_web", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        await _factory.Repo.Received(1).GetNetworkProceduresAsync(
            Arg.Is<NetworkDetailedReportFilter>(f => f.TenantIds.SetEquals(new[] { P, C1, C2 }) && f.Channel == TramiteCanal.PersonaNaturalWeb),
            1, 20, Arg.Any<CancellationToken>());
        await _factory.StdRepo.DidNotReceiveWithAnyArgs().GetProceduresAsync(default!, default, default, default);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var items = body.RootElement.GetProperty("items").EnumerateArray().ToList();
        items.Select(i => i.GetProperty("tenantId").GetGuid()).Should().BeEquivalentTo([P, C1, C2]);
        items.Should().AllSatisfy(i => i.GetProperty("tenantName").GetString().Should().StartWith("Cliente "));
        body.RootElement.GetProperty("summary").GetProperty("byTenant").EnumerateArray().Should().HaveCount(3);
    }

    [Fact]
    public async Task H14320_sin_allCompanies_el_SuperAdmin_sin_tenantId_sigue_en_400_y_con_allCompanies_false_tambien()
    {
        var client = ClientFor(Admin, "SuperAdmin");

        (await client.GetAsync($"{Procedures}?{Range}", TestContext.Current.CancellationToken)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await client.GetAsync($"{Procedures}?{Range}&allCompanies=false", TestContext.Current.CancellationToken)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await client.GetAsync($"{Procedures}?{Range}&tenantId={S}", TestContext.Current.CancellationToken)).StatusCode.Should().Be(HttpStatusCode.OK);
        await _factory.Repo.DidNotReceiveWithAnyArgs().GetNetworkProceduresAsync(default!, default, default, default);
        await _factory.Writer.DidNotReceiveWithAnyArgs().WriteAsync(default!, default);
    }

    [Theory]
    [InlineData(Procedures)]
    [InlineData(Procedures + "/export")]
    public async Task H14320_allCompanies_junto_con_tenantId_es_400(string route)
    {
        var response = await ClientFor(Admin, "SuperAdmin").GetAsync($"{route}?{Range}&allCompanies=true&tenantId={S}", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        await AssertNothingQueried();
    }

    // ── #14320 AC3 — Excel ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task H14320_AC3_la_exportacion_trae_las_mismas_filas_del_listado_e_incluye_la_compania()
    {
        var client = ClientFor(Admin, "SuperAdmin");
        const string filtros = $"{Range}&allCompanies=true&channel=pn_web&status=entregado";

        (await client.GetAsync($"{Procedures}?{filtros}", TestContext.Current.CancellationToken)).StatusCode.Should().Be(HttpStatusCode.OK);
        var export = await client.GetAsync($"{Procedures}/export?{filtros}", TestContext.Current.CancellationToken);

        export.StatusCode.Should().Be(HttpStatusCode.OK);
        export.Content.Headers.ContentType!.MediaType.Should().Be(ExcelContentType);
        var calls = _factory.Repo.ReceivedCalls().Where(c => c.GetMethodInfo().Name is not (nameof(INetworkDetailedReportReadRepository.ListAllTenantIdsAsync) or nameof(INetworkDetailedReportReadRepository.GetNetworkMaxActoresPorRolAsync))).ToList();
        calls.Should().HaveCount(2);
        ((NetworkDetailedReportFilter)calls[1].GetArguments()[0]!).Should().BeEquivalentTo((NetworkDetailedReportFilter)calls[0].GetArguments()[0]!);

        var hoja = ReadSheet(await export.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken));
        hoja[0][0].Should().Be("Compañía");
        hoja.Skip(1).Select(r => r[0]).Should().HaveCount(3).And.OnlyContain(n => n.StartsWith("Cliente "));
    }

    [Fact]
    public async Task H14320_la_exportacion_respeta_el_rango_de_la_red_y_rechaza_el_invertido()
    {
        var response = await ClientFor(Admin, "SuperAdmin").GetAsync($"{Procedures}/export?from=2026-09-14&to=2026-09-01&allCompanies=true", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        await _factory.Repo.DidNotReceiveWithAnyArgs().ExportNetworkProceduresAsync(default!, default!, default);
    }

    // ── #14320 AC4 — otros roles ─────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("AdminCompany", Procedures)]
    [InlineData("AdminCompany", Procedures + "/export")]
    [InlineData("Radicador", Procedures)]
    [InlineData("Radicador", Procedures + "/export")]
    public async Task H14320_AC4_AdminCompany_y_gestor_reciben_403_sin_datos_ni_auditoria(string role, string route)
    {
        var response = await ClientFor(P, role).GetAsync($"{route}?{Range}&allCompanies=true&tenantId={C1}", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        body.Should().NotContain(C1.ToString()).And.NotContain("Cliente ");
        await AssertNothingQueried();
        await _factory.Writer.DidNotReceiveWithAnyArgs().WriteAsync(default!, default);
    }

    [Fact]
    public async Task H14320_AC4_AdminCompany_con_allCompanies_sin_tenantId_tambien_403_y_la_ruta_normal_sigue_igual()
    {
        var client = ClientFor(S, "AdminCompany");

        (await client.GetAsync($"{Procedures}?{Range}&allCompanies=true", TestContext.Current.CancellationToken)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await client.GetAsync($"{Procedures}?{Range}", TestContext.Current.CancellationToken)).StatusCode.Should().Be(HttpStatusCode.OK);
        await _factory.Repo.DidNotReceiveWithAnyArgs().GetNetworkProceduresAsync(default!, default, default, default);
        await _factory.StdRepo.Received(1).GetProceduresAsync(Arg.Is<DetailedReportFilter>(f => f.TenantId == S), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    // ── #14320 AC5 — auditoría ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task H14320_AC5_la_consulta_y_la_exportacion_en_modo_todas_las_companias_quedan_auditadas()
    {
        var client = ClientFor(Admin, "SuperAdmin");

        (await client.GetAsync($"{Procedures}?{Range}&allCompanies=true&channel=pn_web", TestContext.Current.CancellationToken)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetAsync($"{Procedures}/export?{Range}&allCompanies=true", TestContext.Current.CancellationToken)).StatusCode.Should().Be(HttpStatusCode.OK);

        await _factory.Writer.Received(1).WriteAsync(
            Arg.Is<NetworkAccessAuditEntry>(e => e.Resource == NetworkAccessVocabulary.Resources.ReportsProcedures
                && e.Result == NetworkAccessVocabulary.Results.Ok && e.ActorTenantId == Admin && e.ActorUserId == UserId
                && e.ReachedTenantIds.Count == 3 && e.ReachedTenantIds.Contains(C1)
                && e.FiltersJson != null && e.FiltersJson.Contains("\"allCompanies\":true") && e.FiltersJson.Contains("\"channel\":\"pn_web\"")),
            Arg.Any<CancellationToken>());
        await _factory.Writer.Received(1).WriteAsync(
            Arg.Is<NetworkAccessAuditEntry>(e => e.Resource == NetworkAccessVocabulary.Resources.ReportsExport
                && e.Result == NetworkAccessVocabulary.Results.Ok && e.ReachedTenantIds.Count == 3
                && e.FiltersJson != null && e.FiltersJson.Contains("\"allCompanies\":true")),
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("")]
    [InlineData("/export")]
    public async Task H14320_AC5_sin_auditoria_no_salen_datos_de_todas_las_companias(string ruta)
    {
        _factory.Writer.WriteAsync(default!, default).ReturnsForAnyArgs(false);
        try
        {
            var response = await ClientFor(Admin, "SuperAdmin").GetAsync($"{Procedures}{ruta}?{Range}&allCompanies=true", TestContext.Current.CancellationToken);

            response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
            (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).Should().NotContain("legalName").And.NotContain("personDocument");
        }
        finally
        {
            _factory.Writer.WriteAsync(default!, default).ReturnsForAnyArgs(true);
        }
    }

    [Fact]
    public async Task H14320_los_filtros_de_texto_libre_se_auditan_solo_por_nombre()
    {
        await ClientFor(Admin, "SuperAdmin").GetAsync($"{Procedures}?{Range}&allCompanies=true&personDocument=9876543&personName=Ana", TestContext.Current.CancellationToken);

        await _factory.Writer.Received(1).WriteAsync(
            Arg.Is<NetworkAccessAuditEntry>(e => e.FiltersJson != null && e.FiltersJson.Contains("\"textFilters\":[\"personDocument\",\"personName\"]")
                && !e.FiltersJson.Contains("9876543") && !e.FiltersJson.Contains("Ana")),
            Arg.Any<CancellationToken>());
    }

    // ── helpers ──────────────────────────────────────────────────────────────────────────────

    private async Task AssertNothingQueried()
    {
        await _factory.Repo.DidNotReceiveWithAnyArgs().GetNetworkProceduresAsync(default!, default, default, default);
        await _factory.Repo.DidNotReceiveWithAnyArgs().ExportNetworkProceduresAsync(default!, default!, default);
        await _factory.Repo.DidNotReceiveWithAnyArgs().ListAllTenantIdsAsync(default);
        await _factory.StdRepo.DidNotReceiveWithAnyArgs().GetProceduresAsync(default!, default, default, default);
        await _factory.StdRepo.DidNotReceiveWithAnyArgs().ExportProceduresAsync(default!, default!, default);
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

    private HttpClient ClientFor(Guid tenantId, string role)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token(tenantId, role));
        return client;
    }

    private static string Token(Guid tenantId, string role, params string[] permisos)
    {
        var claims = new List<Claim>
        {
            new("sub", UserId.ToString()),
            new("role", role),
            new("role_code", role),
            new("tenant_id", tenantId.ToString()),
        };
        claims.AddRange(permisos.Select(p => new Claim("permissions", p)));

        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = "https://api.flit.co",
            Audience = "flit-api",
            Subject = new ClaimsIdentity(claims),
            Expires = DateTime.UtcNow.AddHours(1),
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(new string('k', 64))), SecurityAlgorithms.HmacSha256),
        });
    }

    /// <summary>Host con los repositorios y el writer sustituidos; el exportador OpenXml es el real.</summary>
    public sealed class CanalFactory : WebApplicationFactory<Program>
    {
        public INetworkDetailedReportReadRepository Repo { get; } = Substitute.For<INetworkDetailedReportReadRepository>();

        public IDetailedReportReadRepository StdRepo { get; } = Substitute.For<IDetailedReportReadRepository>();

        public INetworkAccessAuditWriter Writer { get; } = Substitute.For<INetworkAccessAuditWriter>();

        public CanalFactory()
        {
            Repo.ListAllTenantIdsAsync(Arg.Any<CancellationToken>()).Returns(call => (IReadOnlySet<Guid>)new HashSet<Guid> { P, C1, C2 });
            Repo.GetNetworkProceduresAsync(Arg.Any<NetworkDetailedReportFilter>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
                .Returns(call =>
                {
                    var filter = call.ArgAt<NetworkDetailedReportFilter>(0);
                    var rows = RowsFor(filter);
                    var summary = new NetworkDetailedReportSummaryDto(rows.Count, [new("entregado", rows.Count)], [new("matriculas", rows.Count)], [new("Matrícula", rows.Count)],
                        rows.Select(r => new TenantCountDto(r.TenantId, r.TenantName, 1)).ToList());
                    var page = new NetworkDetailedProceduresPageDto(rows, rows.Count, call.ArgAt<int>(1), call.ArgAt<int>(2), summary,
                        new NetworkScopeDto(filter.TenantIds.OrderBy(t => t).ToList()));
                    return new NetworkAnalyticsResult<NetworkDetailedProceduresPageDto>(page, rows.Select(r => r.TenantId).OrderBy(t => t).ToList());
                });
            Repo.ExportNetworkProceduresAsync(Arg.Any<NetworkDetailedReportFilter>(), Arg.Any<Func<NetworkDetailedProcedureRowDto, CancellationToken, Task>>(), Arg.Any<CancellationToken>())
                .Returns(async call =>
                {
                    var rows = RowsFor(call.ArgAt<NetworkDetailedReportFilter>(0));
                    var onRow = call.ArgAt<Func<NetworkDetailedProcedureRowDto, CancellationToken, Task>>(1);
                    foreach (var row in rows)
                        await onRow(row, CancellationToken.None);
                    return (IReadOnlyList<Guid>)rows.Select(r => r.TenantId).OrderBy(t => t).ToList();
                });

            StdRepo.GetProceduresAsync(Arg.Any<DetailedReportFilter>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
                .Returns(call =>
                {
                    var f = call.ArgAt<DetailedReportFilter>(0);
                    var row = new DetailedProcedureRowDto(
                        Guid.NewGuid(), "000001", "Matrícula", "matriculas", "entregado", "Gestor", null, null, "123", "Persona", false, null, false, "contado", null,
                        f.Channel ?? TramiteCanal.Tramites, f.ChannelTransitOfficeId, f.ChannelTransitOfficeId is null ? null : "Organismo de prueba");
                    return new DetailedProceduresPageDto([row], 1, call.ArgAt<int>(1), call.ArgAt<int>(2),
                        new DetailedReportSummaryDto(1, [new("entregado", 1)], [new("matriculas", 1)], [new("Matrícula", 1)]));
                });
            StdRepo.ExportProceduresAsync(Arg.Any<DetailedReportFilter>(), Arg.Any<Func<DetailedProcedureRowDto, CancellationToken, Task>>(), Arg.Any<CancellationToken>())
                .Returns(async call =>
                {
                    var onRow = call.ArgAt<Func<DetailedProcedureRowDto, CancellationToken, Task>>(1);
                    await onRow(new DetailedProcedureRowDto(
                        Guid.NewGuid(), "000001", "Matrícula", "matriculas", "entregado", "Gestor", null, null, "123", "Persona", false, null, false, "contado", null), CancellationToken.None);
                });
        }

        private static List<NetworkDetailedProcedureRowDto> RowsFor(NetworkDetailedReportFilter filter) =>
            filter.TenantIds.OrderBy(t => t).Select(t => new NetworkDetailedProcedureRowDto(
                t, $"Cliente {t:N}"[..14], Guid.NewGuid(), t.ToString("N")[^6..], "Matrícula", "matriculas", "entregado", "Gestor",
                new DateTimeOffset(2026, 9, 2, 10, 0, 0, TimeSpan.Zero), null, "123", "Persona", false, null, false, "contado", null,
                filter.Channel ?? TramiteCanal.Tramites, filter.ChannelTransitOfficeId, filter.ChannelTransitOfficeId is null ? null : "Organismo de prueba"))
                .ToList();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            ArgumentNullException.ThrowIfNull(builder);
            builder.ConfigureTestServices(services =>
            {
                services.AddScoped(_ => Repo);
                services.AddScoped(_ => StdRepo);
                services.AddScoped(_ => Writer);
                services.AddScoped<ITenantScopeResolver>(_ => new FakeScopeResolver());
            });
        }
    }

    private sealed class FakeScopeResolver : ITenantScopeResolver
    {
        public Task<TenantScope> ResolveAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
            Task.FromResult(tenantId == P
                ? TenantScope.Group(P, [C1, C2], GroupKind.Concesion)
                : TenantScope.Single(tenantId));
    }
}
