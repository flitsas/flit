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

namespace Flit.Comparendos.Api.Modulos.DerechosPeticion;

public sealed record Bloqueo(string Codigo, string Mensaje);

/// <summary>Lo que muestra la vista previa: bloqueos, advertencias que no bloquean, destinatarios, firmante y anexos.</summary>
public sealed record PreparacionDp(
    TipologiaDp Tipologia,
    IReadOnlyList<Bloqueo> Bloqueos,
    IReadOnlyList<string> Advertencias,
    OrganismoTransito? Organismo,
    RepresentanteLegal? Representante,
    IReadOnlyList<(string Nombre, Guid ArchivoId)> Anexos,
    ConfiguracionEmpresa Config,
    EstadoLegal EstadoLegal);

/// <summary>
/// Derechos de petición (HU #13488; ADO #12980; §6.7-6.9). Sin agente de IA [Excel C16]: se sugieren las tipologías
/// habilitadas que proceden en el estado legal actual (RN-147). Un DP por comparendo [Excel J4]; no sobre pagados
/// [Excel J13] ni inactivos. FLIT nunca es peticionario: firma el representante legal configurado [Excel P6, J7]. El
/// radicado se asigna al enviar (RN-140), consecutivo por prefijo y año (RN-141).
/// </summary>
public sealed class ServicioDp(
    ComparendosDb db,
    Capacidades capacidades,
    ResolutorRepresentante resolutor,
    ServicioPlantillas plantillas,
    ContextoPlantilla contexto,
    ServicioCorreo correo,
    RegistroArchivos archivos,
    CalendarioService calendario,
    Timeline timeline,
    Reloj reloj,
    IUsuariosEmpresa usuarios)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Tipologías habilitadas para la empresa, marcando las que proceden hoy (las procedentes primero).</summary>
    public async Task<IReadOnlyList<(TipologiaDp Tipologia, bool Procede)>> TipologiasAsync(Comparendo c, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(c);
        var estado = (await calendario.MotorAsync(ct).ConfigureAwait(false)).Evaluar(Plazos.Fechas(c), reloj.Hoy).Estado.ToString();
        var habilitadas = await (from t in db.Tipologias.AsNoTracking()
                                 join h in db.TipologiasEmpresa.AsNoTracking() on t.Id equals h.TipologiaId
                                 where h.TenantId == c.TenantId && h.Habilitada
                                 select t).ToListAsync(ct).ConfigureAwait(false);
        return habilitadas.Select(t => (t, t.EstadosLegalesPermitidos.Contains(estado))).OrderByDescending(x => x.Item2).ThenBy(x => x.t.Codigo).ToList();
    }

    /// <summary>Bloqueos de §6.8 en su orden, advertencias que no bloquean (S-54) y lo que se adjuntaría.</summary>
    public async Task<PreparacionDp> PrepararAsync(Comparendo c, Guid tipologiaId, bool masivo, bool reintento, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(c);
        var config = await capacidades.DeAsync(c.TenantId, ct).ConfigureAwait(false);
        var t = await db.Tipologias.AsNoTracking().FirstOrDefaultAsync(x => x.Id == tipologiaId, ct).ConfigureAwait(false) ?? throw ReglaException.NoEncontrado("La tipología");
        var habilitada = await db.TipologiasEmpresa.AnyAsync(h => h.TenantId == c.TenantId && h.TipologiaId == tipologiaId && h.Habilitada, ct).ConfigureAwait(false);
        var estado = (await calendario.MotorAsync(ct).ConfigureAwait(false)).Evaluar(Plazos.Fechas(c), reloj.Hoy).Estado;
        var ot = c.OrganismoTransitoId is { } otId ? await db.OrganismosTransito.AsNoTracking().FirstOrDefaultAsync(o => o.Id == otId, ct).ConfigureAwait(false) : null;
        var (propietarioEsDeLaEmpresa, rl) = await resolutor.DelPropietarioAsync(c, config.EmpresaNit, ParaFirmar.DerechoPeticion, ct).ConfigureAwait(false);
        var plantilla = await plantillas.PublicadaAsync(config.ConjuntoPlantillas, TipoPlantilla.DpDocumento, t.Id, ct).ConfigureAwait(false);

        var bloqueos = new List<Bloqueo>();
        void Si(bool condicion, string codigo, string mensaje)
        {
            if (condicion)
                bloqueos.Add(new Bloqueo(codigo, mensaje));
        }

        Si(!config.HasRightPetitionModule, "CAPACIDAD_DP_INACTIVA", "Tu empresa no tiene habilitados los derechos de petición.");
        Si(!c.Activo, "COMPARENDO_INACTIVO", "El comparendo está inactivo.");
        Si(c.Pagado, "COMPARENDO_PAGADO", "El comparendo está marcado como pagado: no admite derecho de petición.");
        Si(!reintento && await db.DerechosPeticion.AnyAsync(d => d.ComparendoId == c.Id, ct).ConfigureAwait(false), "YA_TIENE_DP", "El comparendo ya tiene un derecho de petición: solo admite uno.");
        // Epic #12977: desactivar una tesis del catálogo respeta a las empresas que ya la tenían; solo deja de ofrecerse.
        Si(!habilitada, "TIPOLOGIA_NO_HABILITADA", "La tipología no está habilitada para tu empresa.");
        Si(!t.EstadosLegalesPermitidos.Contains(estado.ToString()), "ESTADO_LEGAL_NO_PERMITIDO", $"«{t.Nombre}» no procede cuando el comparendo está {Formato.EstadoLegal(estado).ToLowerInvariant()}.");
        Si(masivo && !t.PermiteMasivo, "TIPOLOGIA_SIN_MASIVO", "La tipología no admite envío masivo.");
        Si(ot is null, "OT_SIN_MAPEAR", "El comparendo no tiene organismo de tránsito identificado.");
        Si(ot is { CorreosRadicacion.Count: 0 }, "OT_SIN_CORREO", "El organismo de tránsito no tiene correo de radicación.");
        // Hallazgo B9-1: el DP va a nombre del propietario y lo firma su representante; un propietario ajeno no se representa.
        Si(!propietarioEsDeLaEmpresa, "PROPIETARIO_AJENO", "El propietario del vehículo según la fuente no es la empresa ni uno de sus NITs vigilados.");
        Si(propietarioEsDeLaEmpresa && rl is null && config.RequiereRepresentanteLegal, "SIN_REPRESENTANTE_LEGAL", "La empresa exige representante legal y no hay uno activo del propietario (el del NIT, o el de la empresa si el propietario es la empresa) con permiso para firmar derechos de petición.");
        Si(plantilla is null, "SIN_PLANTILLA", "No hay una plantilla publicada para esta tipología.");
        // Hallazgo B9-2: el DP dice a qué correo responder; sin el de la empresa ni el del RL la respuesta no llega.
        Si(propietarioEsDeLaEmpresa && (rl is not null || !config.RequiereRepresentanteLegal) && string.IsNullOrWhiteSpace(config.CorreoRespuesta ?? rl?.Correo), "SIN_CORREO_RESPUESTA", "No hay un correo para recibir la respuesta del organismo: configura el correo de respuesta de la empresa o el del representante legal.");

        // Anexos del representante según la tipología; lo que falta solo advierte [Excel J7].
        var advertencias = new List<string>();
        var anexos = new List<(string, Guid)>();
        if (rl is not null)
        {
            foreach (var adjunto in t.AdjuntosRequeridos)
            {
                var (archivo, nombre) = adjunto switch
                {
                    "Cedula" => (rl.CedulaArchivoId, "cedula-representante"),
                    "CertificadoRepresentacion" => (rl.CertificadoArchivoId, "certificado-representacion"),
                    _ => (rl.EscrituraArchivoId, "escritura-poder"),
                };
                if (archivo is { } id)
                    anexos.Add((nombre, id));
                else
                    advertencias.Add($"Falta {nombre.Replace('-', ' ')} del representante: se envía sin ese anexo.");
            }

            if (rl.FechaExpedicionCertificado is { } f && f.AddDays(30) < reloj.Hoy)
                advertencias.Add("El certificado de representación tiene más de 30 días (no bloquea el envío).");
            if (rl.FirmaArchivoId is null)
                advertencias.Add("El representante no tiene imagen de firma cargada: el documento sale sin firma gráfica.");
        }

        return new PreparacionDp(t, bloqueos, advertencias, ot, rl, anexos, config, estado);
    }

    /// <summary>El PDF del DP: con el radicado (envío) o enmascarado <c>PREFIJO-XXXXXXXXXXXX-XXX</c> (vista previa).</summary>
    public async Task<(byte[] Pdf, Guid VersionId)> DocumentoAsync(Comparendo c, PreparacionDp p, string radicado, bool vistaPrevia, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(p);
        var firma = p.Representante?.FirmaArchivoId is { } f ? (await archivos.LeerAsync(f, ct).ConfigureAwait(false))?.Contenido : null;
        var valores = await contexto.DeAsync(c, new ExtrasContexto(Representante: p.Representante, FirmaPng: firma, Radicado: radicado, Tipologia: p.Tipologia), ct).ConfigureAwait(false);
        var armado = await plantillas.ArmarAsync(p.Config.ConjuntoPlantillas, TipoPlantilla.DpDocumento, p.Tipologia.Id, valores, ct).ConfigureAwait(false);
        return (ServicioPlantillas.Pdf(armado, vistaPrevia ? "VISTA PREVIA" : null), armado.VersionId);
    }

    public static string RadicadoEnmascarado(TipologiaDp t, DateOnly hoy)
    {
        ArgumentNullException.ThrowIfNull(t);
        return $"{t.PrefijoRadicado}-XXXXXXXXXXXX-XXX";
    }

    /// <summary>Formato de la Epic #12980: prefijo, fecha y hora de Colombia al segundo y milisegundos.</summary>
    public static string FormatoRadicado(string prefijo, DateTimeOffset momentoColombia) =>
        $"{prefijo}-{momentoColombia.ToString("yyMMddHHmmss", System.Globalization.CultureInfo.InvariantCulture)}-{momentoColombia.Millisecond:D3}";

    /// <summary>
    /// Radicado del envío (Epic #12980: <c>PREFIJO-AAMMDDHHMMSS-mmm</c>). Se reserva con un INSERT atómico; si el
    /// milisegundo ya está tomado (otro DP del lote u otra réplica), se corre al siguiente.
    /// </summary>
    public async Task<string> AsignarRadicadoAsync(TipologiaDp t, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(t);
        var momento = reloj.AhoraColombia;
        for (var intento = 0; intento < 1000; intento++, momento = momento.AddMilliseconds(1))
        {
            var candidato = FormatoRadicado(t.PrefijoRadicado, momento);
            var ahora = reloj.Ahora;
            var reservado = await db.Database.SqlQuery<string>($"""
                INSERT INTO comparendos.radicado_reservado (radicado, reservado_en) VALUES ({candidato}, {ahora})
                ON CONFLICT (radicado) DO NOTHING
                RETURNING radicado AS "Value"
                """).ToListAsync(ct).ConfigureAwait(false);
            if (reservado.Count == 1)
                return candidato;
        }

        throw new InvalidOperationException("No se pudo reservar un radicado único.");
    }

    /// <summary>
    /// Envía el DP (RN-142): radicado, PDF definitivo con la versión vigente, correo al OT con los anexos y el DP en
    /// ENVIANDO; pasa a ENVIADO cuando la plataforma acepta el correo (<see cref="ReaccionDp"/>). Sin representante
    /// cuando se exige: no se envía, queda en el timeline y se alerta a la empresa (RN-137).
    /// </summary>
    public async Task<DerechoPeticion> EnviarAsync(Comparendo c, Guid tipologiaId, Actor actor, Guid? loteId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(c);
        ArgumentNullException.ThrowIfNull(actor);
        var p = await PrepararAsync(c, tipologiaId, loteId is not null, reintento: false, ct).ConfigureAwait(false);
        if (p.Bloqueos.Count > 0)
        {
            if (p.Bloqueos.Any(b => b.Codigo == "SIN_REPRESENTANTE_LEGAL"))
                await BloqueoSinRepresentanteAsync(c, actor, ct).ConfigureAwait(false);
            throw new ReglaException(p.Bloqueos[0].Codigo, p.Bloqueos[0].Mensaje);
        }

        var radicado = await AsignarRadicadoAsync(p.Tipologia, ct).ConfigureAwait(false);
        var (pdf, versionId) = await DocumentoAsync(c, p, radicado, vistaPrevia: false, ct).ConfigureAwait(false);
        var documentoId = await archivos.GuardarAsync(c.TenantId, "derecho_peticion", $"{radicado}.pdf", "application/pdf", pdf, ct, actor.UsuarioId).ConfigureAwait(false);
        var dp = new DerechoPeticion
        {
            Id = Guid.CreateVersion7(),
            TenantId = c.TenantId,
            ComparendoId = c.Id,
            TipologiaId = p.Tipologia.Id,
            PlantillaVersionId = versionId,
            Radicado = radicado,
            OrganismoTransitoId = p.Organismo!.Id,
            CorreosDestino = [.. p.Organismo.CorreosRadicacion],
            RepresentanteLegalId = p.Representante?.Id,
            PeticionarioSnapshot = JsonSerializer.Serialize(new
            {
                peticionario = c.NombreInfractor,
                documento = c.NumeroDocumentoInfractor,
                representante = p.Representante?.Nombres,
                cargo = p.Representante?.Cargo,
            }, Json),
            EstadoLegalAlRadicar = p.EstadoLegal.ToString(),
            ValorAlRadicar = c.ValorTotal,
            DocumentoArchivoId = documentoId,
            LoteId = loteId,
            EnviadoPor = actor.UsuarioId ?? Guid.Empty,
            CreadoEn = reloj.Ahora,
            CreadoPor = actor.UsuarioId,
        };
        db.DerechosPeticion.Add(dp);
        dp.ComunicacionIds = await CorreoAlOtAsync(c, dp, p, pdf, actor, ct).ConfigureAwait(false);
        dp.ComunicacionId = dp.ComunicacionIds[0];
        c.TieneDp = true;
        c.Gestionado = true;
        return dp;
    }

    /// <summary>Reintenta un DP con envío fallido: mismo radicado y PDF; vuelve a validar los bloqueos salvo «ya tiene DP» (S-30).</summary>
    public async Task ReintentarAsync(Comparendo c, DerechoPeticion dp, Actor actor, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(dp);
        if (dp.Estado != EstadoDp.EnvioFallido)
            throw ReglaException.Conflicto("DP_NO_REINTENTABLE", "Solo se reintenta un derecho de petición cuyo envío falló.");
        var p = await PrepararAsync(c, dp.TipologiaId, masivo: false, reintento: true, ct).ConfigureAwait(false);
        if (p.Bloqueos.Count > 0)
            throw new ReglaException(p.Bloqueos[0].Codigo, p.Bloqueos[0].Mensaje);
        var pdf = (await archivos.LeerAsync(dp.DocumentoArchivoId, ct).ConfigureAwait(false))?.Contenido ?? throw ReglaException.NoEncontrado("El documento del DP");
        dp.Estado = EstadoDp.Enviando;
        dp.ComunicacionIds = await CorreoAlOtAsync(c, dp, p, pdf, actor, ct).ConfigureAwait(false);
        dp.ComunicacionId = dp.ComunicacionIds[0];
    }

    private async Task<List<Guid>> CorreoAlOtAsync(Comparendo c, DerechoPeticion dp, PreparacionDp p, byte[] pdf, Actor actor, CancellationToken ct)
    {
        var valores = await contexto.DeAsync(c, new ExtrasContexto(Representante: p.Representante, Radicado: dp.Radicado, Tipologia: p.Tipologia), ct).ConfigureAwait(false);
        var cuerpo = await plantillas.ArmarAsync(p.Config.ConjuntoPlantillas, TipoPlantilla.CorreoDpOt, null, valores, ct).ConfigureAwait(false);
        var adjuntos = new List<AdjuntoCorreo> { new($"{dp.Radicado}.pdf", "application/pdf", pdf, dp.DocumentoArchivoId) };
        foreach (var (nombre, archivoId) in p.Anexos)
        {
            if (await archivos.LeerAsync(archivoId, ct).ConfigureAwait(false) is { } leido)
                adjuntos.Add(new AdjuntoCorreo($"{nombre}{Path.GetExtension(leido.Archivo.NombreOriginal)}", leido.Archivo.ContentType, leido.Contenido, archivoId));
        }

        var enviados = await correo.RegistrarAsync(new CorreoNuevo
        {
            TenantId = c.TenantId,
            ComparendoId = c.Id,
            Tipo = TipoComunicacion.DpOt,
            Plantilla = "comparendos.dp_ot",
            PlantillaVersionId = cuerpo.VersionId,
            Para = dp.CorreosDestino.Select(x => new Destinatario(x, p.Organismo?.Nombre)).ToList(),
            Cc = EmpresasEndpoints.CopiasPara(p.Config.Cc, TipoComunicacion.DpOt),
            Bcc = EmpresasEndpoints.CopiasPara(p.Config.Bcc, TipoComunicacion.DpOt),
            Asunto = cuerpo.Asunto ?? $"Derecho de petición {dp.Radicado}",
            Html = cuerpo.Html,
            Adjuntos = adjuntos,
            Canal = p.Config.CanalCorreo,
            RemitenteNombre = p.Config.RemitenteNombre ?? p.Config.EmpresaNombre,
            CreadoPor = actor.UsuarioId,
        }, ct).ConfigureAwait(false);
        return [.. enviados.Select(e => e.Id)];
    }

    /// <summary>
    /// Timeline DP_BLOQUEADO_SIN_RL y alerta a la empresa, como máximo una por día (S-12): a sus correos de alerta
    /// operativa o, si no configuró ninguno, a los usuarios que pueden gestionar representantes (Identidad).
    /// </summary>
    private async Task BloqueoSinRepresentanteAsync(Comparendo c, Actor actor, CancellationToken ct)
    {
        timeline.Agregar(c, TipoEventoTimeline.DpBloqueadoSinRl, "No se envió el derecho de petición: la empresa exige representante legal y no hay uno con permiso para firmar.", actor);
        var config = await capacidades.DeAsync(c.TenantId, ct).ConfigureAwait(false);
        var inicio = new DateTimeOffset(reloj.Hoy.ToDateTime(TimeOnly.MinValue), Domain.Tiempo.ColombiaTime.Offset).ToUniversalTime();
        if (await db.Comunicaciones.AnyAsync(x => x.TenantId == c.TenantId && x.Tipo == TipoComunicacion.AlertaSinRepresentante && x.CreadoEn >= inicio, ct).ConfigureAwait(false))
        {
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            return;
        }

        var para = config.CorreosAlertaOperativa.Count > 0
            ? config.CorreosAlertaOperativa.Select(x => new Destinatario(x)).ToList()
            : (await usuarios.ConPermisoAsync(c.TenantId, Permisos.RepresentantesGestionar, ct).ConfigureAwait(false)).Select(u => new Destinatario(u.Correo, u.Nombre)).ToList();
        if (para.Count == 0)
        {
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            return;
        }

        var valores = await contexto.DeAsync(c, null, ct).ConfigureAwait(false);
        var armado = await plantillas.ArmarAsync(config.ConjuntoPlantillas, TipoPlantilla.CorreoAlertaSinRl, null, valores, ct).ConfigureAwait(false);
        await correo.RegistrarAsync(new CorreoNuevo
        {
            TenantId = c.TenantId,
            ComparendoId = c.Id,
            Tipo = TipoComunicacion.AlertaSinRepresentante,
            Plantilla = "comparendos.alerta_sin_representante",
            PlantillaVersionId = armado.VersionId,
            Para = para,
            Asunto = armado.Asunto ?? "Falta el representante legal",
            Html = armado.Html,
            Canal = CanalCorreo.FlitSmtp,
        }, ct).ConfigureAwait(false);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Respuesta del OT (HU #13490): radicado del OT, fecha, sentido y PDF obligatorios (S-24); una vez registrada no se
    /// edita ni se anula [Excel J10].
    /// </summary>
    public async Task RegistrarRespuestaAsync(Comparendo c, DerechoPeticion dp, string radicadoOt, DateOnly fecha, SentidoRespuestaOt sentido, IFormFile? pdf, Actor actor, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(c);
        ArgumentNullException.ThrowIfNull(dp);
        if (dp.RespuestaSentido is not null || dp.Estado == EstadoDp.Respondido)
            throw ReglaException.Conflicto("RESPUESTA_REGISTRADA", "La respuesta del organismo ya está registrada y no se puede modificar.");
        if (dp.Estado != EstadoDp.Enviado)
            throw ReglaException.Conflicto("DP_NO_ENVIADO", "El derecho de petición todavía no fue enviado al organismo.");
        if (string.IsNullOrWhiteSpace(radicadoOt) || radicadoOt.Trim().Length > 60)
            throw new ReglaException("RADICADO_OT_REQUERIDO", "Escribe el radicado de la respuesta del organismo (hasta 60 caracteres).");
        if (fecha > reloj.Hoy || (dp.FechaRadicacion is { } r && fecha < r))
            throw new ReglaException("FECHA_INVALIDA", "La fecha de respuesta debe estar entre la radicación y hoy.");
        if (pdf is null)
            throw new ReglaException("PDF_REQUERIDO", "Adjunta el PDF de la respuesta del organismo.");

        dp.RespuestaArchivoId = await archivos.SubirAsync(c.TenantId, "respuesta_ot", pdf, ["application/pdf"], ct, actor.UsuarioId).ConfigureAwait(false);
        (dp.Estado, dp.RespuestaSentido, dp.RespuestaOrigen, dp.RespuestaRadicadoOt, dp.RespuestaFecha) = (EstadoDp.Respondido, sentido, OrigenRespuestaOt.Operador, radicadoOt.Trim(), fecha);
        (dp.RespuestaRegistradaPor, dp.RespuestaRegistradaEn) = (actor.UsuarioId, reloj.Ahora);
        c.EstadoGestion = EstadoGestion.RespuestaOt;
        timeline.Agregar(c, TipoEventoTimeline.DpRespuestaRegistrada,
            $"Respuesta del organismo al DP {dp.Radicado}: {(sentido == SentidoRespuestaOt.Favorable ? "FAVORABLE" : "DESFAVORABLE")} (radicado {radicadoOt.Trim()}, {fecha:yyyy-MM-dd}).",
            actor, null, "derecho_peticion", dp.Id);
    }
}

/// <summary>El DP pasa a ENVIADO cuando la plataforma acepta su correo, o a ENVÍO FALLIDO si falla del todo (§4.4).</summary>
internal sealed class ReaccionDp(ComparendosDb db, Timeline timeline, CalendarioService calendario, Reloj reloj) : IReaccionCorreo
{
    public async Task AlTerminarAsync(ComunicacionSaliente comunicacion, ResultadoEnvioCorreo resultado, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(comunicacion);
        if (comunicacion.Tipo != TipoComunicacion.DpOt)
            return;
        var dp = await db.DerechosPeticion.FirstOrDefaultAsync(d => (d.ComunicacionIds.Contains(comunicacion.Id) || d.ComunicacionId == comunicacion.Id) && d.Estado == EstadoDp.Enviando, ct).ConfigureAwait(false);
        if (dp is null)
            return;

        // Un OT con varios correos: se decide cuando todos terminaron. Enviado si al menos un buzón del OT lo recibió
        // (el radicado llegó); envío fallido solo si fallaron todos, así el reintento nunca duplica a quien ya lo tiene.
        var otras = dp.ComunicacionIds.Where(x => x != comunicacion.Id).ToList();
        var estados = await db.Comunicaciones.AsNoTracking().Where(x => otras.Contains(x.Id)).Select(x => new { x.Destinatarios, x.Estado }).ToListAsync(ct).ConfigureAwait(false);
        if (estados.Any(x => x.Estado == EstadoComunicacion.Pendiente))
            return;
        var algunaEnviada = comunicacion.Estado is EstadoComunicacion.Enviado or EstadoComunicacion.Entregado or EstadoComunicacion.Abierto
            || estados.Any(x => x.Estado is EstadoComunicacion.Enviado or EstadoComunicacion.Entregado or EstadoComunicacion.Abierto);
        var fallidas = estados.Count(x => x.Estado is EstadoComunicacion.Fallido or EstadoComunicacion.Rebotado)
            + (comunicacion.Estado is EstadoComunicacion.Fallido or EstadoComunicacion.Rebotado ? 1 : 0);
        var c = await db.Comparendos.FirstAsync(x => x.Id == dp.ComparendoId, ct).ConfigureAwait(false);
        if (algunaEnviada)
        {
            var plazo = await db.Tipologias.Where(t => t.Id == dp.TipologiaId).Select(t => t.PlazoRespuestaOtDiasHabiles).FirstAsync(ct).ConfigureAwait(false);
            dp.Estado = EstadoDp.Enviado;
            dp.FechaRadicacion = reloj.Hoy;
            dp.FechaLimiteRespuestaOt = (await calendario.ActualAsync(ct).ConfigureAwait(false)).SumarHabiles(reloj.Hoy, plazo);
            if (c.EstadoGestion == EstadoGestion.SinGestion)
                c.EstadoGestion = EstadoGestion.DpEnviado;
            timeline.Agregar(c, TipoEventoTimeline.DpEnviado,
                $"Derecho de petición {dp.Radicado} enviado a {string.Join(", ", dp.CorreosDestino)}.{(fallidas > 0 ? $" {fallidas} de {dp.ComunicacionIds.Count} buzones del organismo no lo recibieron (ver el Centro de Mensajes)." : string.Empty)}",
                Actor.Sistema, new { dp.Radicado, destinatarios = dp.CorreosDestino }, "derecho_peticion", dp.Id);
        }
        else
        {
            dp.Estado = EstadoDp.EnvioFallido;
            timeline.Agregar(c, TipoEventoTimeline.DpEnvioFallido, $"No se pudo enviar el derecho de petición {dp.Radicado}: {resultado.Mensaje ?? resultado.Resultado}. Se puede reintentar con el mismo radicado.",
                Actor.Sistema, null, "derecho_peticion", dp.Id);
        }

        await ReflejarEnLoteAsync(dp, resultado, ct).ConfigureAwait(false);
    }

    /// <summary>§6.17: el ítem del lote sigue el resultado real del correo al OT (también si un reintento lo arregla después).</summary>
    private async Task ReflejarEnLoteAsync(DerechoPeticion dp, ResultadoEnvioCorreo resultado, CancellationToken ct)
    {
        if (dp.LoteId is not { } loteId)
            return;
        var item = await db.LoteDpItems.FirstOrDefaultAsync(i => i.LoteId == loteId && i.DerechoPeticionId == dp.Id, ct).ConfigureAwait(false);
        var lote = await db.LotesDp.FirstOrDefaultAsync(l => l.Id == loteId, ct).ConfigureAwait(false);
        if (item is null || lote is null)
            return;
        if (dp.Estado == EstadoDp.EnvioFallido && item.Estado == EstadoItemLote.Enviado)
        {
            (item.Estado, item.Motivo) = (EstadoItemLote.Fallido, $"El correo al OT falló: {resultado.Mensaje ?? resultado.Resultado}");
            (lote.Enviados, lote.Fallidos) = (lote.Enviados - 1, lote.Fallidos + 1);
        }
        else if (dp.Estado == EstadoDp.Enviado && item.Estado == EstadoItemLote.Fallido)
        {
            (item.Estado, item.Motivo) = (EstadoItemLote.Enviado, null);
            (lote.Enviados, lote.Fallidos) = (lote.Enviados + 1, lote.Fallidos - 1);
        }

        if (lote.Estado != EstadoLoteDp.EnCurso)
            lote.Estado = lote.Fallidos > 0 ? EstadoLoteDp.CompletadoConErrores : EstadoLoteDp.Completado;
    }
}
