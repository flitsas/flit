using System.Globalization;
using System.Net;
using Flit.Comparendos.Api.Modulos.Comun;
using Flit.Comparendos.Api.Modulos.Correo;
using Flit.Comparendos.Api.Modulos.Plantillas;
using Flit.Comparendos.Api.Modulos.Trabajos;
using Flit.Comparendos.Api.Persistence;
using Flit.Comparendos.Api.Plataforma;
using Flit.Comparendos.Domain.Modelo;
using Flit.Comparendos.Domain.Tiempo;
using Microsoft.EntityFrameworkCore;

namespace Flit.Comparendos.Api.Modulos.Reportes;

/// <summary>
/// Reportes automáticos (RN-187, S-61): cierre semanal (viernes en la tarde, lunes a hoy) y mensual (último día, del 1 a
/// hoy) para las empresas con <c>has_reportes_module</c> y destinatarios. Un correo HTML a <c>destinatarios_reportes</c>
/// con copia oculta a la lista interna de FLIT. Un mismo cierre no sale dos veces el mismo día.
/// </summary>
public sealed class ReportesAutomaticos(ComparendosDb db, ServicioReportes reportes, ServicioCorreo correo, Reloj reloj) : IEjecutorTrabajo
{
    public bool Atiende(TipoTrabajo tipo) => tipo is TipoTrabajo.ReporteSemanal or TipoTrabajo.ReporteMensual;

    public async Task EjecutarAsync(SolicitudTrabajo solicitud, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(solicitud);
        var semanal = solicitud.Tipo == TipoTrabajo.ReporteSemanal;
        var hoy = reloj.Hoy;
        var desde = semanal ? hoy.AddDays(-(((int)hoy.DayOfWeek + 6) % 7)) : new DateOnly(hoy.Year, hoy.Month, 1);
        var tipo = semanal ? TipoComunicacion.ReporteSemanal : TipoComunicacion.ReporteMensual;
        var internos = (await db.AlertaConfiguraciones.AsNoTracking().FirstOrDefaultAsync(ct).ConfigureAwait(false))?.CorreosInternosReportes ?? [];
        var inicioHoy = Alcance.Utc(hoy);

        var empresas = await db.ConfiguracionesEmpresa.AsNoTracking()
            .Where(e => e.HasReportesModule && e.DestinatariosReportes.Count > 0 && (solicitud.TenantId == null || e.TenantId == solicitud.TenantId))
            .ToListAsync(ct).ConfigureAwait(false);
        foreach (var empresa in empresas)
        {
            if (await db.Comunicaciones.AnyAsync(c => c.TenantId == empresa.TenantId && c.Tipo == tipo && c.CreadoEn >= inicioHoy, ct).ConfigureAwait(false))
                continue;
            var a = new Alcance(empresa.TenantId, null, desde, hoy);
            var html = await ArmarAsync(a, empresa.EmpresaNombre ?? "la empresa", semanal, ct).ConfigureAwait(false);
            await correo.RegistrarAsync(new CorreoNuevo
            {
                TenantId = empresa.TenantId,
                Tipo = tipo,
                Plantilla = semanal ? "comparendos.reporte_semanal" : "comparendos.reporte_mensual",
                Para = empresa.DestinatariosReportes.Select(d => new Destinatario(d)).ToList(),
                Bcc = internos,
                Asunto = $"{(semanal ? "Cierre semanal" : "Cierre mensual")} de comparendos — {empresa.EmpresaNombre} ({Formato.Fecha(desde)} a {Formato.Fecha(hoy)})",
                Html = html,
                RemitenteNombre = "FLIT Comparendos",
            }, ct).ConfigureAwait(false);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
    }

    public async Task<string> ArmarAsync(Alcance a, string empresa, bool semanal, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(a);
        var k = await reportes.KpisAsync(a, ct).ConfigureAwait(false);
        var (i, f) = (a.InicioUtc, a.FinUtc);
        var nuevos = db.Comparendos.AsNoTracking().Where(c => c.TenantId == a.Tenant && c.CreadoEn >= i && c.CreadoEn < f);
        var dps = db.DerechosPeticion.AsNoTracking().Where(d => d.TenantId == a.Tenant);
        var pagos = db.Pagos.AsNoTracking().Where(p => p.TenantId == a.Tenant && p.FechaPago >= a.Desde && p.FechaPago <= a.Hasta);
        var radar = await reportes.Radar(a, reloj.Hoy, k.RadarHasta).OrderBy(c => c.FechaLimiteGestion).Take(20)
            .Select(c => new { c.Numero, c.Placa, c.ValorTotal, c.FechaLimiteGestion }).ToListAsync(ct).ConfigureAwait(false);

        var resumen = HtmlCorreo.Tabla(
        [
            ("Deuda activa total", $"{Formato.Pesos(k.DeudaActivaTotal)} en {k.ComparendosConDeuda} comparendos"),
            ("Comparendos nuevos", $"{await nuevos.CountAsync(ct).ConfigureAwait(false)} por {Formato.Pesos(await nuevos.SumAsync(c => c.ValorTotal ?? 0, ct).ConfigureAwait(false))}"),
            ("DPs enviados", (await dps.CountAsync(d => d.FechaRadicacion >= a.Desde && d.FechaRadicacion <= a.Hasta && (d.Estado == EstadoDp.Enviado || d.Estado == EstadoDp.Respondido), ct).ConfigureAwait(false)).ToString(CultureInfo.InvariantCulture)),
            ("DPs respondidos", $"{k.DpFavorables} favorables y {k.DpDesfavorables} desfavorables; {k.DpSinRespuesta} en espera de respuesta"),
            ("Tasa de éxito de DP", k.TasaExitoDp is { } t ? (t * 100).ToString("0.#", CultureInfo.InvariantCulture).Replace('.', ',') + " %" : "Sin respuestas en el periodo"),
            ("Ahorro del periodo", Formato.Pesos(k.AhorroAcumulado)),
            ("Pagos registrados", $"{await pagos.CountAsync(ct).ConfigureAwait(false)} por {Formato.Pesos(await pagos.SumAsync(p => p.ValorPagado, ct).ConfigureAwait(false))}"),
            ("Radar de vencimientos", $"{k.RadarCantidad} comparendos por {Formato.Pesos(k.RadarValor)} vencen su gestión antes del {Formato.Fecha(k.RadarHasta)}"),
        ]);
        var lista = radar.Count == 0
            ? "<p>No hay comparendos próximos a vencer.</p>"
            : HtmlCorreo.Tabla(radar.Select(r => ($"{r.Numero} · {r.Placa}", (string?)$"{Formato.Pesos(r.ValorTotal)} — vence {Formato.Fecha(r.FechaLimiteGestion)}")));
        var cuerpo = $"""
            <p>Resumen {(semanal ? "semanal" : "mensual")} de <b>{WebUtility.HtmlEncode(empresa)}</b> del {Formato.Fecha(a.Desde)} al {Formato.Fecha(a.Hasta)}.</p>
            {resumen}
            <h3 style="font-size:15px;margin:20px 0 8px">Próximos a vencer</h3>
            {lista}
            <p><a href="{correo.AppUrl}/reportes">Ver el dashboard</a></p>
            """;
        return HtmlCorreo.Envolver(semanal ? "Cierre semanal de comparendos" : "Cierre mensual de comparendos", cuerpo);
    }
}
