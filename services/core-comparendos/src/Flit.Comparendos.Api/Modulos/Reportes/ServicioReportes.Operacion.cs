using System.Globalization;
using Flit.Comparendos.Api.Modulos.Bandeja;
using Flit.Comparendos.Domain.Modelo;
using Microsoft.EntityFrameworkCore;

namespace Flit.Comparendos.Api.Modulos.Reportes;

/// <summary>Cantidad y, solo con el módulo de reportes, el valor (null sin el módulo: la gestora no ve pesos).</summary>
public sealed record Cifra(int Cantidad, decimal? Valor);

public sealed record ResumenOperacion(int Pendientes, decimal? ValorPendiente, int Vehiculos, int Nuevos30, int Resueltos30);

/// <summary>Un paso del embudo de gestión con la clave de <c>estadoProceso</c> de la bandeja a la que lleva.</summary>
public sealed record PasoGestion(string EstadoProceso, int Cantidad);

public sealed record GestionOperacion(IReadOnlyList<PasoGestion> Pasos, decimal? TasaExito);

public sealed record VehiculoRepetido(string Placa, int Cantidad, int Organismos, decimal? Valor);

public sealed record VehiculosOperacion(int ConTresOMas, decimal? ConcentracionDiezPorCiento, IReadOnlyList<VehiculoRepetido> Top);

public sealed record InfraccionFrecuente(string Codigo, string Descripcion, int Cantidad, decimal? Valor);

public sealed record EvitablesOperacion(Cifra Soat, Cifra TecnicoMecanica);

public sealed record MesCalendario(string Periodo, int Caducan, int Prescriben);

public sealed record TramoAntiguedad(string Tramo, int Cantidad, decimal? Valor, DateOnly? Desde, DateOnly? Hasta);

public sealed record DpOrganismo(Guid? OrganismoId, string Nombre, int Enviados, int Favorables, int Respondidos, decimal? DiasPromedio);

public sealed record Operacion(
    DateOnly Hoy,
    bool ConValores,
    ResumenOperacion Resumen,
    GestionOperacion Gestion,
    VehiculosOperacion Vehiculos,
    IReadOnlyList<InfraccionFrecuente> Infracciones,
    EvitablesOperacion Evitables,
    IReadOnlyList<MesCalendario> Calendario,
    IReadOnlyList<TramoAntiguedad> Antiguedad,
    IReadOnlyList<DpOrganismo> DpPorOrganismo);

/// <summary>
/// La lente de operación del Inicio de la gestora (rediseño de Comparendos, R2): vehículos que más repiten, infracciones,
/// evitables, calendario legal, antigüedad, embudo de gestión y DP por organismo. Solo lectura. «Pendiente» = activo y sin
/// pagar (lo que la bandeja muestra con <c>pagado=false</c>). Los pesos solo se devuelven con <paramref name="conValores"/>;
/// sin él van en null (las sumas viajan en las mismas pasadas, no hay consultas extra por ellas).
/// </summary>
public sealed partial class ServicioReportes
{
    /// <summary>SOAT vencido (D02) y revisión técnico-mecánica (C35): lo que se pudo evitar con la flota al día.</summary>
    public const string CodigoSoat = "D02";
    public const string CodigoTecnicoMecanica = "C35";

    /// <summary>
    /// Los 8 organismos con más DP enviados; en empate, por nombre (REX R3: un orden estable que además se entiende,
    /// no el del identificador).
    /// </summary>
    public static List<DpOrganismo> OrdenarDpPorOrganismo(IEnumerable<DpOrganismo> filas) =>
        filas.OrderByDescending(x => x.Enviados).ThenBy(x => x.Nombre, StringComparer.Ordinal).Take(8).ToList();

    public async Task<Operacion> OperacionAsync(Guid tenant, bool conValores, ConsultaBandeja bandeja, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(bandeja);
        var h = await bandeja.HoyAsync(ct).ConfigureAwait(false);
        var hoy = h.Dia;
        decimal? V(decimal v) => conValores ? v : null;
        var pendientes = db.Comparendos.AsNoTracking().Where(c => c.TenantId == tenant && c.Activo && !c.Pagado);

        // 1. Una pasada: totales, antigüedad (5 tramos por fecha de imposición) y evitables.
        var d30 = hoy.AddDays(-30);
        var d6m = hoy.AddMonths(-6);
        var d1a = hoy.AddYears(-1);
        var d3a = hoy.AddYears(-3);
        var t = await pendientes.GroupBy(_ => 1).Select(g => new
        {
            N = g.Count(),
            V = g.Sum(c => c.ValorTotal ?? 0),
            N1 = g.Count(c => c.FechaImposicion >= d30),
            V1 = g.Sum(c => c.FechaImposicion >= d30 ? c.ValorTotal ?? 0 : 0),
            N2 = g.Count(c => c.FechaImposicion >= d6m && c.FechaImposicion < d30),
            V2 = g.Sum(c => c.FechaImposicion >= d6m && c.FechaImposicion < d30 ? c.ValorTotal ?? 0 : 0),
            N3 = g.Count(c => c.FechaImposicion >= d1a && c.FechaImposicion < d6m),
            V3 = g.Sum(c => c.FechaImposicion >= d1a && c.FechaImposicion < d6m ? c.ValorTotal ?? 0 : 0),
            N4 = g.Count(c => c.FechaImposicion >= d3a && c.FechaImposicion < d1a),
            V4 = g.Sum(c => c.FechaImposicion >= d3a && c.FechaImposicion < d1a ? c.ValorTotal ?? 0 : 0),
            N5 = g.Count(c => c.FechaImposicion < d3a),
            V5 = g.Sum(c => c.FechaImposicion < d3a ? c.ValorTotal ?? 0 : 0),
            // Lo que entró por OCR puede no traer fecha de imposición: va aparte para que los tramos sumen lo pendiente.
            N6 = g.Count(c => c.FechaImposicion == null),
            V6 = g.Sum(c => c.FechaImposicion == null ? c.ValorTotal ?? 0 : 0),
            NSoat = g.Count(c => c.CodigoInfraccion == CodigoSoat),
            VSoat = g.Sum(c => c.CodigoInfraccion == CodigoSoat ? c.ValorTotal ?? 0 : 0),
            NRtm = g.Count(c => c.CodigoInfraccion == CodigoTecnicoMecanica),
            VRtm = g.Sum(c => c.CodigoInfraccion == CodigoTecnicoMecanica ? c.ValorTotal ?? 0 : 0),
        }).FirstOrDefaultAsync(ct).ConfigureAwait(false);

        var antiguedad = new List<TramoAntiguedad>
        {
            new("Menos de 30 días", t?.N1 ?? 0, V(t?.V1 ?? 0), d30, null),
            new("De 1 a 6 meses", t?.N2 ?? 0, V(t?.V2 ?? 0), d6m, d30.AddDays(-1)),
            new("De 6 a 12 meses", t?.N3 ?? 0, V(t?.V3 ?? 0), d1a, d6m.AddDays(-1)),
            new("De 1 a 3 años", t?.N4 ?? 0, V(t?.V4 ?? 0), d3a, d1a.AddDays(-1)),
            new("Más de 3 años", t?.N5 ?? 0, V(t?.V5 ?? 0), null, d3a.AddDays(-1)),
            // Sin desde ni hasta: la bandeja no filtra «sin fecha», así que este tramo no lleva enlace.
            new("Sin fecha de imposición", t?.N6 ?? 0, V(t?.V6 ?? 0), null, null),
        };

        // 2. Vehículos: una fila por placa (unos miles a lo sumo); el top y la concentración se arman en memoria.
        var placas = await pendientes.Where(c => c.Placa != null).GroupBy(c => c.Placa!)
            .Select(g => new { Placa = g.Key, N = g.Count(), V = g.Sum(c => c.ValorTotal ?? 0), Organismos = g.Select(c => c.OrganismoTransitoId).Distinct().Count() })
            .ToListAsync(ct).ConfigureAwait(false);
        var ordenadas = placas.OrderByDescending(p => p.N).ThenByDescending(p => p.V).ThenBy(p => p.Placa, StringComparer.Ordinal).ToList();
        var decil = (int)Math.Ceiling(ordenadas.Count * 0.1);
        var conPlaca = ordenadas.Sum(p => p.N);
        decimal? concentracion = conPlaca == 0 ? null : Math.Round((decimal)ordenadas.Take(decil).Sum(p => p.N) / conPlaca, 4);
        var vehiculos = new VehiculosOperacion(ordenadas.Count(p => p.N >= 3), concentracion,
            ordenadas.Take(10).Select(p => new VehiculoRepetido(p.Placa, p.N, p.Organismos, V(p.V))).ToList());

        // 3. Infracciones más frecuentes (con su descripción del catálogo).
        var codigos = await pendientes.Where(c => c.CodigoInfraccion != null).GroupBy(c => c.CodigoInfraccion!)
            .Select(g => new { Codigo = g.Key, N = g.Count(), V = g.Sum(c => c.ValorTotal ?? 0), Descripcion = g.Max(c => c.DescripcionInfraccion) })
            .OrderByDescending(x => x.N).ThenBy(x => x.Codigo).Take(5).ToListAsync(ct).ConfigureAwait(false);
        var claves = codigos.Select(c => c.Codigo).ToList();
        var catalogo = await db.InfraccionesCatalogo.AsNoTracking().Where(i => claves.Contains(i.Codigo))
            .ToDictionaryAsync(i => i.Codigo, i => i.Descripcion, ct).ConfigureAwait(false);
        var infracciones = codigos.Select(c => new InfraccionFrecuente(c.Codigo, catalogo.GetValueOrDefault(c.Codigo) ?? c.Descripcion ?? c.Codigo, c.N, V(c.V))).ToList();

        // 4. Calendario legal: lo que caduca o prescribe desde hoy hasta el fin del sexto mes.
        var primerMes = new DateOnly(hoy.Year, hoy.Month, 1);
        var finVentana = primerMes.AddMonths(6);
        var caducan = await pendientes.Where(c => c.FechaCaducidad >= hoy && c.FechaCaducidad < finVentana)
            .GroupBy(c => new { c.FechaCaducidad!.Value.Year, c.FechaCaducidad!.Value.Month }).Select(g => new { g.Key.Year, g.Key.Month, N = g.Count() })
            .ToListAsync(ct).ConfigureAwait(false);
        var prescriben = await pendientes.Where(c => c.FechaPrescripcion >= hoy && c.FechaPrescripcion < finVentana)
            .GroupBy(c => new { c.FechaPrescripcion!.Value.Year, c.FechaPrescripcion!.Value.Month }).Select(g => new { g.Key.Year, g.Key.Month, N = g.Count() })
            .ToListAsync(ct).ConfigureAwait(false);
        var calendario = Enumerable.Range(0, 6).Select(i => primerMes.AddMonths(i)).Select(m => new MesCalendario(
            m.ToString("yyyy-MM", CultureInfo.InvariantCulture),
            caducan.Where(x => x.Year == m.Year && x.Month == m.Month).Sum(x => x.N),
            prescriben.Where(x => x.Year == m.Year && x.Month == m.Month).Sum(x => x.N))).ToList();

        // 5. Últimos 30 días: lo que entró y lo que dejó de estar pendiente (pagado o inactivado).
        var hace30 = Alcance.Utc(d30);
        var nuevos = await db.Comparendos.AsNoTracking().CountAsync(c => c.TenantId == tenant && c.CreadoEn >= hace30, ct).ConfigureAwait(false);
        var pagados = await db.Pagos.AsNoTracking().Where(p => p.TenantId == tenant && p.FechaPago >= d30).Select(p => p.ComparendoId)
            .Union(db.Comparendos.AsNoTracking().Where(c => c.TenantId == tenant && !c.Activo && c.InactivadoEn >= hace30).Select(c => c.Id))
            .CountAsync(ct).ConfigureAwait(false);

        // 6. Embudo de gestión con el filtro de la bandeja: cada paso es la cifra que se ve al hacer clic.
        var pasos = new List<PasoGestion>();
        foreach (var estado in new[] { "SinProcesar", "DerechoPeticion", "RespuestaDesfavorable", "RespuestaFavorable" })
            pasos.Add(new PasoGestion(estado, await bandeja.Filtrar(tenant, new FiltrosBandeja { EstadoProceso = [estado] }, h).CountAsync(ct).ConfigureAwait(false)));
        var fav = pasos[3].Cantidad;
        var desf = pasos[2].Cantidad;

        // 7. DP por organismo: enviados, favorables y días promedio hasta la respuesta (pocos miles de filas a lo sumo).
        var dps = await (from d in db.DerechosPeticion.AsNoTracking()
                         join c in db.Comparendos.AsNoTracking() on d.ComparendoId equals c.Id
                         where d.TenantId == tenant && (d.Estado == EstadoDp.Enviado || d.Estado == EstadoDp.Respondido)
                         select new { c.OrganismoTransitoId, d.Estado, d.RespuestaSentido, d.FechaRadicacion, d.RespuestaFecha }).ToListAsync(ct).ConfigureAwait(false);
        var porOrganismo = dps.GroupBy(d => d.OrganismoTransitoId).Select(g =>
        {
            var respondidos = g.Where(d => d.Estado == EstadoDp.Respondido && d.FechaRadicacion is not null && d.RespuestaFecha is not null).ToList();
            return new
            {
                Organismo = g.Key,
                Enviados = g.Count(),
                Favorables = g.Count(d => d.Estado == EstadoDp.Respondido && d.RespuestaSentido == SentidoRespuestaOt.Favorable),
                Respondidos = respondidos.Count,
                Dias = respondidos.Count == 0 ? (decimal?)null
                    : Math.Round((decimal)respondidos.Average(d => d.RespuestaFecha!.Value.DayNumber - d.FechaRadicacion!.Value.DayNumber), 1),
            };
        }).ToList();
        var ids = porOrganismo.Where(o => o.Organismo != null).Select(o => o.Organismo!.Value).ToList();
        var nombres = await db.OrganismosTransito.AsNoTracking().Where(o => ids.Contains(o.Id)).ToDictionaryAsync(o => o.Id, o => o.Nombre, ct).ConfigureAwait(false);
        var dpPorOrganismo = OrdenarDpPorOrganismo(porOrganismo.Select(o => new DpOrganismo(o.Organismo,
            o.Organismo is { } id && nombres.TryGetValue(id, out var n) ? n : "Sin organismo identificado",
            o.Enviados, o.Favorables, o.Respondidos, o.Dias)));

        return new Operacion(
            hoy,
            conValores,
            new ResumenOperacion(t?.N ?? 0, V(t?.V ?? 0), ordenadas.Count, nuevos, pagados),
            new GestionOperacion(pasos, fav + desf == 0 ? null : Math.Round((decimal)fav / (fav + desf), 4)),
            vehiculos,
            infracciones,
            new EvitablesOperacion(new Cifra(t?.NSoat ?? 0, V(t?.VSoat ?? 0)), new Cifra(t?.NRtm ?? 0, V(t?.VRtm ?? 0))),
            calendario,
            antiguedad,
            dpPorOrganismo);
    }
}
