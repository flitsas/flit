using Flit.Analytics.Application.Abstractions;
using Flit.Analytics.Application.Dtos;
using Flit.Analytics.Application.Queries;
using Flit.Analytics.Application.Queries.Network;
using Flit.Queries.Domain.Tenancy;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Analytics.Application.Tests.Network;

/// <summary>
/// HU #12360 (Feature #12257) — reporte detallado de la red, sin base de datos:
/// <list type="bullet">
///   <item>AC3 — un <c>childTenantId</c> ajeno se rechaza SIN invocar el repositorio.</item>
///   <item>AC4 — el conjunto que llega al repositorio es siempre alcance ∩ filtro; sin alcance de grupo ⇒
///   <c>network_scope_required</c>.</item>
///   <item>AC5 — listado y exportación resuelven EXACTAMENTE el mismo filtro para la misma petición.</item>
///   <item>Contrato — paginación acotada como la ruta de siempre; filtros normalizados igual
///   (recorte, categoría en minúsculas); hijos alcanzados aparte de la respuesta.</item>
/// </list>
/// Uso de ejemplo: <c>new GetNetworkDetailedProceduresHandler(repo).HandleAsync(new(scope, request, 1, 20))</c>.
/// </summary>
public sealed class NetworkDetailedReportHandlersTests
{
    private static readonly Guid P = Guid.Parse("a0000000-0000-4000-8000-000000000100");
    private static readonly Guid C1 = Guid.Parse("a0000000-0000-4000-8000-000000001100");
    private static readonly Guid C2 = Guid.Parse("a0000000-0000-4000-8000-000000001200");
    private static readonly Guid X = Guid.Parse("a0000000-0000-4000-8000-000000009900");
    private static readonly DateOnly From = new(2026, 9, 1);
    private static readonly DateOnly To = new(2026, 9, 14);

    private readonly INetworkDetailedReportReadRepository _repo = Substitute.For<INetworkDetailedReportReadRepository>();

    private static TenantScope GroupP() => TenantScope.Group(P, [C1, C2], GroupKind.Concesion);

    public NetworkDetailedReportHandlersTests()
    {
        _repo.GetNetworkProceduresAsync(Arg.Any<NetworkDetailedReportFilter>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var filter = call.ArgAt<NetworkDetailedReportFilter>(0);
                var page = call.ArgAt<int>(1);
                var pageSize = call.ArgAt<int>(2);
                var scope = new NetworkScopeDto(filter.TenantIds.OrderBy(t => t).ToList());
                var summary = new NetworkDetailedReportSummaryDto(2, [new("entregado", 2)], [new("matriculas", 2)], [new("Matrícula", 2)],
                    [new(C1, "Cliente C1", 1), new(C2, "Cliente C2", 1)]);
                var dto = new NetworkDetailedProceduresPageDto([Row(C1), Row(C2)], 2, page, pageSize, summary, scope);
                return new NetworkAnalyticsResult<NetworkDetailedProceduresPageDto>(dto, [C1, C2]);
            });
    }

    // ── Happy path ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Listado_Cabeza_consulta_todo_el_alcance_y_cada_fila_identifica_a_su_cliente()
    {
        var (result, error) = await new GetNetworkDetailedProceduresHandler(_repo)
            .HandleAsync(new GetNetworkDetailedProceduresQuery(GroupP(), Request(null), 1, 20), TestContext.Current.CancellationToken);

        error.Should().BeNull();
        result!.Response.Items.Should().HaveCount(2);
        result.Response.Items.Select(i => i.TenantId).Should().Equal(C1, C2);
        result.Response.Items.Should().OnlyContain(i => !string.IsNullOrEmpty(i.TenantName));
        result.Response.Scope.TenantIds.Should().Equal(new[] { P, C1, C2 }.OrderBy(t => t));
        result.Response.Summary.ByTenant.Should().HaveCount(2);
        result.ReachedTenantIds.Should().Equal(C1, C2);
        await _repo.Received(1).GetNetworkProceduresAsync(
            Arg.Is<NetworkDetailedReportFilter>(f => f.TenantIds.SetEquals(new[] { P, C1, C2 }) && f.From == From && f.To == To),
            1, 20, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Listado_childTenantId_hijo_acota_el_conjunto_a_ese_hijo()
    {
        var (result, error) = await new GetNetworkDetailedProceduresHandler(_repo)
            .HandleAsync(new GetNetworkDetailedProceduresQuery(GroupP(), Request(C1), 1, 20), TestContext.Current.CancellationToken);

        error.Should().BeNull();
        result!.Response.Scope.TenantIds.Should().Equal(C1);
        await _repo.Received(1).GetNetworkProceduresAsync(
            Arg.Is<NetworkDetailedReportFilter>(f => f.TenantIds.Count == 1 && f.TenantIds.Contains(C1)),
            Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Listado_childTenantId_la_propia_cabeza_es_un_cliente_valido_de_la_red()
    {
        var (_, error) = await new GetNetworkDetailedProceduresHandler(_repo)
            .HandleAsync(new GetNetworkDetailedProceduresQuery(GroupP(), Request(P), 1, 20), TestContext.Current.CancellationToken);

        error.Should().BeNull();
        await _repo.Received(1).GetNetworkProceduresAsync(
            Arg.Is<NetworkDetailedReportFilter>(f => f.TenantIds.Count == 1 && f.TenantIds.Contains(P)),
            Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    // ── AC3 / AC4 — ajeno rechazado sin consulta; sin alcance de grupo nada que consolidar ────

    [Fact]
    public async Task Listado_childTenantId_ajeno_403_child_out_of_scope_sin_invocar_el_repositorio()
    {
        var (result, error) = await new GetNetworkDetailedProceduresHandler(_repo)
            .HandleAsync(new GetNetworkDetailedProceduresQuery(GroupP(), Request(X), 1, 20), TestContext.Current.CancellationToken);

        result.Should().BeNull();
        error.Should().Be(NetworkScopePolicy.ChildOutOfScope);
        await _repo.DidNotReceive().GetNetworkProceduresAsync(Arg.Any<NetworkDetailedReportFilter>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Exportacion_childTenantId_ajeno_devuelve_el_mismo_codigo_sin_filtro()
    {
        var (filter, error) = ExportNetworkDetailedProceduresHandler.Validate(GroupP(), Request(X));

        filter.Should().BeNull();
        error.Should().Be(NetworkScopePolicy.ChildOutOfScope);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Sin_alcance_de_grupo_scope_required_sin_consulta(bool clienteSinRed)
    {
        // Sin alcance (ruta no scopeada) o cliente sin red: nada que consolidar. El SuperAdmin (IsAll)
        // queda cubierto por la política compartida NetworkScopePolicy.Validate (tests de HU #12358/#12359).
        var scope = clienteSinRed ? TenantScope.Single(P) : null;
        var (result, error) = await new GetNetworkDetailedProceduresHandler(_repo)
            .HandleAsync(new GetNetworkDetailedProceduresQuery(scope, Request(null), 1, 20), TestContext.Current.CancellationToken);

        result.Should().BeNull();
        error.Should().Be(NetworkScopePolicy.ScopeRequired);
        await _repo.DidNotReceive().GetNetworkProceduresAsync(Arg.Any<NetworkDetailedReportFilter>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Rango_invalido_invalid_range_sin_consulta()
    {
        var request = Request(null) with { From = To, To = From };

        var (_, error) = await new GetNetworkDetailedProceduresHandler(_repo)
            .HandleAsync(new GetNetworkDetailedProceduresQuery(GroupP(), request, 1, 20), TestContext.Current.CancellationToken);

        error.Should().Be(NetworkAnalyticsScope.InvalidRange);
        ExportNetworkDetailedProceduresHandler.Validate(GroupP(), request).Error.Should().Be(NetworkAnalyticsScope.InvalidRange);
        await _repo.DidNotReceive().GetNetworkProceduresAsync(Arg.Any<NetworkDetailedReportFilter>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    // ── AC5 — listado y exportación comparten la resolución del filtro ────────────────────────

    [Fact]
    public async Task Exportacion_resuelve_exactamente_el_mismo_filtro_que_el_listado()
    {
        var request = Request(C2) with { Category = "  Matriculas ", Status = " entregado ", ReferenceNumber = " 123 ", HasTransformation = true };

        await new GetNetworkDetailedProceduresHandler(_repo)
            .HandleAsync(new GetNetworkDetailedProceduresQuery(GroupP(), request, 1, 20), TestContext.Current.CancellationToken);
        var (exportFilter, error) = ExportNetworkDetailedProceduresHandler.Validate(GroupP(), request);

        error.Should().BeNull();
        var listFilter = (NetworkDetailedReportFilter)_repo.ReceivedCalls().Single().GetArguments()[0]!;
        exportFilter.Should().BeEquivalentTo(listFilter);
        exportFilter!.TenantIds.Should().Equal(C2);
        exportFilter.Category.Should().Be("matriculas", "misma normalización que la ruta de siempre");
        exportFilter.Status.Should().Be("entregado");
        exportFilter.ReferenceNumber.Should().Be("123");
        exportFilter.HasTransformation.Should().BeTrue();
    }

    // ── Contrato ──────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(0, 0, 1, GetDetailedProceduresHandler.DefaultPageSize)]
    [InlineData(-3, 500, 1, GetDetailedProceduresHandler.MaxPageSize)]
    [InlineData(4, 50, 4, 50)]
    public async Task La_paginacion_se_acota_como_en_la_ruta_de_siempre(int page, int pageSize, int expectedPage, int expectedPageSize)
    {
        await new GetNetworkDetailedProceduresHandler(_repo)
            .HandleAsync(new GetNetworkDetailedProceduresQuery(GroupP(), Request(null), page, pageSize), TestContext.Current.CancellationToken);

        await _repo.Received(1).GetNetworkProceduresAsync(Arg.Any<NetworkDetailedReportFilter>(), expectedPage, expectedPageSize, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void La_fila_de_red_no_expone_documentos_ni_enlaces_y_su_forma_base_es_la_fila_de_siempre()
    {
        var row = Row(C1);

        // AC6 — ni contenido ni enlaces de documentos generados o anexos.
        var names = typeof(NetworkDetailedProcedureRowDto).GetProperties().Select(p => p.Name.ToLowerInvariant()).ToList();
        names.Should().NotContain(n => n.Contains("preview") || n.Contains("download") || n.Contains("url") || n.Contains("attachment") || n.Contains("document") && !n.Contains("persondocument"));
        // AC7 — la forma base es exactamente la fila de siempre (mismo vocabulario para el frontend).
        row.ToRow().Should().BeEquivalentTo(new DetailedProcedureRowDto(
            row.Id, row.ReferenceNumber, row.ProcedureTypeName, row.Category, row.Status, row.CreatedByDisplayName, row.SubmittedAt,
            row.CompletedAt, row.PersonDocument, row.PersonFullName, row.HasTransformation, row.TransformationDetail, row.IsLeasing,
            row.PaymentType, row.TransferType));
    }

    // ── helpers ───────────────────────────────────────────────────────────────────────────────

    private static NetworkDetailedReportRequest Request(Guid? child) => new(child, From, To);

    private static NetworkDetailedProcedureRowDto Row(Guid tenant) => new(
        tenant, $"Cliente {tenant:N}"[..12], Guid.NewGuid(), "000001", "Matrícula", "matriculas", "entregado", "Gestor",
        DateTimeOffset.UtcNow, null, "123", "Persona", false, null, false, "contado", null);
}
