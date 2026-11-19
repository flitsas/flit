using System.Globalization;
using Flit.Comparendos.Api.Modulos.Bandeja;
using Flit.Comparendos.Api.Persistence.Modelo;
using Flit.Comparendos.Domain.Modelo;
using Flit.Comparendos.Domain.Tiempo;
using Microsoft.EntityFrameworkCore;

namespace Flit.Comparendos.Api.Modulos.Reportes;

public sealed record Monto(int Cantidad, decimal Valor);

public sealed record MontoConAhorro(int Cantidad, decimal Valor, decimal AhorroEstimado);

public sealed record DineroEstado(string Estado, int Cantidad, decimal Valor);

public sealed record DineroOrganismo(Guid? OrganismoId, string Nombre, int Cantidad, decimal Valor, decimal Exigible);

public sealed record DineroPagado(int Cantidad, decimal Total, decimal Cc, decimal Pc, decimal Bc);

public sealed record DineroDescuento(MontoConAhorro D50, MontoConAhorro D25);

public sealed record DineroFrentes(Monto ProximosAVencer, Monto SinContraventor, Monto RechazadosOt, Monto SinGestion, Monto Vencidos, Monto CaducidadInmediata);

public sealed record DineroMes(string Periodo, decimal ValorIngresado, decimal Pagado);

/// <summary>Solo el reparto de la deuda por estado legal (<c>?partes=estados</c>): lo que necesita el indicador de Reportes.</summary>
public sealed record DineroEstados(DateOnly Hoy, IReadOnlyList<DineroEstado> Estados, Monto Exigible, Monto NoExigible);

public sealed record Dinero(
    DateOnly Hoy,
    IReadOnlyList<DineroEstado> Estados,
    Monto Exigible,
    Monto NoExigible,
    Monto CobroCoactivo,
    DineroDescuento Descuento,
    IReadOnlyList<DineroOrganismo> Organismos,
    DineroPagado Pagado,
    DineroFrentes Frentes,
    decimal AhorroLogrado,
    IReadOnlyList<DineroMes> Mensual);

/// <summary>
/// La lente de dinero del Inicio y de Reportes (rediseño de Comparendos, S2). Solo lectura. La deuda es la misma que la del
/// indicador del dashboard (activo, sin pagar y sin DP favorable); el estado legal sale de las mismas fechas que la bandeja,
/// y cada frente cuenta con el filtro de la bandeja para que la cifra sea la que se ve al hacer clic.
/// </summary>
public sealed partial class ServicioReportes
{
    private static readonly string[] EstadosLegales = ["SinNotificar", "EnTiempo", "Vencido", "Caducado", "Prescrito"];

    /// <summary>Una fila por organismo con la deuda (activo, sin pagar y sin DP favorable) repartida por estado y descuento.</summary>
    private sealed record GrupoDeuda(Guid? Organismo, int N, decimal V, int NSin, decimal VSin, int NTiempo, decimal VTiempo, int NVencido, decimal VVencido,
        int NCaducado, decimal VCaducado, int NPrescrito, decimal VPrescrito, int N50, decimal V50, int N25, decimal V25);

    private async Task<List<GrupoDeuda>> GruposDeudaAsync(Alcance a, DateOnly hoy, CancellationToken ct) =>
        // Una sola pasada agrupada por organismo: los totales por estado salen de sumar los grupos.
        // La misma deuda que KpisAsync (sin DP favorable), escrita como NOT EXISTS: anti join por el índice único de
        // derecho_peticion.comparendo_id en vez de un NOT IN que vuelve a recorrer los comparendos de la empresa (DRE:
        // 11 ms contra 21 ms con 61.620 comparendos).
        await Comparendos(a)
            .Where(c => c.Activo && !c.Pagado
                && !db.DerechosPeticion.Any(d => d.ComparendoId == c.Id && d.Estado == EstadoDp.Respondido && d.RespuestaSentido == SentidoRespuestaOt.Favorable))
            .GroupBy(c => c.OrganismoTransitoId)
            .Select(g => new GrupoDeuda(
                g.Key,
                g.Count(),
                g.Sum(c => c.ValorTotal ?? 0),
                g.Count(c => c.FechaLimiteGestion == null),
                g.Sum(c => c.FechaLimiteGestion == null ? c.ValorTotal ?? 0 : 0),
                g.Count(c => c.FechaLimiteGestion != null && hoy <= c.FechaLimiteGestion),
                g.Sum(c => c.FechaLimiteGestion != null && hoy <= c.FechaLimiteGestion ? c.ValorTotal ?? 0 : 0),
                g.Count(c => c.FechaLimiteGestion != null && hoy > c.FechaLimiteGestion && hoy <= c.FechaCaducidad),
                g.Sum(c => c.FechaLimiteGestion != null && hoy > c.FechaLimiteGestion && hoy <= c.FechaCaducidad ? c.ValorTotal ?? 0 : 0),
                g.Count(c => hoy > c.FechaCaducidad && hoy <= c.FechaPrescripcion),
                g.Sum(c => hoy > c.FechaCaducidad && hoy <= c.FechaPrescripcion ? c.ValorTotal ?? 0 : 0),
                g.Count(c => hoy > c.FechaPrescripcion),
                g.Sum(c => hoy > c.FechaPrescripcion ? c.ValorTotal ?? 0 : 0),
                g.Count(c => c.FechaFinDescuento50 != null && hoy <= c.FechaFinDescuento50),
                g.Sum(c => c.FechaFinDescuento50 != null && hoy <= c.FechaFinDescuento50 ? c.ValorTotal ?? 0 : 0),
                g.Count(c => (c.FechaFinDescuento50 == null || hoy > c.FechaFinDescuento50) && c.FechaFinDescuento25 != null && hoy <= c.FechaFinDescuento25),
                g.Sum(c => (c.FechaFinDescuento50 == null || hoy > c.FechaFinDescuento50) && c.FechaFinDescuento25 != null && hoy <= c.FechaFinDescuento25 ? c.ValorTotal ?? 0 : 0)))
            .ToListAsync(ct).ConfigureAwait(false);

    private static (DineroEstado[] Estados, Monto Exigible, Monto NoExigible) Repartir(List<GrupoDeuda> grupos)
    {
        var estados = new[]
        {
            new DineroEstado(EstadosLegales[0], grupos.Sum(g => g.NSin), grupos.Sum(g => g.VSin)),
            new DineroEstado(EstadosLegales[1], grupos.Sum(g => g.NTiempo), grupos.Sum(g => g.VTiempo)),
            new DineroEstado(EstadosLegales[2], grupos.Sum(g => g.NVencido), grupos.Sum(g => g.VVencido)),
            new DineroEstado(EstadosLegales[3], grupos.Sum(g => g.NCaducado), grupos.Sum(g => g.VCaducado)),
            new DineroEstado(EstadosLegales[4], grupos.Sum(g => g.NPrescrito), grupos.Sum(g => g.VPrescrito)),
        };
        return (estados, new Monto(estados.Take(3).Sum(e => e.Cantidad), estados.Take(3).Sum(e => e.Valor)),
            new Monto(estados.Skip(3).Sum(e => e.Cantidad), estados.Skip(3).Sum(e => e.Valor)));
    }

    /// <summary>Solo el reparto por estado: una consulta, sin frentes, pagos, organismos ni serie (indicador de Reportes).</summary>
    public async Task<DineroEstados> EstadosAsync(Alcance a, ConsultaBandeja bandeja, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(bandeja);
        var hoy = (await bandeja.HoyAsync(ct).ConfigureAwait(false)).Dia;
        var (estados, exigible, noExigible) = Repartir(await GruposDeudaAsync(a, hoy, ct).ConfigureAwait(false));
        return new DineroEstados(hoy, estados, exigible, noExigible);
    }

    public async Task<Dinero> DineroAsync(Alcance a, ConsultaBandeja bandeja, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(bandeja);
        var h = await bandeja.HoyAsync(ct).ConfigureAwait(false);
        var hoy = h.Dia;
        var grupos = await GruposDeudaAsync(a, hoy, ct).ConfigureAwait(false);

        var (estados, exigible, noExigible) = Repartir(grupos);
        var v50 = grupos.Sum(g => g.V50);
        var v25 = grupos.Sum(g => g.V25);

        var top = grupos.OrderByDescending(g => g.V).ThenByDescending(g => g.N).Take(10).ToList();
        var idsTop = top.Where(g => g.Organismo != null).Select(g => g.Organismo!.Value).ToList();
        var nombres = await db.OrganismosTransito.AsNoTracking().Where(o => idsTop.Contains(o.Id)).ToDictionaryAsync(o => o.Id, o => o.Nombre, ct).ConfigureAwait(false);
        var organismos = top.Select(g => new DineroOrganismo(g.Organismo,
            g.Organismo is { } id && nombres.TryGetValue(id, out var nombre) ? nombre : "Sin organismo identificado",
            g.N, g.V, g.VSin + g.VTiempo + g.VVencido)).ToList();

        var pagosPeriodo = await (from p in Pagos(a)
                                  join c in Comparendos(a) on p.ComparendoId equals c.Id
                                  where p.FechaPago >= a.Desde && p.FechaPago <= a.Hasta
                                  group p by p.TipoPago into g
                                  select new { Tipo = g.Key, N = g.Count(), V = g.Sum(p => p.ValorPagado) }).ToListAsync(ct).ConfigureAwait(false);
        decimal Pagado(TipoPago t) => pagosPeriodo.Where(p => p.Tipo == t).Sum(p => p.V);

        var frentes = new DineroFrentes(
            await FrenteAsync(bandeja, a, new FiltrosBandeja { Rapido = "ProximosAVencer" }, h, ct).ConfigureAwait(false),
            await FrenteAsync(bandeja, a, new FiltrosBandeja { Rapido = "SinContraventor" }, h, ct).ConfigureAwait(false),
            await FrenteAsync(bandeja, a, new FiltrosBandeja { Rapido = "RechazadosOt" }, h, ct).ConfigureAwait(false),
            await FrenteAsync(bandeja, a, new FiltrosBandeja { SinGestion = true }, h, ct).ConfigureAwait(false),
            await FrenteAsync(bandeja, a, new FiltrosBandeja { EstadoLegal = ["Vencido"] }, h, ct).ConfigureAwait(false),
            await FrenteAsync(bandeja, a, new FiltrosBandeja { Juridico = "CaducidadInmediata" }, h, ct).ConfigureAwait(false));

        // Como los frentes: la cifra es la de /bandeja?cobroCoactivo=true y el monto es el valor de esos comparendos.
        var coactivo = await FrenteAsync(bandeja, a, new FiltrosBandeja { CobroCoactivo = true }, h, ct).ConfigureAwait(false);

        return new Dinero(hoy, estados, exigible, noExigible, coactivo,
            new DineroDescuento(new MontoConAhorro(grupos.Sum(g => g.N50), v50, v50 * 0.5m), new MontoConAhorro(grupos.Sum(g => g.N25), v25, v25 * 0.25m)),
            organismos,
            new DineroPagado(pagosPeriodo.Sum(p => p.N), pagosPeriodo.Sum(p => p.V), Pagado(TipoPago.CC), Pagado(TipoPago.PC), Pagado(TipoPago.BC)),
            frentes,
            await AhorroAsync(a, ct).ConfigureAwait(false),
            await MensualDineroAsync(a, hoy, ct).ConfigureAwait(false));
    }

    /// <summary>
    /// Pagos de la empresa del alcance. El filtro por tenant repite el del join a propósito: deja usar el índice
    /// (tenant_id, fecha_pago) en vez de recorrer los pagos de todas las empresas (DRE: 0,6 ms contra 6 ms).
    /// </summary>
    private IQueryable<Pago> Pagos(Alcance a) =>
        db.Pagos.AsNoTracking().Where(p => a.Tenant == null || p.TenantId == a.Tenant);

    /// <summary>Cantidad y valor de un frente con el mismo filtro de la bandeja (y el NIT del reporte, si lo hay).</summary>
    private static async Task<Monto> FrenteAsync(ConsultaBandeja bandeja, Alcance a, FiltrosBandeja f, ConsultaBandeja.Hoy h, CancellationToken ct)
    {
        var filtros = a.Nit is { } nit ? f with { Nits = [nit] } : f;
        var r = await bandeja.Filtrar(a.Tenant, filtros, h).GroupBy(_ => 1).Select(g => new { N = g.Count(), V = g.Sum(c => c.ValorTotal ?? 0) })
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);
        return r is null ? new Monto(0, 0) : new Monto(r.N, r.V);
    }

    /// <summary>Últimos 12 meses: valor ingresado (por fecha de registro, en hora de Colombia) y pagado (por fecha de pago).</summary>
    private async Task<List<DineroMes>> MensualDineroAsync(Alcance a, DateOnly hoy, CancellationToken ct)
    {
        var primerMes = new DateOnly(hoy.Year, hoy.Month, 1).AddMonths(-11);
        var inicio = Alcance.Utc(primerMes);
        // Colombia no tiene horario de verano: correr −5 h deja el mes local.
        var desfase = ColombiaTime.Offset;
        var ingresos = await Comparendos(a).Where(c => c.CreadoEn >= inicio)
            .Select(c => new { Local = c.CreadoEn + desfase, c.ValorTotal })
            .GroupBy(x => new { x.Local.Year, x.Local.Month })
            .Select(g => new { g.Key.Year, g.Key.Month, V = g.Sum(x => x.ValorTotal ?? 0) }).ToListAsync(ct).ConfigureAwait(false);
        var pagos = await (from p in Pagos(a)
                           join c in Comparendos(a) on p.ComparendoId equals c.Id
                           where p.FechaPago >= primerMes
                           group p by new { p.FechaPago.Year, p.FechaPago.Month } into g
                           select new { g.Key.Year, g.Key.Month, V = g.Sum(p => p.ValorPagado) }).ToListAsync(ct).ConfigureAwait(false);
        return Enumerable.Range(0, 12).Select(i => primerMes.AddMonths(i)).Select(m => new DineroMes(
            m.ToString("yyyy-MM", CultureInfo.InvariantCulture),
            ingresos.Where(x => x.Year == m.Year && x.Month == m.Month).Sum(x => x.V),
            pagos.Where(x => x.Year == m.Year && x.Month == m.Month).Sum(x => x.V))).ToList();
    }
}
