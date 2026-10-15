using System.Text.Json;
using Flit.Comparendos.Api.Modulos.Calendario;
using Flit.Comparendos.Api.Modulos.Comparendos;
using Flit.Comparendos.Api.Modulos.Comun;
using Flit.Comparendos.Api.Modulos.Configuracion;
using Flit.Comparendos.Api.Modulos.Correo;
using Flit.Comparendos.Api.Modulos.Plantillas;
using Flit.Comparendos.Api.Persistence;
using Flit.Comparendos.Api.Persistence.Modelo;
using Flit.Comparendos.Api.Plataforma;
using Flit.Comparendos.Domain.Modelo;
using Flit.Comparendos.Domain.Plazos;
using Microsoft.EntityFrameworkCore;

namespace Flit.Comparendos.Api.Modulos.CambioContraventor;

/// <summary>Una condición de la matriz de viabilidad (§6.10) con su resultado, para mostrarla en verde o rojo.</summary>
public sealed record CondicionViabilidad(string Codigo, string Descripcion, bool Cumple);

/// <summary>
/// Cambio de contraventor ante el organismo de tránsito (HU #13479; ADO #12978, §6.10). Se solicita con el formato
/// firmado por el representante legal, por correo a los correos de radicación del OT con los documentos del conductor
/// y el certificado de representación; al conductor le llega el aviso 4 con la carta, sin esperar respuesta
/// [Excel C10]. El OT responde rechazo (causal obligatoria) o aceptación; tras un rechazo se puede volver a solicitar.
/// </summary>
public sealed class ServicioCambioContraventor(
    ComparendosDb db,
    Capacidades capacidades,
    PermisosOt permisos,
    ResolutorRepresentante resolutor,
    ServicioPlantillas plantillas,
    ContextoPlantilla contexto,
    ServicioCorreo correo,
    RegistroArchivos archivos,
    CalendarioService calendario,
    Timeline timeline,
    Reloj reloj)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<(IReadOnlyList<CondicionViabilidad> Condiciones, RepresentanteLegal? Representante, OrganismoTransito? Organismo)> ViabilidadAsync(Comparendo c, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(c);
        var config = await capacidades.DeAsync(c.TenantId, ct).ConfigureAwait(false);
        var ot = c.OrganismoTransitoId is { } otId ? await db.OrganismosTransito.AsNoTracking().FirstOrDefaultAsync(o => o.Id == otId, ct).ConfigureAwait(false) : null;
        var rl = await resolutor.ResolverAsync(c.TenantId, c.IdentificacionMonitoreadaId, ParaFirmar.CambioContraventor, ct).ConfigureAwait(false);
        var permiso = ot is null ? (true, true) : await permisos.DeAsync(c.TenantId, ot.Id, rl?.Id, ct).ConfigureAwait(false);
        var estado = (await calendario.MotorAsync(ct).ConfigureAwait(false)).Evaluar(Plazos.Fechas(c), reloj.Hoy).Estado;
        var documentos = await db.DocumentosSoporte.AsNoTracking().Where(d => d.ComparendoId == c.Id && d.Vigente).Select(d => d.Tipo).ToListAsync(ct).ConfigureAwait(false);

        List<CondicionViabilidad> condiciones =
        [
            new("CAPACIDAD", "La empresa tiene habilitado el cambio de contraventor", config.AllowsContravenerChange),
            new("OT_MAPEADO", "El comparendo tiene organismo de tránsito identificado", ot is not null),
            new("OT_PERMITE", "El organismo de tránsito admite el cambio de contraventor", ot?.PermiteCambioContraventor == true),
            new("OT_CORREO", "El organismo de tránsito tiene correo de radicación", ot?.CorreosRadicacion.Count > 0),
            new("OT_NO_BLOQUEADO", "La empresa no bloqueó el cambio ante este organismo", permiso.Item2),
            new("ACTIVO", "El comparendo está activo", c.Activo),
            new("NO_PAGADO", "El comparendo no está pagado", !c.Pagado),
            new("EN_PLAZO", "El comparendo no está caducado ni prescrito", estado is not (EstadoLegal.Caducado or EstadoLegal.Prescrito)),
            new("SIN_COBRO", "El comparendo no está en cobro coactivo", !c.EnCobroCoactivo),
            new("CONDUCTOR", "El comparendo tiene conductor asignado", c.Contraventor is not null),
            new("CEDULA", "Está cargada la cédula del conductor", documentos.Contains(TipoDocumentoSoporte.Cedula)),
            new("LICENCIA", "Está cargada la licencia de conducción", documentos.Contains(TipoDocumentoSoporte.Licencia)),
            new("ESTADO", "No hay una solicitud en curso ni aceptada", c.EstadoCambioContraventor is EstadoCambioContraventor.SinSolicitar or EstadoCambioContraventor.RechazadoOt),
            new("REPRESENTANTE", "Hay un representante legal con permiso para firmar", rl is not null || !config.RequiereRepresentanteLegal),
        ];
        return (condiciones, rl, ot);
    }

    public async Task<byte[]> FormatoAsync(Comparendo c, RepresentanteLegal? rl, string? marcaDeAgua, CancellationToken ct)
    {
        var config = await capacidades.DeAsync(c.TenantId, ct).ConfigureAwait(false);
        var firma = rl?.FirmaArchivoId is { } f ? (await archivos.LeerAsync(f, ct).ConfigureAwait(false))?.Contenido : null;
        var valores = await contexto.DeAsync(c, new ExtrasContexto(Representante: rl, FirmaPng: firma), ct).ConfigureAwait(false);
        var armado = await plantillas.ArmarAsync(config.ConjuntoPlantillas, TipoPlantilla.FormatoCambioContraventor, null, valores, ct).ConfigureAwait(false);
        return ServicioPlantillas.Pdf(armado, marcaDeAgua);
    }

    /// <summary>La carta del aviso 4 al conductor tal como saldría, para revisarla antes de solicitar (§7.4).</summary>
    public async Task<byte[]> CartaAsync(Comparendo c, string? marcaDeAgua, CancellationToken ct)
    {
        var config = await capacidades.DeAsync(c.TenantId, ct).ConfigureAwait(false);
        var valores = await contexto.DeAsync(c, null, ct).ConfigureAwait(false);
        return ServicioPlantillas.Pdf(await plantillas.ArmarAsync(config.ConjuntoPlantillas, TipoPlantilla.CartaCambioContraventor, null, valores, ct).ConfigureAwait(false), marcaDeAgua);
    }

    public async Task<CambioContraventorSolicitud> SolicitarAsync(Comparendo c, Actor actor, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(c);
        ArgumentNullException.ThrowIfNull(actor);
        var (condiciones, rl, ot) = await ViabilidadAsync(c, ct).ConfigureAwait(false);
        if (condiciones.FirstOrDefault(x => !x.Cumple) is { } falla)
            throw new ReglaException($"CAMBIO_{falla.Codigo}", $"No se puede solicitar el cambio de contraventor: {falla.Descripcion.ToLowerInvariant()} no se cumple.");

        var config = await capacidades.DeAsync(c.TenantId, ct).ConfigureAwait(false);
        var firma = rl?.FirmaArchivoId is { } f ? (await archivos.LeerAsync(f, ct).ConfigureAwait(false))?.Contenido : null;
        var valores = await contexto.DeAsync(c, new ExtrasContexto(Representante: rl, FirmaPng: firma), ct).ConfigureAwait(false);
        var formato = ServicioPlantillas.Pdf(await plantillas.ArmarAsync(config.ConjuntoPlantillas, TipoPlantilla.FormatoCambioContraventor, null, valores, ct).ConfigureAwait(false));
        var formatoId = await archivos.GuardarAsync(c.TenantId, "formato_cambio_contraventor", $"cambio-contraventor-{c.Numero}.pdf", "application/pdf", formato, ct, actor.UsuarioId).ConfigureAwait(false);

        // Anexos: formato, cédula y licencia vigentes del conductor y certificado de representación del RL.
        var anexos = new List<AdjuntoCorreo> { new($"cambio-contraventor-{c.Numero}.pdf", "application/pdf", formato, formatoId) };
        foreach (var d in await db.DocumentosSoporte.AsNoTracking().Where(d => d.ComparendoId == c.Id && d.Vigente && (d.Tipo == TipoDocumentoSoporte.Cedula || d.Tipo == TipoDocumentoSoporte.Licencia)).ToListAsync(ct).ConfigureAwait(false))
        {
            if (await archivos.LeerAsync(d.ArchivoId, ct).ConfigureAwait(false) is { } leido)
                anexos.Add(new AdjuntoCorreo($"{d.Tipo.ToString().ToLowerInvariant()}-conductor{Path.GetExtension(leido.Archivo.NombreOriginal)}", leido.Archivo.ContentType, leido.Contenido, d.ArchivoId));
        }

        if (rl?.CertificadoArchivoId is { } cert && await archivos.LeerAsync(cert, ct).ConfigureAwait(false) is { } certificado)
            anexos.Add(new AdjuntoCorreo($"certificado-representacion{Path.GetExtension(certificado.Archivo.NombreOriginal)}", certificado.Archivo.ContentType, certificado.Contenido, cert));

        var correoOt = await plantillas.ArmarAsync(config.ConjuntoPlantillas, TipoPlantilla.CorreoCambioContraventorOt, null, valores, ct).ConfigureAwait(false);
        var enviados = await correo.RegistrarAsync(new CorreoNuevo
        {
            TenantId = c.TenantId,
            ComparendoId = c.Id,
            Tipo = TipoComunicacion.CambioContraventorOt,
            Plantilla = "comparendos.cambio_contraventor_ot",
            PlantillaVersionId = correoOt.VersionId,
            Para = ot!.CorreosRadicacion.Select(x => new Destinatario(x, ot.Nombre)).ToList(),
            Cc = EmpresasEndpoints.CopiasPara(config.Cc, TipoComunicacion.CambioContraventorOt),
            Bcc = EmpresasEndpoints.CopiasPara(config.Bcc, TipoComunicacion.CambioContraventorOt),
            Asunto = correoOt.Asunto ?? $"Solicitud de cambio de contraventor — comparendo {c.Numero}",
            Html = correoOt.Html,
            Adjuntos = anexos,
            Canal = config.CanalCorreo,
            RemitenteNombre = config.RemitenteNombre ?? config.EmpresaNombre,
            CreadoPor = actor.UsuarioId,
        }, ct).ConfigureAwait(false);

        var intento = (short)((await db.SolicitudesCambio.Where(s => s.ComparendoId == c.Id).MaxAsync(s => (short?)s.NumeroIntento, ct).ConfigureAwait(false) ?? 0) + 1);
        var solicitud = new CambioContraventorSolicitud
        {
            Id = Guid.CreateVersion7(),
            TenantId = c.TenantId,
            ComparendoId = c.Id,
            NumeroIntento = intento,
            OrganismoTransitoId = ot.Id,
            RepresentanteLegalId = rl?.Id,
            ContraventorSnapshot = JsonSerializer.Serialize(new { c.Contraventor!.Nombres, tipoDocumento = c.Contraventor.TipoDocumento.ToString(), c.Contraventor.NumeroDocumento, c.Contraventor.Correo }, Json),
            FormatoArchivoId = formatoId,
            ComunicacionId = enviados[0].Id,
            SolicitadoPor = actor.UsuarioId ?? Guid.Empty,
            SolicitadoEn = reloj.Ahora,
        };
        db.SolicitudesCambio.Add(solicitud);
        c.EstadoCambioContraventor = EstadoCambioContraventor.Solicitado;
        timeline.Agregar(c, TipoEventoTimeline.CambioContraventorSolicitado,
            $"Solicitud de cambio de contraventor (intento {intento}) enviada a {ot.Nombre} ({string.Join(", ", ot.CorreosRadicacion)}).", actor,
            new { intento, ot = ot.Nombre, anexos = anexos.Select(a => a.Nombre) }, "cambio_contraventor_solicitud", solicitud.Id);

        // §6.10 pasos 3-4: el aviso 4 al conductor sale solo cuando la plataforma acepta el correo al OT (ReaccionCambioContraventor).
        return solicitud;
    }

    /// <summary>Aviso 4 al conductor con la carta en PDF, sin esperar respuesta [Excel C10]. Sin correo, queda la nota en el timeline.</summary>
    public async Task<Guid?> EnviarAviso4Async(Comparendo c, CambioContraventorSolicitud solicitud, Actor actor, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(c);
        ArgumentNullException.ThrowIfNull(solicitud);
        if (c.Contraventor?.Correo is not { Length: > 0 } destino)
        {
            timeline.Agregar(c, TipoEventoTimeline.NotaManual, "El aviso 4 no salió: el conductor no tiene correo.", Actor.Sistema);
            return null;
        }

        var config = await capacidades.DeAsync(c.TenantId, ct).ConfigureAwait(false);
        var valores = await contexto.DeAsync(c, null, ct).ConfigureAwait(false);
        var carta = ServicioPlantillas.Pdf(await plantillas.ArmarAsync(config.ConjuntoPlantillas, TipoPlantilla.CartaCambioContraventor, null, valores, ct).ConfigureAwait(false));
        var cuerpo = await plantillas.ArmarAsync(config.ConjuntoPlantillas, TipoPlantilla.CorreoAviso4, null, valores, ct).ConfigureAwait(false);
        var enviados = await correo.RegistrarAsync(new CorreoNuevo
        {
            TenantId = c.TenantId,
            ComparendoId = c.Id,
            Tipo = TipoComunicacion.Aviso4,
            Plantilla = "comparendos.aviso4",
            PlantillaVersionId = cuerpo.VersionId,
            Para = [new Destinatario(destino, c.Contraventor.Nombres)],
            Cc = EmpresasEndpoints.CopiasPara(config.Cc, TipoComunicacion.Aviso4),
            Bcc = EmpresasEndpoints.CopiasPara(config.Bcc, TipoComunicacion.Aviso4),
            Asunto = cuerpo.Asunto ?? $"Solicitamos el cambio de contraventor del comparendo {c.Numero}",
            Html = cuerpo.Html,
            Adjuntos = [new AdjuntoCorreo($"carta-cambio-contraventor-{c.Numero}.pdf", "application/pdf", carta)],
            Canal = config.CanalCorreo,
            RemitenteNombre = config.RemitenteNombre ?? config.EmpresaNombre,
            CreadoPor = actor.UsuarioId,
        }, ct).ConfigureAwait(false);

        db.AvisosConductor.Add(new AvisoConductor
        {
            Id = Guid.CreateVersion7(),
            TenantId = c.TenantId,
            ComparendoId = c.Id,
            TipoAviso = TipoAviso.Aviso4,
            Disparo = Disparo.Manual,
            VentanaAlEnvio = VentanaDescuento.SinDescuento,
            CorreoDestino = destino,
            ComunicacionId = enviados[0].Id,
            EnviadoPor = actor.UsuarioId,
            RegistradoEn = reloj.Ahora,
        });
        c.Gestionado = true;
        timeline.Agregar(c, TipoEventoTimeline.AvisoEnviado, $"Aviso 4 (carta de cambio de contraventor) enviado a {destino}.", actor,
            new { tipo = "Aviso4", destinatario = destino }, "comunicacion_saliente", enviados[0].Id);
        return enviados[0].Id;
    }

    public async Task RegistrarRespuestaAsync(Comparendo c, Guid solicitudId, bool aceptada, string? causal, DateOnly fecha, IFormFile? soporte, Actor actor, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(c);
        var s = await db.SolicitudesCambio.FirstOrDefaultAsync(x => x.Id == solicitudId && x.ComparendoId == c.Id, ct).ConfigureAwait(false)
            ?? throw ReglaException.NoEncontrado("La solicitud");
        if (s.Estado != EstadoSolicitudCambio.Solicitado)
            throw ReglaException.Conflicto("SOLICITUD_RESPONDIDA", "Esa solicitud ya tiene respuesta registrada.");
        if (!aceptada && (causal?.Trim().Length ?? 0) < 10)
            throw new ReglaException("CAUSAL_REQUERIDA", "Escribe la causal del rechazo (al menos 10 caracteres).");
        if (fecha > reloj.Hoy || fecha < Domain.Tiempo.ColombiaTime.DayOf(s.SolicitadoEn))
            throw new ReglaException("FECHA_INVALIDA", "La fecha de respuesta debe estar entre la solicitud y hoy.");

        if (soporte is not null)
            s.SoporteRespuestaArchivoId = await archivos.SubirAsync(c.TenantId, "respuesta_cambio_contraventor", soporte, RegistroArchivos.Documentos, ct, actor.UsuarioId).ConfigureAwait(false);
        (s.Estado, s.CausalRechazo, s.FechaRespuestaOt, s.RespondidoPor, s.RespondidoEn) =
            (aceptada ? EstadoSolicitudCambio.AceptadoOt : EstadoSolicitudCambio.RechazadoOt, aceptada ? null : causal!.Trim(), fecha, actor.UsuarioId, reloj.Ahora);
        c.EstadoCambioContraventor = aceptada ? EstadoCambioContraventor.AceptadoOt : EstadoCambioContraventor.RechazadoOt;
        timeline.Agregar(c, aceptada ? TipoEventoTimeline.CambioContraventorAceptado : TipoEventoTimeline.CambioContraventorRechazado,
            aceptada ? $"El organismo de tránsito aceptó el cambio de contraventor ({fecha:yyyy-MM-dd})." : $"El organismo de tránsito rechazó el cambio de contraventor: {causal!.Trim()}",
            actor, null, "cambio_contraventor_solicitud", s.Id);
    }
}

/// <summary>Si el correo de la solicitud al OT falla del todo, la solicitud queda «envío fallido» y el comparendo vuelve a su estado anterior.</summary>
internal sealed class ReaccionCambioContraventor(ComparendosDb db, Timeline timeline, ServicioCambioContraventor servicio) : IReaccionCorreo
{
    public async Task AlTerminarAsync(ComunicacionSaliente comunicacion, ResultadoEnvioCorreo resultado, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(comunicacion);
        if (comunicacion.Tipo != TipoComunicacion.CambioContraventorOt || comunicacion.Estado is EstadoComunicacion.Pendiente)
            return;
        var s = await db.SolicitudesCambio.FirstOrDefaultAsync(x => x.ComunicacionId == comunicacion.Id && x.Estado == EstadoSolicitudCambio.Solicitado, ct).ConfigureAwait(false);
        if (s is null)
            return;
        if (comunicacion.Estado is EstadoComunicacion.Enviado)
        {
            // La plataforma aceptó la solicitud al OT: ahora sí sale el aviso 4 al conductor (queda PENDIENTE para el despachador).
            if (s.Aviso4ComunicacionId is null)
            {
                var comparendo = await db.Comparendos.Include(x => x.Contraventor).FirstAsync(x => x.Id == s.ComparendoId, ct).ConfigureAwait(false);
                s.Aviso4ComunicacionId = await servicio.EnviarAviso4Async(comparendo, s, Actor.Sistema, ct).ConfigureAwait(false);
            }

            return;
        }

        var c = await db.Comparendos.FirstAsync(x => x.Id == s.ComparendoId, ct).ConfigureAwait(false);
        var anterior = await db.SolicitudesCambio.AnyAsync(x => x.ComparendoId == c.Id && x.Id != s.Id && x.Estado == EstadoSolicitudCambio.RechazadoOt, ct).ConfigureAwait(false);
        s.Estado = EstadoSolicitudCambio.EnvioFallido;
        c.EstadoCambioContraventor = anterior ? EstadoCambioContraventor.RechazadoOt : EstadoCambioContraventor.SinSolicitar;
        timeline.Agregar(c, TipoEventoTimeline.ComunicacionEstado, $"No se pudo enviar la solicitud de cambio de contraventor al OT: {resultado.Mensaje ?? resultado.Resultado}.", Actor.Sistema,
            relacionadoTipo: "comunicacion_saliente", relacionadoId: comunicacion.Id);
    }
}
