using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Flit.Analytics.Application.Abstractions;
using Flit.Analytics.Application.Dtos;
using Flit.Api.Middleware;
using Flit.Queries.Domain.Tenancy;
using Flit.Tramites.Application.Auditing;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using NSubstitute;
using Xunit;

namespace Flit.Admin.Tests.Tramites;

/// <summary>
/// HU #12360 (Feature #12257) — capa HTTP del reporte detallado de la red, sin PostgreSQL: host real con
/// el repositorio de red sustituido (<see cref="INetworkDetailedReportReadRepository"/>), el exportador
/// OpenXml REAL sobre ese repositorio, el resolver de alcance fijo (P ⇒ <c>Group(P,[C1,C2])</c>) y el
/// writer de auditoría capturado.
/// <list type="bullet">
///   <item>AC1/AC2 — el listado devuelve cada fila con su cliente (<c>tenantId</c>, <c>tenantName</c>) y
///   totales por cliente; <c>childTenantId</c> acota el conjunto que recibe el repositorio.</item>
///   <item>AC3 — <c>childTenantId</c> ajeno (X) ⇒ 403 <c>network_child_out_of_scope</c> y el repositorio
///   NO se invoca (listado y exportación); el intento queda auditado.</item>
///   <item>AC5 — la exportación responde un XLSX real cuya primera columna es «Compañía» y cuyas filas
///   son las que entregó la misma consulta con el mismo filtro.</item>
///   <item>AC6 — ni el JSON ni la cabecera del XLSX tienen <c>preview</c>, <c>download</c>, <c>url</c> ni <c>attachment</c>.</item>
///   <item>AC7 — rutas nuevas bajo <c>/api/v1/tramites/network/reports/*</c>; las de
///   <c>/api/v1/detailed-report/*</c> siguen con su nombre y sin parámetros nuevos.</item>
/// </list>
/// Uso de ejemplo: <c>GET /api/v1/tramites/network/reports/procedures?from=2026-09-01&amp;to=2026-09-14&amp;childTenantId={C1}</c>
/// con token de P ⇒ 200 y el repositorio recibe <c>{C1}</c>.
/// </summary>
public sealed class NetworkReportsEndpointsTests : IClassFixture<NetworkReportsEndpointsTests.ReportsFactory>
{
    private const string Base = "/api/v1/tramites/network/reports";
    private const string Range = "from=2026-09-01&to=2026-09-14";
    private const string ExcelContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    private static readonly Guid P = Guid.Parse("a0000000-0000-4000-8000-000000000100");
    private static readonly Guid C1 = Guid.Parse("a0000000-0000-4000-8000-000000001100");
    private static readonly Guid C2 = Guid.Parse("a0000000-0000-4000-8000-000000001200");
    private static readonly Guid X = Guid.Parse("a0000000-0000-4000-8000-000000009900");
    private static readonly Guid S = Guid.Parse("a0000000-0000-4000-8000-000000005500");
    private static readonly Guid UserId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private readonly ReportsFactory _factory;

    public NetworkReportsEndpointsTests(ReportsFactory factory)
    {
        _factory = factory;
        _factory.Repo.ClearReceivedCalls();
        _factory.Writer.ClearReceivedCalls();
    }

    public static TheoryData<string> Routes => new()
    {
        $"{Base}/procedures",
        $"{Base}/procedures/export",
    };

    // ── AC7 — rutas nuevas bajo el grupo de red, con la policy de cabeza ─────────────────────

    [Theory]
    [MemberData(nameof(Routes))]
    public async Task Sin_token_401(string route)
    {
        var response = await _factory.CreateClient().GetAsync($"{route}?{Range}", TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        await AssertRepoNotCalled();
    }

    [Theory]
    [MemberData(nameof(Routes))]
    public async Task Cliente_sin_red_403_scope_required_sin_consulta(string route)
    {
        var response = await ClientFor(S, "AdminCompany").GetAsync($"{route}?{Range}", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).Should().Contain(NetworkScopePolicy.ScopeRequired);
        await AssertRepoNotCalled();
        await _factory.Writer.DidNotReceive().WriteAsync(Arg.Any<NetworkAccessAuditEntry>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [MemberData(nameof(Routes))]
    public async Task SuperAdmin_403_scope_required_los_reportes_globales_son_los_de_siempre(string route)
    {
        var response = await ClientFor(P, "SuperAdmin").GetAsync($"{route}?{Range}", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        await AssertRepoNotCalled();
    }

    [Fact]
    public void Las_rutas_de_red_estan_bajo_el_prefijo_del_middleware_y_las_de_detailed_report_no_cambian()
    {
        TenantEnforcementMiddleware.IsRuntimeScoped($"{Base}/procedures").Should().BeTrue();
        TenantEnforcementMiddleware.IsRuntimeScoped($"{Base}/procedures/export").Should().BeTrue();

        var endpoints = _factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(e => e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods.Contains("GET") == true)
            .Select(e => new KeyValuePair<string, string?>(e.RoutePattern.RawText!, e.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName))
            .ToList();

        endpoints.Should().Contain($"{Base}/procedures", "NetworkReportsProcedures")
            .And.Contain($"{Base}/procedures/export", "NetworkReportsExportExcel");
        endpoints.Should().Contain("/api/v1/detailed-report/procedures", "DetailedReportProcedures")
            .And.Contain("/api/v1/detailed-report/procedures/export", "DetailedReportExportExcel");
    }

    // ── AC1 / AC2 — listado con cliente por fila, acotado por hijo ───────────────────────────

    [Fact]
    public async Task Listado_Cabeza_200_cada_fila_con_su_cliente_totales_por_cliente_y_auditoria_ok()
    {
        var response = await ClientFor(P, "AdminCompany").GetAsync($"{Base}/procedures?{Range}&status=entregado&pageSize=500", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        await _factory.Repo.Received(1).GetNetworkProceduresAsync(
            Arg.Is<NetworkDetailedReportFilter>(f => f.TenantIds.SetEquals(new[] { P, C1, C2 }) && f.Status == "entregado"
                && f.From == new DateOnly(2026, 9, 1) && f.To == new DateOnly(2026, 9, 14)),
            1, 100, Arg.Any<CancellationToken>());

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var root = body.RootElement;
        var items = root.GetProperty("items").EnumerateArray().ToList();
        items.Should().HaveCount(3);
        items.Select(i => i.GetProperty("tenantId").GetGuid()).Should().BeEquivalentTo([P, C1, C2]);
        items.Should().AllSatisfy(i => i.GetProperty("tenantName").GetString().Should().StartWith("Cliente "));
        root.GetProperty("totalCount").GetInt32().Should().Be(3);
        root.GetProperty("summary").GetProperty("byTenant").EnumerateArray().Should().HaveCount(3);
        root.GetProperty("scope").GetProperty("tenantIds").EnumerateArray().Select(e => e.GetGuid()).Should().BeEquivalentTo([P, C1, C2]);
        root.TryGetProperty("reachedTenantIds", out _).Should().BeFalse("los hijos alcanzados son para la auditoría, no para el cliente");

        await _factory.Writer.Received(1).WriteAsync(
            Arg.Is<NetworkAccessAuditEntry>(e => e.Resource == NetworkAccessVocabulary.Resources.ReportsProcedures
                && e.Result == NetworkAccessVocabulary.Results.Ok && e.ActorTenantId == P
                && e.ReachedTenantIds.Count == 2 && e.ReachedTenantIds.Contains(C1) && e.ReachedTenantIds.Contains(C2)
                && e.FiltersJson != null && e.FiltersJson.Contains("\"status\":\"entregado\"") && e.FiltersJson.Contains("2026-09-01")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Listado_childTenantId_hijo_200_el_repositorio_recibe_solo_ese_hijo()
    {
        var response = await ClientFor(P, "AdminCompany").GetAsync($"{Base}/procedures?{Range}&childTenantId={C1}", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        await _factory.Repo.Received(1).GetNetworkProceduresAsync(
            Arg.Is<NetworkDetailedReportFilter>(f => f.TenantIds.Count == 1 && f.TenantIds.Contains(C1)),
            1, 20, Arg.Any<CancellationToken>());

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        body.RootElement.GetProperty("items").EnumerateArray().Should().ContainSingle().Which.GetProperty("tenantId").GetGuid().Should().Be(C1);
        body.RootElement.GetProperty("summary").GetProperty("byTenant").EnumerateArray().Should().ContainSingle();
        body.RootElement.GetProperty("scope").GetProperty("tenantIds").EnumerateArray().Select(e => e.GetGuid()).Should().Equal(C1);
    }

    [Fact]
    public async Task Los_filtros_de_texto_libre_se_auditan_solo_por_nombre()
    {
        var response = await ClientFor(P, "AdminCompany").GetAsync(
            $"{Base}/procedures?{Range}&personDocument=1234567&personName=Juan&referenceNumber=000001", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        await _factory.Writer.Received(1).WriteAsync(
            Arg.Is<NetworkAccessAuditEntry>(e => e.FiltersJson != null
                && e.FiltersJson.Contains("\"textFilters\":[\"referenceNumber\",\"personDocument\",\"personName\"]")
                && !e.FiltersJson.Contains("1234567") && !e.FiltersJson.Contains("Juan") && !e.FiltersJson.Contains("000001")),
            Arg.Any<CancellationToken>());
    }

    // ── AC3 — hijo ajeno rechazado sin consulta, con el intento auditado ─────────────────────

    [Theory]
    [MemberData(nameof(Routes))]
    public async Task childTenantId_ajeno_403_child_out_of_scope_sin_consulta_y_con_intento_auditado(string route)
    {
        var response = await ClientFor(P, "AdminCompany").GetAsync($"{route}?{Range}&childTenantId={X}", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).Should().Contain(NetworkScopePolicy.ChildOutOfScope);
        await AssertRepoNotCalled();
        await _factory.Writer.Received(1).WriteAsync(
            Arg.Is<NetworkAccessAuditEntry>(e => e.Result == NetworkAccessVocabulary.Results.Forbidden
                && e.ReachedTenantIds.Single() == X && e.ActorTenantId == P && e.Resource.StartsWith("network.reports.")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task El_alcance_no_sale_de_la_peticion_ni_X_Tenant_Id_ni_tenantId_amplian()
    {
        var client = ClientFor(P, "AdminCompany");
        client.DefaultRequestHeaders.Add("X-Tenant-Id", X.ToString());

        var response = await client.GetAsync($"{Base}/procedures?{Range}&tenantId={X}", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        await _factory.Repo.Received(1).GetNetworkProceduresAsync(
            Arg.Is<NetworkDetailedReportFilter>(f => f.TenantIds.SetEquals(new[] { P, C1, C2 }) && !f.TenantIds.Contains(X)),
            Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [MemberData(nameof(Routes))]
    public async Task Rango_invalido_400_sin_consulta(string route)
    {
        var response = await ClientFor(P, "AdminCompany").GetAsync($"{route}?from=2026-09-14&to=2026-09-01", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        await AssertRepoNotCalled();
    }

    // ── AC5 / AC6 — exportación coherente y sin documentos ───────────────────────────────────

    [Fact]
    public async Task Export_200_xlsx_con_columna_Compania_las_mismas_filas_de_la_consulta_y_auditoria_ok()
    {
        var response = await ClientFor(P, "AdminCompany").GetAsync($"{Base}/procedures/export?{Range}&childTenantId={C2}&status=entregado", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be(ExcelContentType);
        response.Content.Headers.ContentDisposition!.FileName.Should().Contain("reporte_red_20260901_20260914.xlsx");
        await _factory.Repo.Received(1).ExportNetworkProceduresAsync(
            Arg.Is<NetworkDetailedReportFilter>(f => f.TenantIds.Count == 1 && f.TenantIds.Contains(C2) && f.Status == "entregado"),
            Arg.Any<Func<NetworkDetailedProcedureRowDto, CancellationToken, Task>>(), Arg.Any<CancellationToken>());

        var rows = ReadSheet(await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken));
        rows[0][0].Should().Be("Compañía");
        rows[0].Skip(1).Should().Equal(
            "Referencia", "Tipo de trámite", "Categoría", "Estado", "Radicado por", "Persona documento", "Persona nombre",
            "Transformación", "Detalle transformación", "Leasing", "Tipo pago", "Tipo traspaso", "Enviado", "Completado");
        rows.Should().HaveCount(2, "cabecera + la única fila que la consulta entrega para C2");
        rows[1][0].Should().Be($"Cliente {C2:N}"[..14]);
        rows[1][1].Should().Be(ReportsFactory.ReferenceOf(C2));
        // AC6 — ninguna columna de documentos ni enlaces.
        rows[0].Should().NotContain(h => h.Contains("preview", StringComparison.OrdinalIgnoreCase) || h.Contains("download", StringComparison.OrdinalIgnoreCase)
            || h.Contains("url", StringComparison.OrdinalIgnoreCase) || h.Contains("attachment", StringComparison.OrdinalIgnoreCase)
            || h.Contains("anexo", StringComparison.OrdinalIgnoreCase) || h.Contains("enlace", StringComparison.OrdinalIgnoreCase));
        rows.SelectMany(r => r).Should().NotContain(c => c.Contains("http", StringComparison.OrdinalIgnoreCase));

        await _factory.Writer.Received(1).WriteAsync(
            Arg.Is<NetworkAccessAuditEntry>(e => e.Resource == NetworkAccessVocabulary.Resources.ReportsExport
                && e.Result == NetworkAccessVocabulary.Results.Ok && e.ReachedTenantIds.Single() == C2
                && e.FiltersJson != null && e.FiltersJson.Contains(C2.ToString())),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Export_y_listado_resuelven_el_mismo_filtro_para_la_misma_peticion()
    {
        const string query = $"{Range}&childTenantId=a0000000-0000-4000-8000-000000001100&category=Matriculas&isLeasing=true";
        var client = ClientFor(P, "AdminCompany");

        (await client.GetAsync($"{Base}/procedures?{query}", TestContext.Current.CancellationToken)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetAsync($"{Base}/procedures/export?{query}", TestContext.Current.CancellationToken)).StatusCode.Should().Be(HttpStatusCode.OK);

        var calls = _factory.Repo.ReceivedCalls().ToList();
        calls.Should().HaveCount(2);
        var listFilter = (NetworkDetailedReportFilter)calls[0].GetArguments()[0]!;
        var exportFilter = (NetworkDetailedReportFilter)calls[1].GetArguments()[0]!;
        exportFilter.Should().BeEquivalentTo(listFilter);
        exportFilter.TenantIds.Should().Equal(C1);
        exportFilter.Category.Should().Be("matriculas");
        exportFilter.IsLeasing.Should().BeTrue();
    }

    [Fact]
    public async Task Listado_JSON_sin_campos_de_documentos_ni_enlaces()
    {
        var response = await ClientFor(P, "AdminCompany").GetAsync($"{Base}/procedures?{Range}", TestContext.Current.CancellationToken);
        var json = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var body = JsonDocument.Parse(json);
        var names = body.RootElement.GetProperty("items")[0].EnumerateObject().Select(p => p.Name.ToLowerInvariant()).ToList();
        names.Should().Contain(["tenantid", "tenantname", "id", "referencenumber", "status"]);
        names.Should().NotContain(n => n.Contains("preview") || n.Contains("download") || n.Contains("url") || n.Contains("attachment"));
        json.Should().NotContain("http://").And.NotContain("https://");
    }

    // ── helpers ──────────────────────────────────────────────────────────────────────────────

    private async Task AssertRepoNotCalled()
    {
        await _factory.Repo.DidNotReceive().GetNetworkProceduresAsync(Arg.Any<NetworkDetailedReportFilter>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
        await _factory.Repo.DidNotReceive().ExportNetworkProceduresAsync(
            Arg.Any<NetworkDetailedReportFilter>(), Arg.Any<Func<NetworkDetailedProcedureRowDto, CancellationToken, Task>>(), Arg.Any<CancellationToken>());
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

    private static string Token(Guid tenantId, string role)
    {
        var claims = new List<Claim>
        {
            new("sub", UserId.ToString()),
            new("role", role),
            new("role_code", role),
            new("tenant_id", tenantId.ToString()),
        };

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

    /// <summary>
    /// Host con repositorio de red sustituido (una fila por cliente del conjunto recibido), exportador
    /// OpenXml REAL, resolver de alcance y writer de auditoría sustituidos (sin PostgreSQL).
    /// </summary>
    public sealed class ReportsFactory : WebApplicationFactory<Program>
    {
        public INetworkDetailedReportReadRepository Repo { get; } = Substitute.For<INetworkDetailedReportReadRepository>();

        public INetworkAccessAuditWriter Writer { get; } = Substitute.For<INetworkAccessAuditWriter>();

        public static string ReferenceOf(Guid tenant) => tenant.ToString("N")[^6..];

        public ReportsFactory()
        {
            Repo.GetNetworkProceduresAsync(Arg.Any<NetworkDetailedReportFilter>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
                .Returns(call =>
                {
                    var filter = call.ArgAt<NetworkDetailedReportFilter>(0);
                    var rows = RowsFor(filter);
                    var byTenant = rows.Select(r => new TenantCountDto(r.TenantId, r.TenantName, 1)).ToList();
                    var summary = new NetworkDetailedReportSummaryDto(rows.Count, [new("entregado", rows.Count)], [new("matriculas", rows.Count)], [new("Matrícula", rows.Count)], byTenant);
                    var dto = new NetworkDetailedProceduresPageDto(rows, rows.Count, call.ArgAt<int>(1), call.ArgAt<int>(2), summary,
                        new NetworkScopeDto(filter.TenantIds.OrderBy(t => t).ToList()));
                    return new NetworkAnalyticsResult<NetworkDetailedProceduresPageDto>(dto, rows.Select(r => r.TenantId).OrderBy(t => t).ToList());
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
        }

        /// <summary>Una fila «entregado» por cliente del conjunto (el sustituto respeta el conjunto que recibe, como la base).</summary>
        private static List<NetworkDetailedProcedureRowDto> RowsFor(NetworkDetailedReportFilter filter) =>
            filter.TenantIds.OrderBy(t => t).Select(t => new NetworkDetailedProcedureRowDto(
                t, $"Cliente {t:N}"[..14], Guid.NewGuid(), ReferenceOf(t), "Matrícula", "matriculas", "entregado", "Gestor",
                new DateTimeOffset(2026, 9, 2, 10, 0, 0, TimeSpan.Zero), null, "123", "Persona", false, null, false, "contado", null))
                .ToList();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            ArgumentNullException.ThrowIfNull(builder);
            builder.ConfigureTestServices(services =>
            {
                services.AddScoped(_ => Repo);
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
