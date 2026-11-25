using System.Collections.Concurrent;
using System.Data.Common;
using Flit.Impuestos.Api.Modulos.Backoffice;
using Flit.Impuestos.Api.Modulos.Comun;
using Flit.Impuestos.Api.Modulos.Credenciales;
using Flit.Impuestos.Api.Modulos.Estadisticas;
using Flit.Impuestos.Api.Modulos.Liquidaciones;
using Flit.Impuestos.Api.Persistence;
using Flit.Impuestos.Api.Plataforma;
using Flit.Impuestos.Domain.Liquidaciones;
using Flit.Impuestos.Tests.Api;
using Flit.Impuestos.Tests.Reportes;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using E = Flit.Impuestos.Tests.Reportes.EntornoReportes;

namespace Flit.Impuestos.Tests.Estadisticas;

/// <summary>
/// HU #14694 (corrección 2 del cierre) — el dashboard agrega en la base y no en memoria: sobre el Postgres real (5433 en local),
/// <see cref="CalculadoraEstadisticas.ObtenerAsync"/> no trae a memoria las placas con su monto, su resultado, su departamento ni
/// su <c>callbackUrl</c>: toda consulta de <c>liquidacion_items</c> que lee esas columnas lleva <c>GROUP BY</c>. Los valores son
/// los del contrato <c>Dashboard</c> (1.1.0) en la vista de una compañía y en la global del Super Admin, y la serie cuenta el día
/// de Colombia (UTC−5) también en los bordes: las 23:30 de un día no caen en el siguiente y las 00:00 de <c>desde</c> cuentan.
/// </summary>
/// <example>
/// <code>
/// var captura = new CapturaSql();
/// await using var fuente = Fuente(alcance, captura);
/// var d = await CalculadoraEstadisticas.ObtenerAsync(fuente, alcance, rango, ct);
/// captura.DeItems().Should().OnlyContain(sql => sql.Contains("GROUP BY"));
/// </code>
/// </example>
public sealed class EstadisticasEnSqlTests(ApiReportesFixture fixture) : IClassFixture<ApiReportesFixture>
{
    private static readonly string[] ColumnasDePlaca = ["total_a_pagar", "resultado_status", "departamento_codigo", "departamento_nombre", "con_callback", "parcial"];

    private ApiDePrueba Api => fixture.Api;

    [Fact]
    public async Task Una_compania_agrega_en_la_base_con_los_valores_del_contrato()
    {
        var ct = Preparar();
        var empresa = await EmpresaAsync(ct);
        var hoy = RangoDias.Hoy(TimeProvider.System);
        var rango = new RangoDias(hoy.AddDays(-6), hoy);
        await E.SembrarAsync(Api, empresa, TipoLiquidacion.Masiva, Guid.NewGuid(), ct, null,
            new ItemSemilla("SQL001", ResultadoStatus: "con_deuda", Total: 100m, Departamento: "25", DepartamentoNombre: "Cundinamarca"),
            new ItemSemilla("SQL002", ResultadoStatus: "con_deuda", Total: 50.25m, Departamento: "25", DepartamentoNombre: "Cundinamarca"),
            new ItemSemilla("SQL003", ResultadoStatus: "con_deuda", Total: null, Departamento: "05", DepartamentoNombre: "Antioquia"),
            new ItemSemilla("SQL004", ResultadoStatus: "paz_y_salvo", Total: 0m, Parcial: true, Departamento: "05", DepartamentoNombre: "Antioquia"),
            new ItemSemilla("SQL005", EstadoItem.Enviado, ConCallback: true, Departamento: "11", DepartamentoNombre: "Bogotá"),
            new ItemSemilla("SQL006", EstadoItem.Enviado),
            new ItemSemilla("SQL007", EstadoItem.ConsultandoRunt),
            new ItemSemilla("SQL008", EstadoItem.Error, ErrorCodigo: "portal_unavailable"),
            new ItemSemilla("SQL009", EstadoItem.InciertoRequiereDecision),
            new ItemSemilla("SQL010", EstadoItem.DatosIncompletos, CampoFaltante: "vehicleModel"),
            new ItemSemilla("SQL011", EstadoItem.DepartamentoNoHabilitado),
            new ItemSemilla("SQL012", EstadoItem.SinResultadoLoteCerrado));
        var x = hoy.AddDays(-2);
        await E.SembrarAsync(Api, empresa, TipoLiquidacion.Individual, Guid.NewGuid(), ct, Colombia(x, 23, 30),
            new ItemSemilla("SQL013", ResultadoStatus: "con_deuda", Total: 7m, Departamento: "25", DepartamentoNombre: "Cundinamarca"));
        await E.SembrarAsync(Api, empresa, TipoLiquidacion.Individual, Guid.NewGuid(), ct, Colombia(rango.Desde, 0, 0),
            new ItemSemilla("SQL014", EstadoItem.Pendiente));
        await E.SembrarAsync(Api, empresa, TipoLiquidacion.Individual, Guid.NewGuid(), ct, Colombia(rango.Desde, 0, 0).AddMinutes(-1),
            new ItemSemilla("FUERA01", ResultadoStatus: "con_deuda", Total: 999m, Departamento: "25"));
        var alcance = new AlcanceLectura([empresa], false, new Dictionary<Guid, ReferenciaDto> { [empresa] = new(empresa, "Empresa de prueba") });
        var captura = new CapturaSql();

        DashboardDto d;
        await using (var fuente = Fuente(alcance, captura))
            d = await CalculadoraEstadisticas.ObtenerAsync(fuente, alcance, rango, ct);

        captura.DeItems().Should().NotBeEmpty();
        captura.DeItems().Where(sql => ColumnasDePlaca.Any(c => sql.Contains(c, StringComparison.Ordinal)))
            .Should().OnlyContain(sql => sql.Contains("GROUP BY", StringComparison.Ordinal), "las placas se agregan en la base, no en memoria");

        d.Periodo.Should().Be(new PeriodoDashboardDto(rango.Desde, rango.Hasta));
        d.Contadores.Should().Be(new ContadoresDashboardDto(14, 2, 12, 1));
        d.Resultados.Should().Be(new ResultadosDashboardDto(
            Exitosas: 5, ConError: 1, PendientesWebhook: 1, PendientesConsulta: 3, DatosIncompletos: 1, DepartamentoNoHabilitado: 1, Inciertas: 1, SinResultado: 1));
        d.Deuda.Should().Be(new DeudaDashboardDto(4, 1));
        d.MontoTotal.Should().Be(157.25m, "la suma no cuenta el null como 0 y no incluye lo de antes de desde");
        d.LiquidacionesSinMonto.Should().Be(1);
        d.Parcial.Should().BeTrue();
        d.Ranking.Should().BeNull();

        d.Serie.Should().HaveCount(7);
        d.Serie.Should().OnlyContain(p => p.Total == p.Individuales + p.Masivas);
        d.Serie.Single(p => p.Fecha == x).Should().Be(new PuntoSerieDto(x, 1, 1, 0), "las 23:30 de Colombia son de ese día aunque en UTC ya sea el siguiente");
        d.Serie.Single(p => p.Fecha == x.AddDays(1)).Total.Should().Be(0);
        d.Serie.Single(p => p.Fecha == rango.Desde).Should().Be(new PuntoSerieDto(rango.Desde, 1, 1, 0), "las 00:00 de desde cuentan");
        d.Serie.Single(p => p.Fecha == hoy).Should().Be(new PuntoSerieDto(hoy, 12, 0, 12));

        d.PorDepartamento.Should().Equal(
            new DepartamentoDashboardDto("25", "Cundinamarca", 3, 157.25m),
            new DepartamentoDashboardDto("05", "Antioquia", 2, 0m),
            new DepartamentoDashboardDto("11", "Bogotá", 1, null));

        d.UltimosLotes.Should().HaveCount(3);
        d.UltimosLotes[0].Tipo.Should().Be("MASIVA");
        d.UltimosLotes[1].Placa.Should().Be("SQL013");
        d.UltimosLotes[2].Placa.Should().Be("SQL014");
    }

    [Fact]
    public async Task La_vista_global_agrega_en_la_base_con_el_ranking_de_las_companias()
    {
        var ct = Preparar();
        var a = await EmpresaAsync(ct);
        var b = await EmpresaAsync(ct);
        var hoy = RangoDias.Hoy(TimeProvider.System);
        var rango = new RangoDias(hoy.AddDays(-1), hoy);
        await E.SembrarAsync(Api, a, TipoLiquidacion.Masiva, Guid.NewGuid(), ct, null,
            new ItemSemilla("GLA001", ResultadoStatus: "con_deuda", Total: 10m, Departamento: "25", DepartamentoNombre: "Cundinamarca"),
            new ItemSemilla("GLA002", ResultadoStatus: "con_deuda", Total: 20m, Departamento: "25", DepartamentoNombre: "Cundinamarca"));
        await E.SembrarAsync(Api, b, TipoLiquidacion.Individual, Guid.NewGuid(), ct, null,
            new ItemSemilla("GLB001", ResultadoStatus: "paz_y_salvo", Total: 0m, Departamento: "25", DepartamentoNombre: "Cundinamarca"));
        var referencias = new Dictionary<Guid, ReferenciaDto> { [a] = new(a, "Alfa"), [b] = new(b, "Beta") };
        var alcance = new AlcanceLectura([a, b], true, referencias);
        var captura = new CapturaSql();

        DashboardDto d;
        await using (var fuente = Fuente(alcance, captura))
            d = await CalculadoraEstadisticas.ObtenerAsync(fuente, alcance, rango, ct);

        captura.DeItems().Where(sql => ColumnasDePlaca.Any(c => sql.Contains(c, StringComparison.Ordinal)))
            .Should().OnlyContain(sql => sql.Contains("GROUP BY", StringComparison.Ordinal));
        d.Contadores.Should().Be(new ContadoresDashboardDto(3, 1, 2, 0));
        d.MontoTotal.Should().Be(30m);
        d.Deuda.Should().Be(new DeudaDashboardDto(2, 1));
        d.PorDepartamento.Should().Equal(new DepartamentoDashboardDto("25", "Cundinamarca", 3, 30m));
        d.Ranking.Should().Equal(new RankingDashboardDto(new ReferenciaDto(a, "Alfa"), 2), new RankingDashboardDto(new ReferenciaDto(b, "Beta"), 1));
        d.UltimosLotes.Select(l => l.Empresa.Id).Should().BeEquivalentTo([a, b]);
    }

    private static DateTimeOffset Colombia(DateOnly dia, int hora, int minuto) =>
        new DateTimeOffset(dia.ToDateTime(new TimeOnly(hora, minuto)), RangoDias.OffsetColombia).ToUniversalTime();

    private FuenteLiquidaciones Fuente(AlcanceLectura alcance, CapturaSql captura)
    {
        var opciones = new DbContextOptionsBuilder<ImpuestosDb>();
        ImpuestosDb.Configure(opciones, Api.BaseDeDatos);
        opciones.AddInterceptors(captura);
        var proteccion = Api.App.Services.GetRequiredService<IProteccionImpuestos>();
        var almacen = new AlmacenCredenciales(opciones.Options, proteccion, TimeProvider.System);
        var lectura = new LecturaEntreEmpresas(new ImpuestosDb(opciones.Options, new EmpresaFija(null)));
        return FuenteLiquidaciones.Abrir(alcance, almacen, lectura);
    }

    private async Task<Guid> EmpresaAsync(CancellationToken ct)
    {
        var empresa = Guid.NewGuid();
        await E.SembrarEmpresaAsync(Api, empresa, ct);
        return empresa;
    }

    private CancellationToken Preparar()
    {
        Assert.SkipWhen(Api.Omitir is not null, Api.Omitir ?? string.Empty);
        return TestContext.Current.CancellationToken;
    }

    /// <summary>Guarda el SQL de cada lectura (lector y escalar) del contexto.</summary>
    private sealed class CapturaSql : DbCommandInterceptor
    {
        private readonly ConcurrentQueue<string> _sql = new();

        public IReadOnlyList<string> DeItems() => [.. _sql.Where(s => s.Contains("liquidacion_items", StringComparison.Ordinal))];

        public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
        {
            _sql.Enqueue(command.CommandText);
            return result;
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            _sql.Enqueue(command.CommandText);
            return ValueTask.FromResult(result);
        }

        public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<object> result, CancellationToken cancellationToken = default)
        {
            _sql.Enqueue(command.CommandText);
            return ValueTask.FromResult(result);
        }
    }
}
