using System.Globalization;
using Flit.Comparendos.Api.Modulos.Calendario;
using Flit.Comparendos.Api.Persistence;
using Flit.Comparendos.Api.Persistence.Modelo;
using Flit.Comparendos.Api.Plataforma;
using Flit.Comparendos.Domain.Modelo;
using Flit.Comparendos.Domain.Tiempo;
using Microsoft.EntityFrameworkCore;

namespace Flit.Comparendos.Api.Modulos.Reportes;

/// <summary>Alcance de un reporte: una empresa (o todas, solo SuperAdmin), un NIT monitoreado opcional y un periodo en fechas de Colombia.</summary>
public sealed record Alcance(Guid? Tenant, Guid? Nit, DateOnly Desde, DateOnly Hasta)
{
    public DateTimeOffset InicioUtc => Utc(Desde);

    public DateTimeOffset FinUtc => Utc(Hasta.AddDays(1));

    public static DateTimeOffset Utc(DateOnly d) => new DateTimeOffset(d.ToDateTime(TimeOnly.MinValue), ColombiaTime.Offset).ToUniversalTime();
}

public sealed record Kpis(
    decimal DeudaActivaTotal,
    int ComparendosConDeuda,
    int DpFavorables,
    int DpDesfavorables,
    decimal? TasaExitoDp,
    int DpSinRespuesta,
    decimal AhorroAcumulado,
    int RadarCantidad,
    decimal RadarValor,
    DateOnly RadarHasta);

public sealed record PuntoMensual(string Periodo, int Ingresados, decimal ValorIngresado, int DpEnviados, int DpFavorables, decimal Ahorro, int PagosCc, int PagosPc, int PagosBc);

public sealed record ParGestion(Guid Usuario, Guid Comparendo);

public sealed record FilaProductividad(Guid UsuarioId, string Usuario, int Gestionados, int DpEnviados, int ContraventoresAsignados, int Pagos, int AvisosManuales, int OcrRegistrados, int RespuestasOt);

/// <summary>
/// Dashboard ejecutivo y productividad (HU #13498; §6.12, §6.16). Consultas directas en tiempo real; los montos son
/// del comparendo o del DP al radicar. Las acciones automáticas (API, scheduler, favorable automático) no se atribuyen.
/// </summary>
public sealed class ServicioReportes(ComparendosDb db, CalendarioService calendario, Reloj reloj)
{
    public static readonly string[] Metricas = ["gestionados", "dpEnviados", "contraventoresAsignados", "pagos", "avisosManuales", "ocrRegistrados", "respuestasOt"];

    // El DP_ENVIADO del timeline lo registra el sistema al confirmar la entrega: el DP se atribuye por su enviado_por.
    private static readonly TipoEventoTimeline[] EventosDeGestion =
        [TipoEventoTimeline.PagoRegistrado, TipoEventoTimeline.ContraventorAsignado, TipoEventoTimeline.ContraventorReemplazado, TipoEventoTimeline.AvisoEnviado];

    /// <summary>(usuario, comparendo) de cada acción de gestión del periodo: eventos del usuario más los DPs que envió.</summary>
    private async Task<List<ParGestion>> GestionesAsync(Alcance a, CancellationToken ct)
    {
        var (i, f, t) = (a.InicioUtc, a.FinUtc, a.Tenant);
        var eventos = await db.Timeline.AsNoTracking()
            .Where(e => (t == null || e.TenantId == t) && e.ActorTipo == ActorTipo.Usuario && e.ActorUsuarioId != null && EventosDeGestion.Contains(e.Tipo) && e.OcurridoEn >= i && e.OcurridoEn < f)
            .Select(e => new { Usuario = e.ActorUsuarioId!.Value, e.ComparendoId }).Distinct().ToListAsync(ct).ConfigureAwait(false);
        var dps = await db.DerechosPeticion.AsNoTracking()
            .Where(d => (t == null || d.TenantId == t) && (d.Estado == EstadoDp.Enviado || d.Estado == EstadoDp.Respondido) && d.FechaRadicacion >= a.Desde && d.FechaRadicacion <= a.Hasta)
            .Select(d => new { Usuario = d.EnviadoPor, d.ComparendoId }).Distinct().ToListAsync(ct).ConfigureAwait(false);
        return eventos.Select(x => new ParGestion(x.Usuario, x.ComparendoId)).Concat(dps.Select(x => new ParGestion(x.Usuario, x.ComparendoId))).ToList();
    }

    private IQueryable<Comparendo> Comparendos(Alcance a) =>
        db.Comparendos.AsNoTracking().Where(c => (a.Tenant == null || c.TenantId == a.Tenant) && (a.Nit == null || c.IdentificacionMonitoreadaId == a.Nit));

    private IQueryable<DerechoPeticion> Dps(Alcance a) =>
        from d in db.DerechosPeticion.AsNoTracking()
        join c in Comparendos(a) on d.ComparendoId equals c.Id
        select d;

    public async Task<Kpis> KpisAsync(Alcance a, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(a);
        var favorables = Dps(a).Where(d => d.Estado == EstadoDp.Respondido && d.RespuestaSentido == SentidoRespuestaOt.Favorable);
        var conFavorable = favorables.Select(d => d.ComparendoId);
        var deuda = Comparendos(a).Where(c => c.Activo && !c.Pagado && !conFavorable.Contains(c.Id));
        var deudaTotal = await deuda.SumAsync(c => c.ValorTotal ?? 0, ct).ConfigureAwait(false);
        var deudaCuenta = await deuda.CountAsync(ct).ConfigureAwait(false);

        var respondidas = Dps(a).Where(d => d.Estado == EstadoDp.Respondido && d.RespuestaFecha >= a.Desde && d.RespuestaFecha <= a.Hasta);
        var fav = await respondidas.CountAsync(d => d.RespuestaSentido == SentidoRespuestaOt.Favorable, ct).ConfigureAwait(false);
        var desf = await respondidas.CountAsync(d => d.RespuestaSentido == SentidoRespuestaOt.Desfavorable, ct).ConfigureAwait(false);
        var sinRespuesta = await Dps(a).CountAsync(d => d.Estado == EstadoDp.Enviado, ct).ConfigureAwait(false);

        // Ahorro: cada comparendo una vez (el último DP favorable del periodo).
        var ahorros = await respondidas.Where(d => d.RespuestaSentido == SentidoRespuestaOt.Favorable)
            .GroupBy(d => d.ComparendoId).Select(g => g.OrderByDescending(d => d.RespuestaFecha).Select(d => d.ValorAlRadicar).First())
            .ToListAsync(ct).ConfigureAwait(false);

        var (cantidad, valor, hasta) = await RadarAsync(a, ct).ConfigureAwait(false);
        return new Kpis(deudaTotal, deudaCuenta, fav, desf, fav + desf == 0 ? null : Math.Round((decimal)fav / (fav + desf), 4), sinRespuesta,
            ahorros.Sum(v => v ?? 0), cantidad, valor, hasta);
    }

    /// <summary>Radar de vencimientos (S-56): en tiempo, sin DP ni pago, con límite de gestión en los próximos 7 días hábiles.</summary>
    public IQueryable<Comparendo> Radar(Alcance a, DateOnly hoy, DateOnly limite) =>
        Comparendos(a).Where(c => c.Activo && !c.Pagado && !c.TieneDp && c.FechaLimiteGestion != null && hoy <= c.FechaLimiteGestion && c.FechaLimiteGestion <= limite);

    public async Task<DateOnly> LimiteRadarAsync(CancellationToken ct) =>
        (await calendario.ActualAsync(ct).ConfigureAwait(false)).SumarHabiles(reloj.Hoy, 7);

    private async Task<(int, decimal, DateOnly)> RadarAsync(Alcance a, CancellationToken ct)
    {
        var limite = await LimiteRadarAsync(ct).ConfigureAwait(false);
        var q = Radar(a, reloj.Hoy, limite);
        return (await q.CountAsync(ct).ConfigureAwait(false), await q.SumAsync(c => c.ValorTotal ?? 0, ct).ConfigureAwait(false), limite);
    }

    /// <summary>Serie mensual desde enero del año anterior hasta el mes actual (sirve para «últimos 12 meses» y «año contra año»).</summary>
    public async Task<List<PuntoMensual>> SerieAsync(Alcance a, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(a);
        var hoy = reloj.Hoy;
        var desde = new DateOnly(hoy.Year - 1, 1, 1);
        var inicio = Alcance.Utc(desde);
        var ingresos = await Comparendos(a).Where(c => c.CreadoEn >= inicio).Select(c => new { c.CreadoEn, c.ValorTotal }).ToListAsync(ct).ConfigureAwait(false);
        var enviados = await Dps(a).Where(d => d.FechaRadicacion >= desde && (d.Estado == EstadoDp.Enviado || d.Estado == EstadoDp.Respondido))
            .Select(d => d.FechaRadicacion!.Value).ToListAsync(ct).ConfigureAwait(false);
        var favorables = await Dps(a).Where(d => d.Estado == EstadoDp.Respondido && d.RespuestaSentido == SentidoRespuestaOt.Favorable && d.RespuestaFecha >= desde)
            .Select(d => new { Fecha = d.RespuestaFecha!.Value, d.ValorAlRadicar }).ToListAsync(ct).ConfigureAwait(false);
        var pagos = await (from p in db.Pagos.AsNoTracking()
                           join c in Comparendos(a) on p.ComparendoId equals c.Id
                           where p.FechaPago >= desde
                           select new { p.FechaPago, p.TipoPago }).ToListAsync(ct).ConfigureAwait(false);

        static string Mes(DateOnly d) => d.ToString("yyyy-MM", CultureInfo.InvariantCulture);
        var serie = new List<PuntoMensual>();
        for (var m = desde; m <= hoy; m = m.AddMonths(1))
        {
            var clave = Mes(m);
            var ing = ingresos.Where(i => Mes(DateOnly.FromDateTime(ColombiaTime.From(i.CreadoEn).DateTime)) == clave).ToList();
            var fav = favorables.Where(f => Mes(f.Fecha) == clave).ToList();
            var pag = pagos.Where(p => Mes(p.FechaPago) == clave).ToList();
            serie.Add(new PuntoMensual(clave, ing.Count, ing.Sum(i => i.ValorTotal ?? 0), enviados.Count(e => Mes(e) == clave), fav.Count, fav.Sum(f => f.ValorAlRadicar ?? 0),
                pag.Count(p => p.TipoPago == TipoPago.CC), pag.Count(p => p.TipoPago == TipoPago.PC), pag.Count(p => p.TipoPago == TipoPago.BC)));
        }

        return serie;
    }

    /// <summary>Totales por NIT monitoreado (lo que entró por placa o sin NIT va en una fila aparte).</summary>
    public async Task<List<object>> TotalesPorNitAsync(Alcance a, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(a);
        var filas = await Comparendos(a).GroupBy(c => c.IdentificacionMonitoreadaId).Select(g => new
        {
            Nit = g.Key,
            Total = g.Count(),
            Activos = g.Count(c => c.Activo),
            Valor = g.Where(c => c.Activo && !c.Pagado).Sum(c => c.ValorTotal ?? 0),
            ConDp = g.Count(c => c.TieneDp),
            Pagados = g.Count(c => c.Pagado),
        }).ToListAsync(ct).ConfigureAwait(false);
        var ids = filas.Where(f => f.Nit != null).Select(f => f.Nit!.Value).ToList();
        var nits = await db.IdentificacionesMonitoreadas.AsNoTracking().Where(i => ids.Contains(i.Id))
            .ToDictionaryAsync(i => i.Id, i => new { i.TipoDocumento, i.NumeroBase, i.DigitoVerificacion, i.NombreTitular }, ct).ConfigureAwait(false);
        return filas.OrderByDescending(f => f.Valor).Select(f => (object)new
        {
            nitId = f.Nit,
            nit = f.Nit is { } id && nits.TryGetValue(id, out var n) ? $"{n.NumeroBase}{(n.DigitoVerificacion is { } dv ? $"-{dv}" : string.Empty)}" : null,
            titular = f.Nit is { } id2 && nits.TryGetValue(id2, out var n2) ? n2.NombreTitular : "Sin NIT (ingresó por placa u OCR)",
            f.Total, f.Activos, deudaActiva = f.Valor, f.ConDp, f.Pagados,
        }).ToList();
    }

    /// <summary>Productividad por usuario (§6.12): solo acciones de usuarios, nunca del sistema.</summary>
    public async Task<List<FilaProductividad>> ProductividadAsync(Alcance a, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(a);
        var (i, f) = (a.InicioUtc, a.FinUtc);
        var t = a.Tenant;
        var gestionados = (await GestionesAsync(a, ct).ConfigureAwait(false))
            .GroupBy(p => p.Usuario).ToDictionary(g => g.Key, g => g.Select(p => p.Comparendo).Distinct().Count());
        var dps = await db.DerechosPeticion.AsNoTracking()
            .Where(d => (t == null || d.TenantId == t) && (d.Estado == EstadoDp.Enviado || d.Estado == EstadoDp.Respondido) && d.FechaRadicacion >= a.Desde && d.FechaRadicacion <= a.Hasta)
            .GroupBy(d => d.EnviadoPor).Select(g => new { Usuario = g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Usuario, x => x.N, ct).ConfigureAwait(false);
        var contraventores = await db.ContraventorHistorial.AsNoTracking()
            .Where(h => (t == null || h.TenantId == t) && h.Origen == OrigenContraventor.Manual && h.UsuarioId != null && h.RegistradoEn >= i && h.RegistradoEn < f)
            .GroupBy(h => h.UsuarioId!.Value).Select(g => new { Usuario = g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Usuario, x => x.N, ct).ConfigureAwait(false);
        var pagos = await db.Pagos.AsNoTracking()
            .Where(p => (t == null || p.TenantId == t) && p.RegistradoEn >= i && p.RegistradoEn < f)
            .GroupBy(p => p.RegistradoPor).Select(g => new { Usuario = g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Usuario, x => x.N, ct).ConfigureAwait(false);
        var avisos = await db.AvisosConductor.AsNoTracking()
            .Where(v => (t == null || v.TenantId == t) && v.Disparo == Disparo.Manual && v.EnviadoPor != null && v.RegistradoEn >= i && v.RegistradoEn < f)
            .GroupBy(v => v.EnviadoPor!.Value).Select(g => new { Usuario = g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Usuario, x => x.N, ct).ConfigureAwait(false);
        var ocr = await db.DocumentosOcr.AsNoTracking()
            .Where(d => (t == null || d.TenantId == t) && d.Estado == EstadoDocumentoOcr.Registrado && d.RevisadoPor != null && d.RevisadoEn >= i && d.RevisadoEn < f)
            .GroupBy(d => d.RevisadoPor!.Value).Select(g => new { Usuario = g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Usuario, x => x.N, ct).ConfigureAwait(false);
        var respuestas = await db.DerechosPeticion.AsNoTracking()
            .Where(d => (t == null || d.TenantId == t) && d.RespuestaRegistradaPor != null && d.RespuestaRegistradaEn >= i && d.RespuestaRegistradaEn < f)
            .GroupBy(d => d.RespuestaRegistradaPor!.Value).Select(g => new { Usuario = g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Usuario, x => x.N, ct).ConfigureAwait(false);

        var usuarios = gestionados.Keys.Concat(dps.Keys).Concat(contraventores.Keys).Concat(pagos.Keys).Concat(avisos.Keys).Concat(ocr.Keys).Concat(respuestas.Keys)
            .Where(u => u != Guid.Empty).Distinct().ToList();
        var nombres = await NombresAsync(usuarios, ct).ConfigureAwait(false);
        return usuarios.Select(u => new FilaProductividad(u, nombres.GetValueOrDefault(u, u.ToString()), gestionados.GetValueOrDefault(u), dps.GetValueOrDefault(u),
                contraventores.GetValueOrDefault(u), pagos.GetValueOrDefault(u), avisos.GetValueOrDefault(u), ocr.GetValueOrDefault(u), respuestas.GetValueOrDefault(u)))
            .OrderByDescending(x => x.Gestionados).ThenBy(x => x.Usuario, StringComparer.Ordinal).ToList();
    }

    /// <summary>El producto no tiene directorio de usuarios: el nombre es el último con que el usuario quedó en el timeline.</summary>
    private async Task<Dictionary<Guid, string>> NombresAsync(List<Guid> usuarios, CancellationToken ct) =>
        await db.Timeline.AsNoTracking().Where(e => e.ActorUsuarioId != null && usuarios.Contains(e.ActorUsuarioId.Value))
            .GroupBy(e => e.ActorUsuarioId!.Value).Select(g => new { Id = g.Key, Nombre = g.OrderByDescending(e => e.OcurridoEn).Select(e => e.ActorNombre).First() })
            .ToDictionaryAsync(x => x.Id, x => x.Nombre, ct).ConfigureAwait(false);

    /// <summary>Los comparendos que componen una cifra de productividad (clic en la cifra, §7.9).</summary>
    public IQueryable<Guid> ComparendosDeMetrica(Alcance a, Guid usuario, string metrica)
    {
        ArgumentNullException.ThrowIfNull(a);
        var (i, f, t) = (a.InicioUtc, a.FinUtc, a.Tenant);
        return metrica switch
        {
            "gestionados" => db.Timeline.Where(e => (t == null || e.TenantId == t) && e.ActorTipo == ActorTipo.Usuario && e.ActorUsuarioId == usuario
                    && EventosDeGestion.Contains(e.Tipo) && e.OcurridoEn >= i && e.OcurridoEn < f).Select(e => e.ComparendoId)
                .Union(ComparendosDeMetrica(a, usuario, "dpEnviados")),
            "dpEnviados" => db.DerechosPeticion.Where(d => (t == null || d.TenantId == t) && d.EnviadoPor == usuario
                && (d.Estado == EstadoDp.Enviado || d.Estado == EstadoDp.Respondido) && d.FechaRadicacion >= a.Desde && d.FechaRadicacion <= a.Hasta).Select(d => d.ComparendoId),
            "contraventoresAsignados" => db.ContraventorHistorial.Where(h => (t == null || h.TenantId == t) && h.Origen == OrigenContraventor.Manual && h.UsuarioId == usuario
                && h.RegistradoEn >= i && h.RegistradoEn < f).Select(h => h.ComparendoId),
            "pagos" => db.Pagos.Where(p => (t == null || p.TenantId == t) && p.RegistradoPor == usuario && p.RegistradoEn >= i && p.RegistradoEn < f).Select(p => p.ComparendoId),
            "avisosManuales" => db.AvisosConductor.Where(v => (t == null || v.TenantId == t) && v.Disparo == Disparo.Manual && v.EnviadoPor == usuario
                && v.RegistradoEn >= i && v.RegistradoEn < f).Select(v => v.ComparendoId),
            "ocrRegistrados" => db.DocumentosOcr.Where(d => (t == null || d.TenantId == t) && d.Estado == EstadoDocumentoOcr.Registrado && d.RevisadoPor == usuario
                && d.RevisadoEn >= i && d.RevisadoEn < f && d.ComparendoId != null).Select(d => d.ComparendoId!.Value),
            "respuestasOt" => db.DerechosPeticion.Where(d => (t == null || d.TenantId == t) && d.RespuestaRegistradaPor == usuario
                && d.RespuestaRegistradaEn >= i && d.RespuestaRegistradaEn < f).Select(d => d.ComparendoId),
            _ => throw new ReglaException("METRICA_INVALIDA", $"Métrica inválida. Usa: {string.Join(", ", Metricas)}."),
        };
    }
}
