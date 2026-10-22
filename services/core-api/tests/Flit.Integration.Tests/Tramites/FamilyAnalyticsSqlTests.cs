using Flit.Analytics.Application.Dtos;
using Flit.Analytics.Application.Queries.Metrics;
using Flit.Infrastructure.Persistence.Entities.Identity;
using Flit.Infrastructure.Persistence.Repositories;
using Flit.Integration.Tests.Postgres;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Tramites.Estados;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Flit.Integration.Tests.Tramites;

/// <summary>
/// HU #13664 (Feature #13636 F-F, Épica #12753, criterio FF-4) contra PostgreSQL REAL — el predicado <c>@families</c>
/// (<c>AnalyticsFamilyFilter</c>) de las lecturas analíticas: overview del dashboard/Reportes, detalle de trámites,
/// tendencia mensual, panorama en vivo, funnel, reporte detallado y overview de red. <c>null</c> = sin restricción,
/// lista = solo esas familias (por el TIPO, ADR-0050, sin distinguir mayúsculas), vacío = ninguna.
/// <para>Uso de ejemplo: <c>repo.GetOverviewAsync(cliente, null, null, ["MATRICULAS", "OTROS"])</c> no trae la categoría <c>traspasos</c>.</para>
/// </summary>
public sealed class FamilyAnalyticsSqlTests(PostgresDatabaseFixture fixture) : PostgresTestBase(fixture)
{
    private static readonly Guid Cliente = new("5a5a5a5a-0001-4000-8000-0000000136b2");
    private static readonly Guid Gestor = new("5a5a5a5a-0002-4000-8000-0000000136b2");
    private static readonly IReadOnlyList<string> SinTraspasos = ["MATRICULAS", "otros"];
    private static readonly DateOnly Desde = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-10));
    private static readonly DateOnly Hasta = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1));

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [PostgresFact]
    public async Task Overview_y_detalle_excluyen_la_familia_sin_Ver_y_sin_restriccion_no_cambian()
    {
        await SembrarAsync();
        await using var ctx = NewContext();
        var repo = new AnalyticsReadRepository(ctx);

        Categorias(await repo.GetOverviewAsync(Cliente, null, null, null, Ct)).Should().BeEquivalentTo(["matriculas", "traspasos", "otros"]);
        Categorias(await repo.GetOverviewAsync(Cliente, null, null, SinTraspasos, Ct)).Should().BeEquivalentTo(["matriculas", "otros"]);
        (await repo.GetOverviewAsync(Cliente, null, null, [], Ct)).Should().BeEmpty("vacío = ninguna familia visible");
        Categorias(await repo.GetOverviewAsync(null, null, null, SinTraspasos, Ct)).Should().NotContain("traspasos", "también en la vista global");

        var todo = await repo.GetProcedureDetailsAsync(Cliente, Desde, Hasta, null, null, 1, 50, null, Ct);
        todo.TotalCount.Should().Be(3);
        var filtrado = await repo.GetProcedureDetailsAsync(Cliente, Desde, Hasta, null, null, 1, 50, SinTraspasos, Ct);
        filtrado.TotalCount.Should().Be(2);
        filtrado.Items.Select(i => i.Category).Should().BeEquivalentTo(["matriculas", "otros"]);

        var exportadas = new List<string>();
        await repo.ExportProcedureDetailsAsync(Cliente, Desde, Hasta, null, null, (row, _) =>
        {
            exportadas.Add(row.Category);
            return Task.CompletedTask;
        }, SinTraspasos, Ct);
        exportadas.Should().BeEquivalentTo(["matriculas", "otros"], "la exportación sale del mismo predicado");

        (await repo.GetMonthlyTrendAsync(Cliente, Desde, Hasta, SinTraspasos, Ct)).Select(p => p.Category).Should().NotContain("traspasos");
        (await repo.GetTopProducersAsync(Cliente, Desde, Hasta, 5, SinTraspasos, Ct)).Should().BeEmpty("prepara la consulta con @families tipado aunque no haya radicaciones");
    }

    [PostgresFact]
    public async Task Metricas_y_reporte_detallado_excluyen_la_familia_sin_Ver()
    {
        await SembrarAsync();
        await using var ctx = NewContext();
        var metricas = new AnalyticsMetricsReadRepository(ctx);

        // Estado actual de los activos (no depende de que «hoy» en Bogotá coincida con la siembra).
        (await metricas.GetLiveOverviewAsync(Cliente, 7, null, Ct)).Today.ByStatus.Sum(s => s.Count).Should().Be(3);
        (await metricas.GetLiveOverviewAsync(Cliente, 7, SinTraspasos, Ct)).Today.ByStatus.Sum(s => s.Count).Should().Be(2);

        var filtro = new MetricsFilter(Cliente, Desde, Hasta);
        (await metricas.GetFunnelAsync(filtro, Ct)).States[0].Count.Should().Be(3);
        (await metricas.GetFunnelAsync(filtro with { AllowedFamilies = SinTraspasos }, Ct)).States[0].Count.Should().Be(2);
        (await metricas.GetOtMetricsAsync(filtro with { AllowedFamilies = SinTraspasos }, Ct)).Should().NotBeNull("el comando multi-statement prepara @families en todos sus CTE");

        var detallado = new DetailedReportReadRepository(ctx);
        var reporte = new DetailedReportFilter(Cliente, Desde, Hasta, null, null, null, null, null, null, null, null, null);
        var completo = await detallado.GetProceduresAsync(reporte, 1, 50, Ct);
        var sinTraspasos = await detallado.GetProceduresAsync(reporte with { AllowedFamilies = SinTraspasos }, 1, 50, Ct);
        sinTraspasos.Items.Should().NotContain(i => i.Category == "traspasos");
        sinTraspasos.Summary.ByCategory.Select(c => c.Label).Should().NotContain("traspasos");
        sinTraspasos.TotalCount.Should().Be(completo.TotalCount - completo.Items.Count(i => i.Category == "traspasos"));

        var red = new AnalyticsNetworkReadRepository(ctx);
        var overviewRed = await red.GetNetworkOverviewAsync(new HashSet<Guid> { Cliente }, null, null, SinTraspasos, Ct);
        Categorias(overviewRed.Items).Should().BeEquivalentTo(["matriculas", "otros"]);
    }

    private static List<string> Categorias(IReadOnlyList<CategoryMetricsDto> items) => [.. items.Select(c => c.Category)];

    private async Task SembrarAsync()
    {
        await using (var ctx = NewContext())
        {
            ctx.Tenants.Add(TenantSeed.New(Cliente, "IT-136B2", isGroupParent: false, parentId: null));
            await ctx.SaveChangesAsync();
            ctx.Users.Add(new User
            {
                Id = Gestor,
                Email = "it-136b2@flit.test",
                DisplayName = "Gestor 136B2",
                Status = "active",
                HomeTenantId = Cliente,
                CreatedAt = DateTimeOffset.UtcNow,
            });
            await ctx.SaveChangesAsync();
        }

        await using (var ctx = NewContext())
        {
            async Task<Guid> TipoDe(string familia) => await ctx.ProcedureTypes.AsNoTracking()
                .Where(t => t.Family == familia).Select(t => t.Id).FirstAsync();

            ctx.ProcedureInstances.AddRange(
                Tramite(1, await TipoDe("MATRICULAS")),
                Tramite(2, await TipoDe("TRASPASO")),
                Tramite(3, await TipoDe("OTROS")));
            await ctx.SaveChangesAsync();
        }
    }

    private static ProcedureInstance Tramite(int n, Guid tipo) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = Cliente,
        ProcedureTypeId = tipo,
        ReferenceNumber = $"IT136B2-{n}",
        Status = TramiteEstado.Borrador,
        Vin = $"VIN136B2{n:D9}",
        CreatedByUserId = Gestor,
        CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-n),
    };
}
