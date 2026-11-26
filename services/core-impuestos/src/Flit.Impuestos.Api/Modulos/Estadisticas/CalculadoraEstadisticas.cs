using Flit.Impuestos.Api.Modulos.Comun;
using Flit.Impuestos.Api.Modulos.Liquidaciones;
using Flit.Impuestos.Api.Persistence;
using Flit.Impuestos.Domain.Liquidaciones;
using Microsoft.EntityFrameworkCore;

namespace Flit.Impuestos.Api.Modulos.Estadisticas;

/// <summary>
/// Placas del rango agregadas en la base por tipo de liquidación, estado y <c>callbackUrl</c>: cuántas son, cuántas con deuda o al
/// día, cuántas con y sin monto, la suma de los montos (sin los null) y cuántas vienen <c>partial</c>.
/// </summary>
internal sealed record GrupoEstado(
    TipoLiquidacion Tipo, EstadoItem Estado, bool ConCallback, int Total, int ConDeuda, int PazYSalvo, int ConMonto, decimal Monto, int SinMonto, int Parciales);

/// <summary>Placas de un día del rango (en hora de Colombia, <c>Dia</c> = días desde <c>desde</c>) y tipo de liquidación.</summary>
internal sealed record GrupoDia(int Dia, TipoLiquidacion Tipo, int Total);

/// <summary>Placas de un departamento con el primer nombre no vacío (o null), cuántas traen monto y su suma.</summary>
internal sealed record GrupoDepartamento(string Codigo, string? Nombre, int Total, int ConMonto, decimal Monto);

/// <summary>Placas del rango de una compañía (ranking de la vista global).</summary>
internal sealed record GrupoEmpresa(Guid Empresa, int Total);

/// <summary>Lo que <see cref="CalculadoraEstadisticas.ObtenerAsync"/> agrega en la base y <see cref="CalculadoraEstadisticas.Calcular"/> arma.</summary>
internal sealed record AgregadosEstadistica(
    IReadOnlyList<GrupoEstado> PorEstado,
    IReadOnlyList<GrupoDia> PorDia,
    IReadOnlyList<GrupoDepartamento> PorDepartamento,
    IReadOnlyList<GrupoEmpresa>? PorEmpresa);

/// <summary>
/// Indicadores del dashboard (plan §5.1, F4a-CF1..CF4, F4-CF5; esquema <c>Dashboard</c> de impuestos.v1.yaml 1.1.0). La unidad es la
/// placa liquidada (una individual cuenta 1; una masiva, sus placas), por la fecha de creación de su liquidación en hora de Colombia:
/// <list type="bullet">
///   <item><c>contadores</c>: total, individuales y masivas; <c>lotesEnProceso</c> son las masivas del rango en <c>CREADO</c> o
///   <c>EN_PROCESO</c>.</item>
///   <item><c>resultados</c> por la categoría del estado (plan §3.2); <c>noAplica</c> (1.3.0) son las de servicio público. <c>ENVIADO</c> enviada con <c>callbackUrl</c> cuenta como
///   «pendiente de webhook»; enviada sin él y las que todavía no salen (<c>PENDIENTE</c>, <c>CONSULTANDO_RUNT</c>,
///   <c>REINTENTO_PROGRAMADO</c>, <c>ENVIANDO</c>), como «pendiente por consulta».</item>
///   <item><c>deuda</c>: exitosas con <c>con_deuda</c> y con <c>paz_y_salvo</c>.</item>
///   <item><c>montoTotal</c>: suma de <c>totalAPagar</c> sin los null; null si ninguna trae monto (delta 13, nunca 0).
///   <c>liquidacionesSinMonto</c>: exitosas sin monto. <c>parcial</c>: hay alguna sin monto o alguna consulta <c>partial</c>.</item>
///   <item><c>serie</c>: un punto por día del rango (con ceros); <c>porDepartamento</c>: las placas con departamento, la de más
///   placas primero, con su monto nullable (delta 10).</item>
///   <item><c>ranking</c>: solo en la vista global, las compañías con placas en el rango, la de más primero.</item>
/// </list>
/// <see cref="ObtenerAsync"/> agrega en la base (<c>GROUP BY</c> con <c>COUNT</c>/<c>SUM</c>, traducido por EF Core): a memoria solo
/// llegan los grupos (estado × tipo × callback, días, departamentos y compañías), nunca las placas. El día se cuenta en hora de
/// Colombia como días enteros desde las 00:00 de <c>desde</c> (UTC−5 fijo, igual que <see cref="RangoDias"/>). El filtro por
/// empresa es el de la <see cref="FuenteLiquidaciones"/> (el de <c>TenantFilter</c> con una compañía; el de las auditadas en la
/// vista global). <see cref="Calcular"/> es puro y arma el contrato con esos grupos.
/// </summary>
/// <example>
/// <code>
/// var dashboard = await CalculadoraEstadisticas.ObtenerAsync(fuente, alcance, rango, ct);
/// var puro = CalculadoraEstadisticas.Calcular(rango, agregados, lotesEnProceso: 1, ultimosLotes: [], alcance);
/// </code>
/// </example>
internal static class CalculadoraEstadisticas
{
    public const int UltimosLotes = 5;

    private const double SegundosPorDia = 86400d;

    public static async Task<DashboardDto> ObtenerAsync(FuenteLiquidaciones fuente, AlcanceLectura alcance, RangoDias rango, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(fuente);
        ArgumentNullException.ThrowIfNull(alcance);
        var inicio = rango.Inicio;
        var fin = rango.Fin;
        var lotesDelRango = fuente.Lotes.Where(l => l.CreadaEn >= inicio && l.CreadaEn < fin);
        var placas =
            from i in fuente.Items
            join l in lotesDelRango on i.LiquidacionId equals l.Id
            select new
            {
                l.TenantId,
                l.Tipo,
                l.CreadaEn,
                i.Estado,
                i.ConCallback,
                i.ResultadoStatus,
                i.TotalAPagar,
                i.Parcial,
                i.DepartamentoCodigo,
                i.DepartamentoNombre,
            };

        var porEstado = await placas
            .GroupBy(p => new { p.Tipo, p.Estado, p.ConCallback })
            .Select(g => new GrupoEstado(
                g.Key.Tipo,
                g.Key.Estado,
                g.Key.ConCallback,
                g.Count(),
                g.Count(p => p.ResultadoStatus == "con_deuda"),
                g.Count(p => p.ResultadoStatus == "paz_y_salvo"),
                g.Count(p => p.TotalAPagar != null),
                g.Sum(p => p.TotalAPagar ?? 0m),
                g.Count(p => p.TotalAPagar == null),
                g.Count(p => p.Parcial)))
            .ToListAsync(ct).ConfigureAwait(false);

        // Días enteros desde las 00:00 de Colombia de «desde»: toda fila es >= inicio, así que el piso es el día del rango.
        var porDia = await placas
            .GroupBy(p => new { Dia = (int)Math.Floor((p.CreadaEn - inicio).TotalSeconds / SegundosPorDia), p.Tipo })
            .Select(g => new GrupoDia(g.Key.Dia, g.Key.Tipo, g.Count()))
            .ToListAsync(ct).ConfigureAwait(false);

        var porDepartamento = await placas
            .Where(p => p.DepartamentoCodigo != null && p.DepartamentoCodigo != string.Empty)
            .GroupBy(p => p.DepartamentoCodigo!)
            .Select(g => new GrupoDepartamento(
                g.Key,
                g.Max(p => string.IsNullOrWhiteSpace(p.DepartamentoNombre) ? null : p.DepartamentoNombre),
                g.Count(),
                g.Count(p => p.TotalAPagar != null),
                g.Sum(p => p.TotalAPagar ?? 0m)))
            .ToListAsync(ct).ConfigureAwait(false);

        var porEmpresa = alcance.Global
            ? await placas.GroupBy(p => p.TenantId).Select(g => new GrupoEmpresa(g.Key, g.Count())).ToListAsync(ct).ConfigureAwait(false)
            : null;

        var enProceso = await lotesDelRango
            .CountAsync(l => l.Tipo == TipoLiquidacion.Masiva && (l.Estado == EstadoLote.Creado || l.Estado == EstadoLote.EnProceso), ct).ConfigureAwait(false);
        var ultimos = await lotesDelRango.OrderByDescending(l => l.CreadaEn).ThenByDescending(l => l.Id).Take(UltimosLotes).ToListAsync(ct).ConfigureAwait(false);
        var individuales = ultimos.Where(l => l.Tipo == TipoLiquidacion.Individual).Select(l => l.Id).ToList();
        var unicas = individuales.Count == 0
            ? []
            : await fuente.Items.Where(i => individuales.Contains(i.LiquidacionId)).Select(i => new { i.LiquidacionId, i.Id, i.Placa, i.Estado, i.ErrorCodigo, i.ErrorMensaje })
                .ToListAsync(ct).ConfigureAwait(false);
        var porLote = unicas.GroupBy(p => p.LiquidacionId).ToDictionary(g => g.Key, g => g.First());
        var resumenes = ultimos.Select(l =>
        {
            var unico = porLote.GetValueOrDefault(l.Id);
            return new ResumenLiquidacionDto(l.Id, CodigosDeEnum.Codigo(l.Tipo), CodigosDeEnum.Codigo(l.Estado), l.TotalItems,
                ConsultaLiquidaciones.Contadores(l),
                l.CreadaEn, ConsultaLiquidaciones.CreadorDe(l), alcance.Referencia(l.TenantId), l.NombreArchivo,
                unico?.Placa, unico?.Id, unico is null ? null : CodigosDeEnum.Codigo(unico.Estado),
                unico is null ? null : ConsultaLiquidaciones.PlacaNoInscrita(unico.Estado, unico.ErrorCodigo, unico.ErrorMensaje));
        }).ToList();
        return Calcular(rango, new AgregadosEstadistica(porEstado, porDia, porDepartamento, porEmpresa), enProceso, resumenes, alcance);
    }

    public static DashboardDto Calcular(
        RangoDias rango, AgregadosEstadistica agregados, int lotesEnProceso, IReadOnlyList<ResumenLiquidacionDto> ultimosLotes, AlcanceLectura alcance)
    {
        ArgumentNullException.ThrowIfNull(agregados);
        ArgumentNullException.ThrowIfNull(ultimosLotes);
        ArgumentNullException.ThrowIfNull(alcance);
        var grupos = agregados.PorEstado;
        var total = grupos.Sum(g => g.Total);
        var individuales = grupos.Where(g => g.Tipo == TipoLiquidacion.Individual).Sum(g => g.Total);
        var exitosas = grupos.Where(g => g.Estado == EstadoItem.Exitoso).ToList();
        var sinMonto = exitosas.Sum(g => g.SinMonto);
        var conMonto = grupos.Sum(g => g.ConMonto);

        var resultados = new ResultadosDashboardDto(
            Exitosas: exitosas.Sum(g => g.Total),
            ConError: Contar(grupos, CategoriaDashboard.Error),
            PendientesWebhook: grupos.Where(PendienteDeWebhook).Sum(g => g.Total),
            PendientesConsulta: grupos.Where(g => EstadosItem.Categoria(g.Estado) == CategoriaDashboard.Pendiente && !PendienteDeWebhook(g)).Sum(g => g.Total),
            DatosIncompletos: Contar(grupos, CategoriaDashboard.DatosIncompletos),
            DepartamentoNoHabilitado: Contar(grupos, CategoriaDashboard.DepartamentoNoHabilitado),
            Inciertas: Contar(grupos, CategoriaDashboard.Incierto),
            SinResultado: Contar(grupos, CategoriaDashboard.SinResultado),
            NoAplica: Contar(grupos, CategoriaDashboard.NoAplica));

        var serie = Enumerable.Range(0, rango.Dias).Select(n =>
        {
            var delDia = agregados.PorDia.Where(d => d.Dia == n).ToList();
            var todas = delDia.Sum(d => d.Total);
            var ind = delDia.Where(d => d.Tipo == TipoLiquidacion.Individual).Sum(d => d.Total);
            return new PuntoSerieDto(rango.Desde.AddDays(n), todas, ind, todas - ind);
        }).ToList();

        var departamentos = agregados.PorDepartamento
            .Select(d => new DepartamentoDashboardDto(d.Codigo, string.IsNullOrWhiteSpace(d.Nombre) ? d.Codigo : d.Nombre, d.Total, d.ConMonto == 0 ? null : d.Monto))
            .OrderByDescending(d => d.Total).ThenBy(d => d.Codigo, StringComparer.Ordinal)
            .ToList();

        IReadOnlyList<RankingDashboardDto>? ranking = alcance.Global
            ? [.. (agregados.PorEmpresa ?? []).Select(g => new RankingDashboardDto(alcance.Referencia(g.Empresa), g.Total))
                .OrderByDescending(r => r.Total).ThenBy(r => r.Empresa.Nombre, StringComparer.Ordinal).ThenBy(r => r.Empresa.Id)]
            : null;

        return new DashboardDto(
            new PeriodoDashboardDto(rango.Desde, rango.Hasta),
            new ContadoresDashboardDto(total, individuales, total - individuales, lotesEnProceso),
            resultados,
            new DeudaDashboardDto(exitosas.Sum(g => g.ConDeuda), exitosas.Sum(g => g.PazYSalvo)),
            conMonto == 0 ? null : grupos.Sum(g => g.Monto),
            sinMonto,
            sinMonto > 0 || exitosas.Any(g => g.Parciales > 0),
            serie,
            departamentos,
            ultimosLotes,
            ranking);
    }

    private static bool PendienteDeWebhook(GrupoEstado g) => g.Estado == EstadoItem.Enviado && g.ConCallback;

    private static int Contar(IReadOnlyList<GrupoEstado> grupos, CategoriaDashboard categoria) =>
        grupos.Where(g => EstadosItem.Categoria(g.Estado) == categoria).Sum(g => g.Total);
}
