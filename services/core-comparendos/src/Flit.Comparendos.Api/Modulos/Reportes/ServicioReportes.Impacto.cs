using Flit.Comparendos.Domain.Modelo;
using Microsoft.EntityFrameworkCore;

namespace Flit.Comparendos.Api.Modulos.Reportes;

/// <summary>Tasa: favorables sobre lo respondido en el año (la misma «tasa de éxito» del dashboard).</summary>
public sealed record ImpactoDp(int Enviados, int Favorables, decimal? Tasa);

public sealed record ImpactoCambios(int Solicitados, int Aceptados);

public sealed record EmpresaConImpacto(Guid EmpresaId, string Nombre, decimal Ahorro, int DpFavorables);

public sealed record ImpactoPlataforma(
    int Anio,
    DateOnly Desde,
    DateOnly Hasta,
    decimal AhorroLogrado,
    ImpactoDp Dp,
    int ComparendosGestionados,
    int AvisosEnviados,
    ImpactoCambios CambiosContraventor,
    IReadOnlyList<EmpresaConImpacto> TopEmpresas);

/// <summary>
/// «Impacto de FLIT» en el Inicio del SuperAdmin (rediseño R4, <c>/admin/resumen?partes=impacto</c>): lo que la plataforma
/// ayudó a hacer a todas las empresas en el año en curso. Solo lectura y sin N+1: una consulta por tabla, agrupada por
/// empresa donde hace falta (el top 5 sale de la misma pasada de los DP).
/// </summary>
public sealed partial class ServicioReportes
{
    public async Task<ImpactoPlataforma> ImpactoPlataformaAsync(CancellationToken ct)
    {
        var hoy = reloj.Hoy;
        var desde = new DateOnly(hoy.Year, 1, 1);
        var a = new Alcance(null, null, desde, hoy);
        var (i, f) = (a.InicioUtc, a.FinUtc);

        // DP del año por empresa. Enviados: radicados en el año (como productividad, sin los automáticos fallidos).
        var enviados = await db.DerechosPeticion.AsNoTracking()
            .Where(d => (d.Estado == EstadoDp.Enviado || d.Estado == EstadoDp.Respondido) && d.FechaRadicacion >= desde && d.FechaRadicacion <= hoy)
            .GroupBy(d => d.TenantId).Select(g => new { Tenant = g.Key, N = g.Count() }).ToListAsync(ct).ConfigureAwait(false);
        // Favorables del año: el ahorro cuenta cada comparendo una vez (su último DP favorable), igual que el dashboard.
        var favorables = await db.DerechosPeticion.AsNoTracking()
            .Where(d => d.Estado == EstadoDp.Respondido && d.RespuestaSentido == SentidoRespuestaOt.Favorable && d.RespuestaFecha >= desde && d.RespuestaFecha <= hoy)
            .GroupBy(d => new { d.TenantId, d.ComparendoId })
            .Select(g => new { g.Key.TenantId, Valor = g.OrderByDescending(d => d.RespuestaFecha).Select(d => d.ValorAlRadicar).First() })
            .ToListAsync(ct).ConfigureAwait(false);
        // Tasa como la del dashboard: favorables sobre lo que tuvo respuesta en el año (no sobre lo enviado, que puede
        // haberse respondido el año siguiente y daría más de 100 %).
        var desfavorables = await db.DerechosPeticion.AsNoTracking()
            .CountAsync(d => d.Estado == EstadoDp.Respondido && d.RespuestaSentido == SentidoRespuestaOt.Desfavorable && d.RespuestaFecha >= desde && d.RespuestaFecha <= hoy, ct)
            .ConfigureAwait(false);
        var porEmpresa = favorables.GroupBy(x => x.TenantId).Select(g => new { Tenant = g.Key, Ahorro = g.Sum(x => x.Valor ?? 0), Favorables = g.Count() }).ToList();

        // Comparendos gestionados: la regla de productividad (acciones de usuarios: pago, contraventor, aviso y DP enviado),
        // contando cada comparendo una vez. UNION en SQL, sin traer los pares a memoria.
        var porEventos = db.Timeline.AsNoTracking()
            .Where(e => e.ActorTipo == ActorTipo.Usuario && e.ActorUsuarioId != null && EventosDeGestion.Contains(e.Tipo) && e.OcurridoEn >= i && e.OcurridoEn < f)
            .Select(e => e.ComparendoId);
        var porDp = db.DerechosPeticion.AsNoTracking()
            .Where(d => !d.EnvioAutomatico && (d.Estado == EstadoDp.Enviado || d.Estado == EstadoDp.Respondido) && d.FechaRadicacion >= desde && d.FechaRadicacion <= hoy)
            .Select(d => d.ComparendoId);
        var gestionados = await porEventos.Union(porDp).CountAsync(ct).ConfigureAwait(false);

        var avisos = await db.AvisosConductor.AsNoTracking().CountAsync(v => v.RegistradoEn >= i && v.RegistradoEn < f, ct).ConfigureAwait(false);
        var cambios = await db.SolicitudesCambio.AsNoTracking().GroupBy(_ => 1).Select(g => new
        {
            Solicitados = g.Count(s => s.SolicitadoEn >= i && s.SolicitadoEn < f),
            Aceptados = g.Count(s => s.Estado == EstadoSolicitudCambio.AceptadoOt && s.FechaRespuestaOt >= desde && s.FechaRespuestaOt <= hoy),
        }).FirstOrDefaultAsync(ct).ConfigureAwait(false);

        var top = porEmpresa.OrderByDescending(x => x.Ahorro).ThenByDescending(x => x.Favorables).Take(5).ToList();
        var ids = top.Select(x => x.Tenant).ToList();
        var nombres = await db.ConfiguracionesEmpresa.AsNoTracking().Where(c => ids.Contains(c.TenantId))
            .ToDictionaryAsync(c => c.TenantId, c => c.EmpresaNombre, ct).ConfigureAwait(false);

        var totalEnviados = enviados.Sum(x => x.N);
        var totalFavorables = porEmpresa.Sum(x => x.Favorables);
        return new ImpactoPlataforma(
            hoy.Year,
            desde,
            hoy,
            porEmpresa.Sum(x => x.Ahorro),
            new ImpactoDp(totalEnviados, totalFavorables, totalFavorables + desfavorables == 0 ? null : Math.Round((decimal)totalFavorables / (totalFavorables + desfavorables), 4)),
            gestionados,
            avisos,
            new ImpactoCambios(cambios?.Solicitados ?? 0, cambios?.Aceptados ?? 0),
            top.Select(x => new EmpresaConImpacto(x.Tenant, nombres.GetValueOrDefault(x.Tenant) ?? x.Tenant.ToString(), x.Ahorro, x.Favorables)).ToList());
    }
}
