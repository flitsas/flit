using Flit.Impuestos.Api.Modulos.Comun;
using Flit.Impuestos.Api.Modulos.Liquidaciones;
using Flit.Impuestos.Api.Persistence;
using Flit.Impuestos.Domain.Liquidaciones;
using Microsoft.EntityFrameworkCore;

namespace Flit.Impuestos.Api.Modulos.Estadisticas;

/// <summary>Una placa del rango, sin datos personales, con lo que el dashboard cuenta y suma.</summary>
internal sealed record FilaEstadistica(
    Guid Empresa,
    TipoLiquidacion Tipo,
    DateTimeOffset CreadaEn,
    EstadoItem Estado,
    bool ConCallback,
    string? ResultadoStatus,
    decimal? TotalAPagar,
    bool Parcial,
    string? DepartamentoCodigo,
    string? DepartamentoNombre);

/// <summary>
/// Indicadores del dashboard (plan §5.1, F4a-CF1..CF4, F4-CF5; esquema <c>Dashboard</c> de impuestos.v1.yaml 1.1.0). La unidad es la
/// placa liquidada (una individual cuenta 1; una masiva, sus placas), por la fecha de creación de su liquidación en hora de Colombia:
/// <list type="bullet">
///   <item><c>contadores</c>: total, individuales y masivas; <c>lotesEnProceso</c> son las masivas del rango en <c>CREADO</c> o
///   <c>EN_PROCESO</c>.</item>
///   <item><c>resultados</c> por la categoría del estado (plan §3.2). <c>ENVIADO</c> enviada con <c>callbackUrl</c> cuenta como
///   «pendiente de webhook»; enviada sin él y las que todavía no salen (<c>PENDIENTE</c>, <c>CONSULTANDO_RUNT</c>,
///   <c>REINTENTO_PROGRAMADO</c>, <c>ENVIANDO</c>), como «pendiente por consulta».</item>
///   <item><c>deuda</c>: exitosas con <c>con_deuda</c> y con <c>paz_y_salvo</c>.</item>
///   <item><c>montoTotal</c>: suma de <c>totalAPagar</c> sin los null; null si ninguna trae monto (delta 13, nunca 0).
///   <c>liquidacionesSinMonto</c>: exitosas sin monto. <c>parcial</c>: hay alguna sin monto o alguna consulta <c>partial</c>.</item>
///   <item><c>serie</c>: un punto por día del rango (con ceros); <c>porDepartamento</c>: las placas con departamento, la de más
///   placas primero, con su monto nullable (delta 10).</item>
///   <item><c>ranking</c>: solo en la vista global, las compañías con placas en el rango, la de más primero.</item>
/// </list>
/// <see cref="Calcular"/> es puro; <see cref="ObtenerAsync"/> carga las filas de la <see cref="FuenteLiquidaciones"/>.
/// </summary>
/// <example>
/// <code>
/// var dashboard = await CalculadoraEstadisticas.ObtenerAsync(fuente, alcance, rango, ct);
/// var puro = CalculadoraEstadisticas.Calcular(rango, filas, lotesEnProceso: 1, ultimosLotes: [], alcance);
/// </code>
/// </example>
internal static class CalculadoraEstadisticas
{
    public const int UltimosLotes = 5;

    public static async Task<DashboardDto> ObtenerAsync(FuenteLiquidaciones fuente, AlcanceLectura alcance, RangoDias rango, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(fuente);
        ArgumentNullException.ThrowIfNull(alcance);
        var inicio = rango.Inicio;
        var fin = rango.Fin;
        var lotesDelRango = fuente.Lotes.Where(l => l.CreadaEn >= inicio && l.CreadaEn < fin);
        var filas = await (
                from i in fuente.Items
                join l in lotesDelRango on i.LiquidacionId equals l.Id
                select new FilaEstadistica(l.TenantId, l.Tipo, l.CreadaEn, i.Estado, i.ConCallback, i.ResultadoStatus, i.TotalAPagar, i.Parcial,
                    i.DepartamentoCodigo, i.DepartamentoNombre))
            .ToListAsync(ct).ConfigureAwait(false);
        var enProceso = await lotesDelRango
            .CountAsync(l => l.Tipo == TipoLiquidacion.Masiva && (l.Estado == EstadoLote.Creado || l.Estado == EstadoLote.EnProceso), ct).ConfigureAwait(false);
        var ultimos = await lotesDelRango.OrderByDescending(l => l.CreadaEn).ThenByDescending(l => l.Id).Take(UltimosLotes).ToListAsync(ct).ConfigureAwait(false);
        var individuales = ultimos.Where(l => l.Tipo == TipoLiquidacion.Individual).Select(l => l.Id).ToList();
        var placas = individuales.Count == 0
            ? []
            : await fuente.Items.Where(i => individuales.Contains(i.LiquidacionId)).Select(i => new { i.LiquidacionId, i.Id, i.Placa, i.Estado })
                .ToListAsync(ct).ConfigureAwait(false);
        var porLote = placas.GroupBy(p => p.LiquidacionId).ToDictionary(g => g.Key, g => g.First());
        var resumenes = ultimos.Select(l =>
        {
            var unico = porLote.GetValueOrDefault(l.Id);
            return new ResumenLiquidacionDto(l.Id, CodigosDeEnum.Codigo(l.Tipo), CodigosDeEnum.Codigo(l.Estado), l.TotalItems,
                new ContadoresDto(l.CExitosas, l.CConDeuda, l.CPazYSalvo, l.CError, l.CDatosIncompletos, l.CDptoNoHabilitado, l.CIncierto, l.CSinResultado, l.CPendientes),
                l.CreadaEn, ConsultaLiquidaciones.CreadorDe(l), alcance.Referencia(l.TenantId), l.NombreArchivo,
                unico?.Placa, unico?.Id, unico is null ? null : CodigosDeEnum.Codigo(unico.Estado));
        }).ToList();
        return Calcular(rango, filas, enProceso, resumenes, alcance);
    }

    public static DashboardDto Calcular(
        RangoDias rango, IReadOnlyList<FilaEstadistica> filas, int lotesEnProceso, IReadOnlyList<ResumenLiquidacionDto> ultimosLotes, AlcanceLectura alcance)
    {
        ArgumentNullException.ThrowIfNull(filas);
        ArgumentNullException.ThrowIfNull(alcance);
        var individuales = filas.Count(f => f.Tipo == TipoLiquidacion.Individual);
        var exitosas = filas.Where(f => f.Estado == EstadoItem.Exitoso).ToList();
        var sinMonto = exitosas.Count(f => f.TotalAPagar is null);
        var conMonto = filas.Where(f => f.TotalAPagar is not null).ToList();

        var resultados = new ResultadosDashboardDto(
            Exitosas: exitosas.Count,
            ConError: Contar(filas, CategoriaDashboard.Error),
            PendientesWebhook: filas.Count(f => f.Estado == EstadoItem.Enviado && f.ConCallback),
            PendientesConsulta: filas.Count(f => EstadosItem.Categoria(f.Estado) == CategoriaDashboard.Pendiente && !(f.Estado == EstadoItem.Enviado && f.ConCallback)),
            DatosIncompletos: Contar(filas, CategoriaDashboard.DatosIncompletos),
            DepartamentoNoHabilitado: Contar(filas, CategoriaDashboard.DepartamentoNoHabilitado),
            Inciertas: Contar(filas, CategoriaDashboard.Incierto),
            SinResultado: Contar(filas, CategoriaDashboard.SinResultado));

        var porDia = filas.GroupBy(f => RangoDias.DiaDe(f.CreadaEn)).ToDictionary(g => g.Key, g => g.ToList());
        var serie = Enumerable.Range(0, rango.Dias).Select(n =>
        {
            var dia = rango.Desde.AddDays(n);
            var delDia = porDia.GetValueOrDefault(dia) ?? [];
            var ind = delDia.Count(f => f.Tipo == TipoLiquidacion.Individual);
            return new PuntoSerieDto(dia, delDia.Count, ind, delDia.Count - ind);
        }).ToList();

        var departamentos = filas.Where(f => !string.IsNullOrEmpty(f.DepartamentoCodigo))
            .GroupBy(f => f.DepartamentoCodigo!, StringComparer.Ordinal)
            .Select(g => new DepartamentoDashboardDto(
                g.Key, g.Select(f => f.DepartamentoNombre).FirstOrDefault(n => !string.IsNullOrWhiteSpace(n)) ?? g.Key, g.Count(), Suma(g)))
            .OrderByDescending(d => d.Total).ThenBy(d => d.Codigo, StringComparer.Ordinal)
            .ToList();

        IReadOnlyList<RankingDashboardDto>? ranking = alcance.Global
            ? [.. filas.GroupBy(f => f.Empresa).Select(g => new RankingDashboardDto(alcance.Referencia(g.Key), g.Count()))
                .OrderByDescending(r => r.Total).ThenBy(r => r.Empresa.Nombre, StringComparer.Ordinal).ThenBy(r => r.Empresa.Id)]
            : null;

        return new DashboardDto(
            new PeriodoDashboardDto(rango.Desde, rango.Hasta),
            new ContadoresDashboardDto(filas.Count, individuales, filas.Count - individuales, lotesEnProceso),
            resultados,
            new DeudaDashboardDto(
                exitosas.Count(f => f.ResultadoStatus == "con_deuda"),
                exitosas.Count(f => f.ResultadoStatus == "paz_y_salvo")),
            conMonto.Count == 0 ? null : conMonto.Sum(f => f.TotalAPagar!.Value),
            sinMonto,
            sinMonto > 0 || exitosas.Any(f => f.Parcial),
            serie,
            departamentos,
            ultimosLotes,
            ranking);
    }

    private static int Contar(IReadOnlyList<FilaEstadistica> filas, CategoriaDashboard categoria) => filas.Count(f => EstadosItem.Categoria(f.Estado) == categoria);

    private static decimal? Suma(IEnumerable<FilaEstadistica> filas)
    {
        var montos = filas.Where(f => f.TotalAPagar is not null).Select(f => f.TotalAPagar!.Value).ToList();
        return montos.Count == 0 ? null : montos.Sum();
    }
}
