using System.Text.Json.Serialization;
using Flit.Comparendos.Api.Persistence.Modelo;
using Flit.Comparendos.Domain.Modelo;
using Microsoft.EntityFrameworkCore;

namespace Flit.Comparendos.Api.Modulos.Reportes;

/// <summary>
/// Una fila de DP agrupada por el organismo AL QUE SE RADICÓ el DP. Enviados y sin respuesta: radicados en el periodo.
/// Favorables y desfavorables: respuestas recibidas en el periodo, el mismo corte de la tasa canónica
/// (<see cref="ServicioReportes.KpisAsync"/>), así que esas dos columnas suman la cifra del Resumen. «Sin respuesta» de la
/// fila NO es la del Resumen: esa es a hoy, sin periodo.
/// </summary>
public sealed record FilaDp(string Clave, string Nombre, int Enviados, int Favorables, int Desfavorables, int SinRespuesta, decimal? DiasPromedio, decimal? TasaExito);

public sealed record CeldaDp(Guid OrganismoId, string Organismo, string Infraccion, int Enviados, int Favorables, int Desfavorables, decimal? TasaExito);

public sealed record ConsolidadoDp(Guid EmpresaId, string Empresa, Guid OrganismoId, string Organismo, int Enviados, int Favorables, int Desfavorables, int SinRespuesta, decimal? TasaExito);

public sealed record DpPositivo(Guid ComparendoId, string Numero, string? Placa, string Radicado, string Organismo, DateOnly? FechaRadicacion, DateOnly FechaRespuesta, decimal? ValorAlRadicar);

public sealed record DpVencido(Guid ComparendoId, string Numero, string? Placa, string Radicado, string Organismo, DateOnly? FechaRadicacion, DateOnly FechaLimite, int DiasVencido);

public sealed record PuntoDpDiario(DateOnly Dia, int Enviados, int Respondidos);

public sealed record ReporteDp(
    decimal? TasaExito,
    decimal Ahorro,
    int Favorables,
    int Desfavorables,
    int SinRespuesta,
    int Enviados,
    IReadOnlyList<FilaDp> PorOrganismo,
    IReadOnlyList<FilaDp> PorInfraccion,
    IReadOnlyList<CeldaDp> Cruce,
    IReadOnlyList<DpPositivo> Positivos,
    int TotalPositivos,
    IReadOnlyList<PuntoDpDiario> Acumulado,
    IReadOnlyList<DpVencido> Vencidos,
    int TotalVencidos,
    IReadOnlyList<ConsolidadoDp>? Consolidado)
{
    /// <summary>
    /// Con «todas las empresas» (SuperAdmin), las empresas cuyos datos van en la respuesta: las del consolidado y las de los
    /// vencidos listados (que pueden haberse radicado antes del periodo). El endpoint las anota con <c>AnotarLectura</c> (§2.4).
    /// No viaja en el JSON.
    /// </summary>
    [JsonIgnore]
    public IReadOnlyCollection<Guid> EmpresasLeidas { get; init; } = [];
}

/// <summary>
/// Pestaña «Derechos de petición» (V1: «Reporte DP Detallado» y el consolidado de «Resumen de reportes»). La tasa y el
/// ahorro NO se recalculan: salen de <see cref="KpisAsync"/> (R1). Las listas llevan tope; su total va aparte.
/// </summary>
public sealed partial class ServicioReportes
{
    public const int TopeListaDp = 200;
    public const int TopeCruceDp = 60;

    /// <summary>
    /// Tasa de éxito de una fila: la MISMA fórmula y el mismo corte que la tasa canónica del Resumen
    /// (<c>ServicioReportes.cs:85-92</c>, <see cref="KpisAsync"/>): favorables / (favorables + desfavorables) sobre las
    /// respuestas recibidas en el periodo. Sin respuestas, null («—»), no 0 %. Si cambia allá, cambia aquí.
    /// </summary>
    public static decimal? TasaFila(int favorables, int desfavorables) =>
        favorables + desfavorables == 0 ? null : Math.Round((decimal)favorables / (favorables + desfavorables), 4);

    public async Task<ReporteDp> DpAsync(Alcance a, bool consolidado, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(a);
        var kpis = await KpisAsync(a, ct).ConfigureAwait(false);
        var hoy = reloj.Hoy;

        // Una pasada por los DP enviados del alcance que se radicaron o se respondieron en el periodo (miles a lo sumo).
        var filas = await (from d in Dps(a)
                           join c in db.Comparendos.AsNoTracking() on d.ComparendoId equals c.Id
                           where (d.Estado == EstadoDp.Enviado || d.Estado == EstadoDp.Respondido)
                                 && ((d.FechaRadicacion >= a.Desde && d.FechaRadicacion <= a.Hasta)
                                     || (d.Estado == EstadoDp.Respondido && d.RespuestaFecha >= a.Desde && d.RespuestaFecha <= a.Hasta))
                           select new
                           {
                               d.TenantId, d.OrganismoTransitoId, c.CodigoInfraccion, d.Estado, d.RespuestaSentido, d.FechaRadicacion, d.RespuestaFecha,
                           }).ToListAsync(ct).ConfigureAwait(false);

        bool Radicado(DateOnly? f) => f >= a.Desde && f <= a.Hasta;
        bool Respondido(EstadoDp e, DateOnly? f) => e == EstadoDp.Respondido && f >= a.Desde && f <= a.Hasta;

        var organismoIds = filas.Select(f => f.OrganismoTransitoId).Distinct().ToList();
        var organismos = await db.OrganismosTransito.AsNoTracking().Where(o => organismoIds.Contains(o.Id))
            .ToDictionaryAsync(o => o.Id, o => o.Nombre, ct).ConfigureAwait(false);
        string Organismo(Guid id) => organismos.GetValueOrDefault(id) ?? "Organismo sin nombre";

        FilaDp Fila(string clave, string nombre, IEnumerable<(EstadoDp Estado, SentidoRespuestaOt? Sentido, DateOnly? Radicacion, DateOnly? Respuesta)> g)
        {
            var l = g.ToList();
            var resp = l.Where(x => Respondido(x.Estado, x.Respuesta)).ToList();
            var conDias = resp.Where(x => x.Radicacion is not null).ToList();
            var fav = resp.Count(x => x.Sentido == SentidoRespuestaOt.Favorable);
            var desf = resp.Count(x => x.Sentido == SentidoRespuestaOt.Desfavorable);
            return new FilaDp(clave, nombre,
                l.Count(x => Radicado(x.Radicacion)),
                fav,
                desf,
                l.Count(x => Radicado(x.Radicacion) && x.Estado == EstadoDp.Enviado),
                conDias.Count == 0 ? null : Math.Round((decimal)conDias.Average(x => x.Respuesta!.Value.DayNumber - x.Radicacion!.Value.DayNumber), 1),
                TasaFila(fav, desf));
        }

        var porOrganismo = filas.GroupBy(f => f.OrganismoTransitoId)
            .Select(g => Fila(g.Key.ToString(), Organismo(g.Key), g.Select(x => (x.Estado, x.RespuestaSentido, x.FechaRadicacion, x.RespuestaFecha))))
            .OrderByDescending(f => f.Enviados).ThenByDescending(f => f.Favorables + f.Desfavorables).ThenBy(f => f.Nombre, StringComparer.Ordinal).ToList();

        var codigos = filas.Where(f => f.CodigoInfraccion != null).Select(f => f.CodigoInfraccion!).Distinct().ToList();
        var catalogo = await db.InfraccionesCatalogo.AsNoTracking().Where(i => codigos.Contains(i.Codigo))
            .ToDictionaryAsync(i => i.Codigo, i => i.Descripcion, ct).ConfigureAwait(false);
        var porInfraccion = filas.GroupBy(f => f.CodigoInfraccion ?? "")
            .Select(g => Fila(g.Key, g.Key.Length == 0 ? "Sin código de infracción" : catalogo.GetValueOrDefault(g.Key) ?? g.Key,
                g.Select(x => (x.Estado, x.RespuestaSentido, x.FechaRadicacion, x.RespuestaFecha))))
            .OrderByDescending(f => f.Enviados).ThenByDescending(f => f.Favorables + f.Desfavorables).ThenBy(f => f.Clave, StringComparer.Ordinal).ToList();

        var cruce = filas.GroupBy(f => (f.OrganismoTransitoId, Codigo: f.CodigoInfraccion ?? "Sin código"))
            .Select(g =>
            {
                var fila = Fila("", "", g.Select(x => (x.Estado, x.RespuestaSentido, x.FechaRadicacion, x.RespuestaFecha)));
                return new CeldaDp(g.Key.OrganismoTransitoId, Organismo(g.Key.OrganismoTransitoId), g.Key.Codigo, fila.Enviados, fila.Favorables, fila.Desfavorables, fila.TasaExito);
            })
            .OrderByDescending(c => c.Enviados).ThenByDescending(c => c.Favorables).ThenBy(c => c.Organismo, StringComparer.Ordinal).ThenBy(c => c.Infraccion, StringComparer.Ordinal)
            .Take(TopeCruceDp).ToList();

        // Acumulado diario del periodo (a lo sumo tres años: ~1.100 puntos).
        var enviadosPorDia = filas.Where(f => Radicado(f.FechaRadicacion)).GroupBy(f => f.FechaRadicacion!.Value).ToDictionary(g => g.Key, g => g.Count());
        var respondidosPorDia = filas.Where(f => Respondido(f.Estado, f.RespuestaFecha)).GroupBy(f => f.RespuestaFecha!.Value).ToDictionary(g => g.Key, g => g.Count());
        var acumulado = new List<PuntoDpDiario>();
        var (env, res) = (0, 0);
        for (var dia = a.Desde; dia <= a.Hasta && dia <= hoy; dia = dia.AddDays(1))
        {
            env += enviadosPorDia.GetValueOrDefault(dia);
            res += respondidosPorDia.GetValueOrDefault(dia);
            acumulado.Add(new PuntoDpDiario(dia, env, res));
        }

        // Positivos: los favorables respondidos en el periodo (la lista detrás del ahorro).
        var positivosQ = from d in Dps(a)
                         join c in db.Comparendos.AsNoTracking() on d.ComparendoId equals c.Id
                         join o in db.OrganismosTransito.AsNoTracking() on d.OrganismoTransitoId equals o.Id into og
                         from o in og.DefaultIfEmpty()
                         where d.Estado == EstadoDp.Respondido && d.RespuestaSentido == SentidoRespuestaOt.Favorable && d.RespuestaFecha >= a.Desde && d.RespuestaFecha <= a.Hasta
                         select new { d, c.Numero, c.Placa, Organismo = o == null ? null : o.Nombre };
        var totalPositivos = await positivosQ.CountAsync(ct).ConfigureAwait(false);
        var positivos = (await positivosQ.OrderByDescending(x => x.d.RespuestaFecha).ThenBy(x => x.Numero).Take(TopeListaDp).ToListAsync(ct).ConfigureAwait(false))
            .Select(x => new DpPositivo(x.d.ComparendoId, x.Numero, x.Placa, x.d.Radicado, x.Organismo ?? "Organismo sin nombre", x.d.FechaRadicacion, x.d.RespuestaFecha!.Value, x.d.ValorAlRadicar))
            .ToList();

        // Vencidos sin respuesta: a hoy, sin importar el periodo (como «sin respuesta» del Resumen).
        var vencidosQ = from d in Dps(a)
                        join c in db.Comparendos.AsNoTracking() on d.ComparendoId equals c.Id
                        join o in db.OrganismosTransito.AsNoTracking() on d.OrganismoTransitoId equals o.Id into og
                        from o in og.DefaultIfEmpty()
                        where d.Estado == EstadoDp.Enviado && d.FechaLimiteRespuestaOt != null && d.FechaLimiteRespuestaOt < hoy
                        select new { d, c.Numero, c.Placa, Organismo = o == null ? null : o.Nombre };
        var totalVencidos = await vencidosQ.CountAsync(ct).ConfigureAwait(false);
        var vencidosFilas = await vencidosQ.OrderBy(x => x.d.FechaLimiteRespuestaOt).ThenBy(x => x.Numero).Take(TopeListaDp).ToListAsync(ct).ConfigureAwait(false);
        var vencidos = vencidosFilas
            .Select(x => new DpVencido(x.d.ComparendoId, x.Numero, x.Placa, x.d.Radicado, x.Organismo ?? "Organismo sin nombre", x.d.FechaRadicacion,
                x.d.FechaLimiteRespuestaOt!.Value, hoy.DayNumber - x.d.FechaLimiteRespuestaOt!.Value.DayNumber))
            .ToList();

        List<ConsolidadoDp>? porEmpresa = null;
        if (consolidado)
        {
            var tenants = filas.Select(f => f.TenantId).Distinct().ToList();
            var empresas = await db.ConfiguracionesEmpresa.AsNoTracking().Where(e => tenants.Contains(e.TenantId))
                .ToDictionaryAsync(e => e.TenantId, e => e.EmpresaNombre, ct).ConfigureAwait(false);
            porEmpresa = filas.GroupBy(f => (f.TenantId, f.OrganismoTransitoId))
                .Select(g =>
                {
                    var fila = Fila("", "", g.Select(x => (x.Estado, x.RespuestaSentido, x.FechaRadicacion, x.RespuestaFecha)));
                    return new ConsolidadoDp(g.Key.TenantId, empresas.GetValueOrDefault(g.Key.TenantId) ?? "Empresa sin nombre", g.Key.OrganismoTransitoId,
                        Organismo(g.Key.OrganismoTransitoId), fila.Enviados, fila.Favorables, fila.Desfavorables, fila.SinRespuesta, fila.TasaExito);
                })
                .OrderBy(c => c.Empresa, StringComparer.Ordinal).ThenByDescending(c => c.Enviados).ThenBy(c => c.Organismo, StringComparer.Ordinal).ToList();
        }

        var leidas = consolidado ? filas.Select(f => f.TenantId).Concat(vencidosFilas.Select(v => v.d.TenantId)).ToHashSet() : [];
        return new ReporteDp(kpis.TasaExitoDp, kpis.AhorroAcumulado, kpis.DpFavorables, kpis.DpDesfavorables, kpis.DpSinRespuesta,
            porOrganismo.Sum(f => f.Enviados), porOrganismo, porInfraccion, cruce, positivos, totalPositivos, acumulado, vencidos, totalVencidos, porEmpresa)
        {
            EmpresasLeidas = leidas,
        };
    }
}
