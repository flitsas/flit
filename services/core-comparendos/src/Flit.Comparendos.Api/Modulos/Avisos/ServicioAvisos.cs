using Flit.Comparendos.Api.Modulos.Calendario;
using Flit.Comparendos.Api.Modulos.Comparendos;
using Flit.Comparendos.Api.Modulos.Configuracion;
using Flit.Comparendos.Api.Modulos.Correo;
using Flit.Comparendos.Api.Modulos.Plantillas;
using Flit.Comparendos.Api.Modulos.Portal;
using Flit.Comparendos.Api.Persistence;
using Flit.Comparendos.Api.Persistence.Modelo;
using Flit.Comparendos.Api.Plataforma;
using Flit.Comparendos.Domain.Modelo;
using Flit.Comparendos.Domain.Plazos;
using Microsoft.EntityFrameworkCore;

namespace Flit.Comparendos.Api.Modulos.Avisos;

/// <summary>
/// Avisos al conductor (HU #13485; §6.6; Excel C9, C10). Los avisos 1 a 3 siguen las ventanas de descuento: 1 inicial
/// (con la ventana vigente), 2 al pasar al 25% (si el 1 salió en el 50%) y 3 al perder el descuento (si el último salió
/// con descuento). Nunca retroactivos (RN-114) y sin motor 10/5/1 en esta entrega (S-01). El aviso 4 es la carta del
/// cambio de contraventor y lo dispara ese flujo.
/// </summary>
public sealed class ServicioAvisos(
    ComparendosDb db,
    ServicioCorreo correo,
    ServicioPlantillas plantillas,
    ContextoPlantilla contexto,
    ServicioPortal portal,
    CalendarioService calendario,
    Capacidades capacidades,
    Timeline timeline,
    AdjuntosAviso adjuntos,
    Reloj reloj)
{
    /// <summary>Elegibilidad común E(c) (§6.6) con el motivo cuando no aplica.</summary>
    public async Task<string?> NoElegibleAsync(Comparendo c, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(c);
        if (!c.Activo)
            return "El comparendo está inactivo.";
        if (c.Pagado)
            return "El comparendo ya está pagado.";
        if (c.Contraventor is null)
            return "El comparendo no tiene conductor asignado.";
        if (string.IsNullOrWhiteSpace(c.Contraventor.Correo))
            return "El conductor no tiene correo.";
        var config = await capacidades.DeAsync(c.TenantId, ct).ConfigureAwait(false);
        return config.NotificationChannel != CanalComunicacion.Email ? "El canal de avisos de la empresa no es correo." : null;
    }

    /// <summary>
    /// Qué aviso automático toca hoy (§6.6), o null. Requiere además no estar notificado en persona y tener fecha base.
    /// </summary>
    public async Task<TipoAviso?> AutomaticoQueTocaAsync(Comparendo c, VentanaDescuento ventana, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(c);
        if (c.NotificadoFisicamente || ventana == VentanaDescuento.SinFechaBase)
            return null;
        if (c.EstadoNotificacionConductor == EstadoNotificacionConductor.SinNotificar)
            return TipoAviso.Aviso1;

        var ultimo = await db.AvisosConductor.AsNoTracking().Where(a => a.ComparendoId == c.Id && a.TipoAviso != TipoAviso.Aviso4)
            .OrderByDescending(a => a.RegistradoEn).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        var aviso1 = await db.AvisosConductor.AsNoTracking().Where(a => a.ComparendoId == c.Id && a.TipoAviso == TipoAviso.Aviso1)
            .OrderBy(a => a.RegistradoEn).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (ventana == VentanaDescuento.SinDescuento && c.EstadoNotificacionConductor is EstadoNotificacionConductor.Aviso1 or EstadoNotificacionConductor.Aviso2
            && ultimo?.VentanaAlEnvio is VentanaDescuento.Descuento50 or VentanaDescuento.Descuento25)
            return TipoAviso.Aviso3;
        if (ventana == VentanaDescuento.Descuento25 && c.EstadoNotificacionConductor == EstadoNotificacionConductor.Aviso1 && aviso1?.VentanaAlEnvio == VentanaDescuento.Descuento50)
            return TipoAviso.Aviso2;
        return null;
    }

    /// <summary>Envía un aviso 1, 2 o 3 (automático o manual) y lo deja en la gestión del comparendo.</summary>
    public async Task<AvisoConductor> EnviarAsync(Comparendo c, TipoAviso tipo, Disparo disparo, Actor actor, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(c);
        ArgumentNullException.ThrowIfNull(actor);
        if (tipo == TipoAviso.Aviso4)
            throw new ReglaException("AVISO_4_DESDE_CAMBIO", "El aviso 4 sale con la solicitud de cambio de contraventor.");
        if (await NoElegibleAsync(c, ct).ConfigureAwait(false) is { } motivo)
            throw new ReglaException("AVISO_NO_PROCEDE", motivo);

        var config = await capacidades.DeAsync(c.TenantId, ct).ConfigureAwait(false);
        var motor = await calendario.MotorAsync(ct).ConfigureAwait(false);
        var s = motor.Evaluar(Plazos.Fechas(c), reloj.Hoy);
        var ventana = Plazos.Ventana(s, c);
        var enlace = portal.Emitir(c);
        var valores = await contexto.DeAsync(c, new ExtrasContexto(PortalEnlace: enlace.Url, PortalVence: enlace.Vence), ct).ConfigureAwait(false);
        var adjuntado = await adjuntos.ParaAvisoAsync(c, config, tipo, ct).ConfigureAwait(false);
        valores["adjuntos.autorizacion"] = adjuntado.Autorizacion ? "sí" : null;
        valores["adjuntos.poder"] = adjuntado.Poder ? "sí" : null;
        var plantilla = tipo switch { TipoAviso.Aviso1 => TipoPlantilla.CorreoAviso1, TipoAviso.Aviso2 => TipoPlantilla.CorreoAviso2, _ => TipoPlantilla.CorreoAviso3 };
        var comunicacion = tipo switch { TipoAviso.Aviso1 => TipoComunicacion.Aviso1, TipoAviso.Aviso2 => TipoComunicacion.Aviso2, _ => TipoComunicacion.Aviso3 };
        var armado = await plantillas.ArmarAsync(config.ConjuntoPlantillas, plantilla, null, valores, ct).ConfigureAwait(false);
        var enviados = await correo.RegistrarAsync(new CorreoNuevo
        {
            TenantId = c.TenantId,
            ComparendoId = c.Id,
            Tipo = comunicacion,
            Plantilla = $"comparendos.{comunicacion.ToString().ToLowerInvariant()}",
            PlantillaVersionId = armado.VersionId,
            Para = [new Destinatario(c.Contraventor!.Correo!, c.Contraventor.Nombres)],
            Cc = EmpresasEndpoints.CopiasPara(config.Cc, comunicacion),
            Bcc = EmpresasEndpoints.CopiasPara(config.Bcc, comunicacion),
            Asunto = armado.Asunto ?? "Comparendo",
            Html = armado.Html,
            Adjuntos = adjuntado.Archivos,
            Canal = config.CanalCorreo,
            RemitenteNombre = config.RemitenteNombre ?? config.EmpresaNombre,
            CreadoPor = actor.UsuarioId,
        }, ct).ConfigureAwait(false);

        var porcentaje = ventana switch { VentanaDescuento.Descuento50 => 50, VentanaDescuento.Descuento25 => 25, _ => 0 };
        var aviso = new AvisoConductor
        {
            Id = Guid.CreateVersion7(),
            TenantId = c.TenantId,
            ComparendoId = c.Id,
            TipoAviso = tipo,
            Disparo = disparo,
            VentanaAlEnvio = ventana,
            PorcentajeDescuento = (short)porcentaje,
            FechaLimiteVentana = ventana switch { VentanaDescuento.Descuento50 => c.FechaFinDescuento50, VentanaDescuento.Descuento25 => c.FechaFinDescuento25, _ => null },
            CorreoDestino = c.Contraventor.Correo!,
            ComunicacionId = enviados[0].Id,
            PortalEnlaceId = enlace.Id,
            EnviadoPor = actor.UsuarioId,
            RegistradoEn = reloj.Ahora,
        };
        db.AvisosConductor.Add(aviso);

        // El estado no retrocede: un reenvío de un aviso igual o anterior solo deja evento y comunicación (§4.5).
        var nuevo = tipo switch { TipoAviso.Aviso1 => EstadoNotificacionConductor.Aviso1, TipoAviso.Aviso2 => EstadoNotificacionConductor.Aviso2, _ => EstadoNotificacionConductor.Aviso3 };
        if (nuevo > c.EstadoNotificacionConductor)
            c.EstadoNotificacionConductor = nuevo;
        c.Gestionado = true;
        timeline.Agregar(c, TipoEventoTimeline.AvisoEnviado,
            $"{Nombre(tipo)} {(disparo == Disparo.Manual ? "enviado a mano" : "enviado automáticamente")} a {c.Contraventor.Correo} ({(porcentaje > 0 ? $"descuento del {porcentaje}%" : "sin descuento")}).{Faltantes(adjuntado)}",
            actor, new { tipo = tipo.ToString(), ventana = ventana.ToString(), destinatario = c.Contraventor.Correo, adjuntos = adjuntado.Archivos.Select(a => a.Nombre), faltantes = adjuntado.Faltantes },
            "comunicacion_saliente", enviados[0].Id);
        return aviso;
    }

    /// <summary>HU #13884: lo que no se pudo adjuntar, dicho en la línea de tiempo del comparendo.</summary>
    public static string Faltantes(Adjuntos a) => a.Faltantes.Count == 0 ? string.Empty : $" Salió sin {string.Join(" ni ", a.Faltantes)}.";

    public static string Nombre(TipoAviso t) => t switch
    {
        TipoAviso.Aviso1 => "Aviso 1 (inicial)",
        TipoAviso.Aviso2 => "Aviso 2 (descuento del 25%)",
        TipoAviso.Aviso3 => "Aviso 3 (sin descuento)",
        _ => "Aviso 4 (cambio de contraventor)",
    };
}
