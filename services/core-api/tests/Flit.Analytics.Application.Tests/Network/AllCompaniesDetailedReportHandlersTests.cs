using Flit.Analytics.Application.Abstractions;
using Flit.Analytics.Application.Dtos;
using Flit.Analytics.Application.Queries;
using Flit.Analytics.Application.Queries.Network;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Analytics.Application.Tests.Network;

/// <summary>
/// HU #14319 / HU #14320 (Épica #14296) — filtros de canal y modo «todas las compañías» sin base de datos:
/// el canal y el OT del canal viajan del pedido al filtro (listado, exportación y red), el universo del modo
/// «todas las compañías» sale del repositorio y un rango invertido se rechaza sin consultar.
/// Uso de ejemplo: <c>new GetAllCompaniesDetailedProceduresHandler(repo).HandleAsync(new(null, from, to), 1, 20)</c>.
/// </summary>
public sealed class AllCompaniesDetailedReportHandlersTests
{
    private static readonly Guid A = Guid.Parse("a0000000-0000-4000-8000-000000000001");
    private static readonly Guid B = Guid.Parse("a0000000-0000-4000-8000-000000000002");
    private static readonly Guid Office = Guid.Parse("a0000000-0000-4000-8000-0000000000f1");
    private static readonly DateOnly From = new(2026, 9, 1);
    private static readonly DateOnly To = new(2026, 9, 14);

    private readonly INetworkDetailedReportReadRepository _repo = Substitute.For<INetworkDetailedReportReadRepository>();

    public AllCompaniesDetailedReportHandlersTests()
    {
        _repo.ListAllTenantIdsAsync(Arg.Any<CancellationToken>()).Returns(call => (IReadOnlySet<Guid>)new HashSet<Guid> { A, B });
        _repo.GetNetworkProceduresAsync(Arg.Any<NetworkDetailedReportFilter>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var summary = new NetworkDetailedReportSummaryDto(0, [], [], [], []);
                return new NetworkAnalyticsResult<NetworkDetailedProceduresPageDto>(
                    new NetworkDetailedProceduresPageDto([], 0, call.ArgAt<int>(1), call.ArgAt<int>(2), summary, new NetworkScopeDto([])), [A, B]);
            });
    }

    [Fact]
    public async Task Todas_las_companias_usa_el_universo_del_repositorio_y_normaliza_los_filtros_como_la_red()
    {
        var request = new NetworkDetailedReportRequest(Guid.NewGuid(), From, To, Category: " Matriculas ", PersonName: "  ")
        {
            Channel = " pn_web ",
            ChannelTransitOfficeId = Office,
        };

        var (resultado, error) = await new GetAllCompaniesDetailedProceduresHandler(_repo).HandleAsync(request, 0, 5000, TestContext.Current.CancellationToken);

        error.Should().BeNull();
        resultado!.ReachedTenantIds.Should().BeEquivalentTo([A, B]);
        await _repo.Received(1).GetNetworkProceduresAsync(
            Arg.Is<NetworkDetailedReportFilter>(f => f.TenantIds.SetEquals(new[] { A, B }) && f.Channel == "pn_web" && f.ChannelTransitOfficeId == Office
                && f.Category == "matriculas" && f.PersonName == null),
            1, GetDetailedProceduresHandler.MaxPageSize, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Un_rango_invertido_se_rechaza_sin_consultar_el_universo_ni_los_datos()
    {
        var (resultado, error) = await new GetAllCompaniesDetailedProceduresHandler(_repo)
            .HandleAsync(new NetworkDetailedReportRequest(null, To, From), 1, 20, TestContext.Current.CancellationToken);
        var (filtro, errorExport) = await new ExportAllCompaniesDetailedProceduresHandler(_repo)
            .ResolveAsync(new NetworkDetailedReportRequest(null, To, From), TestContext.Current.CancellationToken);

        resultado.Should().BeNull();
        filtro.Should().BeNull();
        error.Should().Be(NetworkAnalyticsScope.InvalidRange);
        errorExport.Should().Be(NetworkAnalyticsScope.InvalidRange);
        await _repo.DidNotReceiveWithAnyArgs().ListAllTenantIdsAsync(TestContext.Current.CancellationToken);
        await _repo.DidNotReceiveWithAnyArgs().GetNetworkProceduresAsync(default!, default, default, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Listado_y_exportacion_resuelven_el_mismo_filtro_para_la_misma_peticion()
    {
        var request = new NetworkDetailedReportRequest(null, From, To, Status: "entregado") { Channel = "integracion" };

        await new GetAllCompaniesDetailedProceduresHandler(_repo).HandleAsync(request, 1, 20, TestContext.Current.CancellationToken);
        var (exportFilter, _) = await new ExportAllCompaniesDetailedProceduresHandler(_repo).ResolveAsync(request, TestContext.Current.CancellationToken);

        var listFilter = (NetworkDetailedReportFilter)_repo.ReceivedCalls()
            .Single(c => c.GetMethodInfo().Name == nameof(INetworkDetailedReportReadRepository.GetNetworkProceduresAsync)).GetArguments()[0]!;
        exportFilter.Should().BeEquivalentTo(listFilter);
    }

    [Fact]
    public async Task Los_handlers_de_siempre_pasan_canal_y_OT_al_filtro()
    {
        var std = Substitute.For<IDetailedReportReadRepository>();
        std.GetProceduresAsync(Arg.Any<DetailedReportFilter>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new DetailedProceduresPageDto([], 0, 1, 20, new DetailedReportSummaryDto(0, [], [], [])));

        await new GetDetailedProceduresHandler(std).HandleAsync(
            new GetDetailedProceduresQuery(A, From, To, null, null, null, null, null, null, null, null, null, 1, 20)
            { Channel = " pn_punto ", ChannelTransitOfficeId = Office },
            TestContext.Current.CancellationToken);
        var (export, _) = (ExportDetailedProceduresHandler.Validate(
            new ExportDetailedProceduresQuery(A, From, To, null, null, null, null, null, null, null, null, null)
            { Channel = "pn_punto", ChannelTransitOfficeId = Office }));

        await std.Received(1).GetProceduresAsync(
            Arg.Is<DetailedReportFilter>(f => f.Channel == "pn_punto" && f.ChannelTransitOfficeId == Office), 1, 20, Arg.Any<CancellationToken>());
        export!.Channel.Should().Be("pn_punto");
        export.ChannelTransitOfficeId.Should().Be(Office);
    }
}
