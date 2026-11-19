using System.Threading.RateLimiting;
using Flit.Comparendos.Api.Modulos.Calendario;
using Flit.Comparendos.Api.Modulos.Comparendos;
using Flit.Comparendos.Api.Modulos.Comun;
using Flit.Comparendos.Api.Modulos.Plantillas;
using Flit.Comparendos.Api.Persistence;
using Flit.Comparendos.Api.Plataforma;
using Flit.Comparendos.Domain.Modelo;
using Flit.Comparendos.Domain.Tiempo;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace Flit.Comparendos.Api.Modulos.Portal;

/// <summary>
/// Portal del conductor (HU #13480, §7.17): público, sin login, por enlace único. Muestra el comparendo, sus plazos y
/// descuentos y la evidencia; nunca datos del representante, gestión jurídica ni pagos. Un enlace inválido, vencido o
/// revocado responde siempre lo mismo (404). Límite de 30 peticiones por minuto por IP.
/// </summary>
public static class PortalEndpoints
{
    public const string PoliticaLimite = "portal";

    public static IServiceCollection AddLimitePortal(this IServiceCollection services) =>
        services.AddRateLimiter(o =>
        {
            o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            o.AddPolicy(PoliticaLimite, http => RateLimitPartition.GetFixedWindowLimiter(
                // X-Real-IP lo pone nginx con la IP real (pisa lo que mande el cliente); X-Forwarded-For se puede falsear desde afuera.
                http.Request.Headers["X-Real-IP"].ToString().Trim() is { Length: > 0 } ip ? ip : http.Connection.RemoteIpAddress?.ToString() ?? "desconocida",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = 30, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
        });

    public static void Map(RouteGroupBuilder api)
    {
        var publico = api.MapGroup("/publico/portal").AllowAnonymous().RequireRateLimiting(PoliticaLimite);
        publico.MapGet("/{token}", Ver);
        publico.MapGet("/{token}/evidencia", Evidencia);
        api.MapPost("/comparendos/{id:guid}/portal/revocar", Revocar).RequirePermiso(Permisos.AvisosEnviar);
    }

    private static async Task<Persistence.Modelo.PortalEnlace?> ValidoAsync(ComparendosDb db, string token, Reloj reloj, CancellationToken ct)
    {
        var hash = ServicioPortal.Hash(token);
        var enlace = await db.PortalEnlaces.FirstOrDefaultAsync(p => p.TokenHash == hash, ct).ConfigureAwait(false);
        return enlace is null || enlace.RevocadoEn is not null || enlace.ExpiraEn < reloj.Ahora ? null : enlace;
    }

    private static async Task<IResult> Ver(string token, ComparendosDb db, Reloj reloj, CalendarioService calendario, Timeline timeline, CancellationToken ct)
    {
        var enlace = await ValidoAsync(db, token, reloj, ct).ConfigureAwait(false);
        if (enlace is null)
            return Results.NotFound(new { code = "ENLACE_NO_DISPONIBLE", error = "Este enlace no está disponible." });

        var c = await db.Comparendos.Include(x => x.Contraventor).FirstAsync(x => x.Id == enlace.ComparendoId, ct).ConfigureAwait(false);
        var ot = c.OrganismoTransitoId is { } otId ? await db.OrganismosTransito.AsNoTracking().Where(o => o.Id == otId).Select(o => o.Nombre).FirstOrDefaultAsync(ct).ConfigureAwait(false) : null;
        var config = await db.ConfiguracionesEmpresa.AsNoTracking().FirstOrDefaultAsync(e => e.TenantId == c.TenantId, ct).ConfigureAwait(false);
        var motor = await calendario.MotorAsync(ct).ConfigureAwait(false);
        var s = motor.Evaluar(Plazos.Fechas(c), reloj.Hoy);

        // Un evento de apertura por día y enlace.
        var hoy = reloj.Hoy;
        var ultimo = enlace.UltimoAccesoEn is { } u ? ColombiaTime.DayOf(u) : (DateOnly?)null;
        enlace.Accesos++;
        enlace.UltimoAccesoEn = reloj.Ahora;
        if (ultimo != hoy)
            timeline.Agregar(c, TipoEventoTimeline.PortalAbierto, "El conductor abrió el portal del comparendo.", Actor.Conductor, relacionadoTipo: "portal_enlace", relacionadoId: enlace.Id);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        return Results.Ok(new
        {
            empresa = config?.EmpresaNombre,
            conjunto = (config?.ConjuntoPlantillas ?? ConjuntoPlantillas.Flit).ToString(),
            conductor = c.Contraventor is { } k ? new { k.Nombres, documento = k.NumeroDocumento is null ? null : $"{k.TipoDocumento} {HtmlCorreo.Enmascarar(k.NumeroDocumento)}" } : null,
            comparendo = new
            {
                c.Numero,
                c.Placa,
                c.FechaImposicion,
                hora = c.HoraImposicion,
                c.FechaNotificacion,
                organismo = ot ?? c.SecretariaTexto,
                infraccion = new { codigo = c.CodigoInfraccion, descripcion = c.DescripcionInfraccion },
                direccion = c.DireccionInfraccion,
                valorTotal = c.ValorTotal,
                valorTexto = Formato.Pesos(c.ValorTotal),
                esFotomulta = c.EsFotomulta != false,
            },
            descuento = new
            {
                ventana = Plazos.Ventana(s, c).ToString(),
                finDescuento50 = c.FechaFinDescuento50,
                finDescuento25 = c.FechaFinDescuento25,
                texto = Plazos.Ventana(s, c) switch
                {
                    VentanaDescuento.Descuento50 => $"50% de descuento hasta el {Formato.Fecha(c.FechaFinDescuento50)} y 25% hasta el {Formato.Fecha(c.FechaFinDescuento25)}.",
                    VentanaDescuento.Descuento25 => $"25% de descuento hasta el {Formato.Fecha(c.FechaFinDescuento25)}.",
                    VentanaDescuento.SinDescuento => "El plazo de descuento terminó: el valor a pagar es el 100%.",
                    _ => "El comparendo todavía no tiene fecha de notificación.",
                },
            },
            dondePagar = "Para pagar con descuento debes hacer el curso de seguridad vial en un Centro Integral de Atención (CIA) autorizado y pagar en el organismo de tránsito o en los canales del SIMIT.",
            evidencia = new { foto = c.FotoEvidenciaUrl, documento = c.ArchivoEvidenciaId is not null },
            vence = ColombiaTime.DayOf(enlace.ExpiraEn),
        });
    }

    /// <summary>El documento original (OCR) como evidencia, solo con un enlace válido.</summary>
    private static async Task<IResult> Evidencia(string token, ComparendosDb db, Reloj reloj, RegistroArchivos archivos, CancellationToken ct)
    {
        var enlace = await ValidoAsync(db, token, reloj, ct).ConfigureAwait(false);
        var archivoId = enlace is null ? null : await db.Comparendos.Where(c => c.Id == enlace.ComparendoId).Select(c => c.ArchivoEvidenciaId).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (archivoId is not { } id || await archivos.LeerAsync(id, ct).ConfigureAwait(false) is not { } leido)
            return Results.NotFound(new { code = "ENLACE_NO_DISPONIBLE", error = "Este enlace no está disponible." });
        return Results.File(leido.Contenido, leido.Archivo.ContentType);
    }

    private static async Task<IResult> Revocar(Guid id, AccesoComparendo acceso, ComparendosDb db, Reloj reloj, CancellationToken ct)
    {
        var c = await acceso.ParaOperarAsync(id, ct).ConfigureAwait(false);
        var n = await db.PortalEnlaces.Where(p => p.ComparendoId == c.Id && p.RevocadoEn == null)
            .ExecuteUpdateAsync(u => u.SetProperty(p => p.RevocadoEn, reloj.Ahora).SetProperty(p => p.MotivoRevocacion, "MANUAL"), ct).ConfigureAwait(false);
        return Results.Ok(new { revocados = n });
    }
}
