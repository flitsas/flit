using Flit.Comparendos.Api.Modulos.Comparendos;
using Flit.Comparendos.Api.Modulos.Comun;
using Flit.Comparendos.Api.Modulos.Correo;
using Flit.Comparendos.Api.Modulos.Configuracion;
using Flit.Comparendos.Api.Modulos.Plantillas;
using Flit.Comparendos.Api.Modulos.Portal;
using Flit.Comparendos.Api.Persistence;
using Flit.Comparendos.Api.Persistence.Modelo;
using Flit.Comparendos.Api.Plataforma;
using Flit.Comparendos.Domain.Modelo;
using Microsoft.EntityFrameworkCore;

namespace Flit.Comparendos.Api.Modulos.Avisos;

/// <summary>Si el correo con el documento sale y, si no, por qué (texto de negocio, sin tecnología).</summary>
public sealed record DecisionEvidencia(bool SeEnviara, string? Motivo)
{
    public static DecisionEvidencia Si { get; } = new(true, null);

    public static DecisionEvidencia No(string motivo) => new(false, motivo);
}

/// <summary>Cómo terminó el intento de enviar la evidencia: lo que devuelve «asociar».</summary>
public sealed record ResultadoEvidencia(string Estado, string? Motivo)
{
    public const string Enviado = "Enviado";
    public const string Pendiente = "Pendiente";
    public const string Omitido = "Omitido";
}

/// <summary>
/// GDC-EVID: la única regla que decide si, al asociar un documento escaneado a un comparendo, sale de inmediato un correo al
/// conductor con ese documento como evidencia. La usan el envío y el GET de la pantalla, para que lo que se anuncia antes
/// de asociar sea lo que pasa. No aplican la secuencia de avisos, la ventana de descuento ni el límite diario: es un
/// correo por evento, no un aviso.
/// </summary>
public static class EvidenciaPorCorreo
{
    /// <summary>
    /// Lo más que cabe de adjunto. core-notificaciones recibe el correo por gRPC sin <c>MaxReceiveMessageSize</c> propio
    /// (4 MiB por defecto) y el mensaje lleva además el HTML: se deja un margen de 1 MiB para el cuerpo y las cabeceras.
    /// </summary>
    public const long TopeAdjuntoBytes = 3L * 1024 * 1024;

    public static DecisionEvidencia Evaluar(Comparendo c, ConfiguracionEmpresa config, bool productoHabilitado, long? tamanoDocumentoBytes)
    {
        ArgumentNullException.ThrowIfNull(c);
        ArgumentNullException.ThrowIfNull(config);
        if (!c.Activo)
            return DecisionEvidencia.No("El comparendo está inactivo");
        if (c.Pagado)
            return DecisionEvidencia.No("El comparendo ya está pagado");
        if (c.Contraventor is null)
            return DecisionEvidencia.No("No hay un conductor asignado");
        if (string.IsNullOrWhiteSpace(c.Contraventor.Correo))
            return DecisionEvidencia.No("El conductor no tiene correo registrado");
        if (config.NotificationChannel != CanalComunicacion.Email)
            return DecisionEvidencia.No("La empresa no usa correo para notificar");
        if (!productoHabilitado)
            return DecisionEvidencia.No("La empresa no tiene habilitado el producto de Comparendos");
        if (tamanoDocumentoBytes is not { } tamano || tamano <= 0)
            return DecisionEvidencia.No("El comparendo no tiene un documento para enviar");
        if (tamano > TopeAdjuntoBytes)
            return DecisionEvidencia.No("El documento pesa demasiado para enviarlo por correo");
        return DecisionEvidencia.Si;
    }
}

/// <summary>
/// GDC-EVID: arma y registra el correo con el documento. Se llama DESPUÉS de confirmar la asociación: el comparendo en
/// memoria del que asocia quedó viejo (el archivo se escribe con ExecuteUpdate y no trae el conductor), así que aquí se
/// relee. Registrar no envía: quien llama confirma con SaveChanges y despacha.
/// </summary>
public sealed partial class ServicioEvidencia(
    ComparendosDb db,
    Capacidades capacidades,
    ServicioPlantillas plantillas,
    ContextoPlantilla contexto,
    ServicioPortal portal,
    ServicioCorreo correo,
    Timeline timeline,
    IProductosHabilitados productos,
    DespachadorCorreos despachador,
    ILogger<ServicioEvidencia> logger)
{
    /// <summary>Lo que dirá la pantalla antes de asociar: con el documento que se va a asociar (el nuevo).</summary>
    public async Task<DecisionEvidencia> PrevistaAsync(Guid comparendoId, Guid tenantId, Guid? archivoId, CancellationToken ct)
    {
        var c = await db.Comparendos.AsNoTracking().Include(x => x.Contraventor)
            .FirstOrDefaultAsync(x => x.Id == comparendoId && x.TenantId == tenantId, ct).ConfigureAwait(false);
        if (c is null)
            return DecisionEvidencia.No("El comparendo ya no existe");
        var config = await capacidades.DeAsync(tenantId, ct).ConfigureAwait(false);
        return EvidenciaPorCorreo.Evaluar(c, config, await productos.ComparendosHabilitadoAsync(tenantId, ct).ConfigureAwait(false), await TamanoAsync(archivoId, tenantId, ct).ConfigureAwait(false));
    }

    /// <summary>
    /// GDC-EVID §9: lo que dirá el modal de «Asociar los N» para cada documento del lote (misma regla que el envío), con la
    /// identidad del conductor para contar conductores DISTINTOS: tipo y número de documento; si falta, el correo normalizado.
    /// Nunca se devuelve al cliente (solo se cuenta). Todo de la empresa <paramref name="tenantId"/>.
    /// </summary>
    public async Task<IReadOnlyList<(DecisionEvidencia Decision, string? Conductor)>> PrevistasAsync(Guid tenantId, IReadOnlyList<(Guid ComparendoId, Guid ArchivoId)> lote, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(lote);
        if (lote.Count == 0)
            return [];
        var ids = lote.Select(x => x.ComparendoId).Distinct().ToList();
        var archivosIds = lote.Select(x => x.ArchivoId).Distinct().ToList();
        var comparendos = await db.Comparendos.AsNoTracking().Include(x => x.Contraventor).Where(x => x.TenantId == tenantId && ids.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, ct).ConfigureAwait(false);
        var tamanos = await db.Archivos.AsNoTracking().Where(a => a.TenantId == tenantId && archivosIds.Contains(a.Id))
            .ToDictionaryAsync(a => a.Id, a => a.TamanoBytes, ct).ConfigureAwait(false);
        var config = await capacidades.DeAsync(tenantId, ct).ConfigureAwait(false);
        var producto = await productos.ComparendosHabilitadoAsync(tenantId, ct).ConfigureAwait(false);
        return lote.Select(x => comparendos.TryGetValue(x.ComparendoId, out var c)
            ? (EvidenciaPorCorreo.Evaluar(c, config, producto, tamanos.TryGetValue(x.ArchivoId, out var t) ? t : null), Identidad(c.Contraventor))
            : (DecisionEvidencia.No("El comparendo ya no existe"), (string?)null)).ToList();
    }

    private static string? Identidad(Persistence.Modelo.Contraventor? k) =>
        k is null ? null
        : !string.IsNullOrWhiteSpace(k.NumeroDocumento) ? $"{k.TipoDocumento}:{k.NumeroDocumento.Trim().ToUpperInvariant()}"
        : k.Correo?.Trim().ToLowerInvariant();

    /// <summary>
    /// Registra el correo con <paramref name="archivoId"/> (el documento recién asociado) y su evento en la línea de
    /// tiempo. Devuelve la comunicación a despachar, o el motivo por el que no sale. No confirma: SaveChanges es de quien llama.
    /// </summary>
    public async Task<(Guid? ComunicacionId, DecisionEvidencia Decision)> RegistrarAsync(Guid comparendoId, Guid tenantId, Guid archivoId, bool reemplazo, Actor actor, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(actor);
        // El comparendo que dejó AsociarAsync en el contexto está viejo: se suelta y se relee, ya con el conductor.
        if (db.Comparendos.Local.FirstOrDefault(x => x.Id == comparendoId) is { } viejo)
            db.Entry(viejo).State = EntityState.Detached;
        var c = await db.Comparendos.Include(x => x.Contraventor).FirstOrDefaultAsync(x => x.Id == comparendoId && x.TenantId == tenantId, ct).ConfigureAwait(false);
        if (c is null)
            return (null, DecisionEvidencia.No("El comparendo ya no existe"));

        var config = await capacidades.DeAsync(tenantId, ct).ConfigureAwait(false);
        var archivo = await db.Archivos.AsNoTracking().FirstOrDefaultAsync(a => a.Id == archivoId && a.TenantId == tenantId, ct).ConfigureAwait(false);
        var decision = EvidenciaPorCorreo.Evaluar(c, config, await productos.ComparendosHabilitadoAsync(tenantId, ct).ConfigureAwait(false), archivo?.TamanoBytes);
        if (!decision.SeEnviara || archivo is null)
        {
            // Sin archivo, Evaluar ya dijo que no (no hay tamaño); el respaldo evita una nota sin motivo.
            decision = decision.Motivo is null ? DecisionEvidencia.No("El comparendo no tiene un documento para enviar") : decision;
            NotaOmitida(c, decision.Motivo!, reemplazo, actor);
            return (null, decision);
        }

        var enlace = portal.Emitir(c);
        var valores = await contexto.DeAsync(c, new ExtrasContexto(PortalEnlace: enlace.Url, PortalVence: enlace.Vence), ct).ConfigureAwait(false);
        valores["evidencia.reemplazo"] = reemplazo ? "sí" : null;
        var armado = await plantillas.ArmarAsync(config.ConjuntoPlantillas, TipoPlantilla.CorreoEvidencia, null, valores, ct).ConfigureAwait(false);
        var tipo = TipoComunicacion.Evidencia;
        var nombre = $"comparendo-{c.Numero}{Path.GetExtension(archivo.NombreOriginal)}";
        var enviados = await correo.RegistrarAsync(new CorreoNuevo
        {
            TenantId = c.TenantId,
            ComparendoId = c.Id,
            Tipo = tipo,
            Plantilla = $"comparendos.{tipo.ToString().ToLowerInvariant()}",
            PlantillaVersionId = armado.VersionId,
            Para = [new Destinatario(c.Contraventor!.Correo!, c.Contraventor.Nombres)],
            Cc = EmpresasEndpoints.CopiasPara(config.Cc, tipo),
            Bcc = EmpresasEndpoints.CopiasPara(config.Bcc, tipo),
            Asunto = armado.Asunto ?? "Comparendo",
            Html = armado.Html,
            Adjuntos = [new AdjuntoCorreo(nombre, archivo.ContentType, [], archivo.Id)],
            Canal = config.CanalCorreo,
            RemitenteNombre = config.RemitenteNombre ?? config.EmpresaNombre,
            CreadoPor = actor.UsuarioId,
        }, ct).ConfigureAwait(false);

        timeline.Agregar(c, TipoEventoTimeline.EvidenciaEnviada,
            // «Registrado para envío»: se escribe al armar el correo, antes del despacho; el estado real de entrega es el del Centro de mensajes.
            $"Correo con el {(reemplazo ? "documento nuevo" : "documento")} del comparendo registrado para envío a {c.Contraventor.Correo}.",
            actor, new { destinatario = c.Contraventor.Correo, adjunto = nombre, reemplazo }, "comunicacion_saliente", enviados[0].Id);
        return (enviados[0].Id, DecisionEvidencia.Si);
    }

    /// <summary>Lo más que puede tardar el trabajo posterior a la asociación (registrar, nota y despacho).</summary>
    private static readonly TimeSpan TopeTrabajoPosterior = TimeSpan.FromSeconds(20);

    /// <summary>Plazo propio de la nota de «no se envió»: corto e independiente del plazo del correo.</summary>
    private static readonly TimeSpan TopeNota = TimeSpan.FromSeconds(10);

    /// <summary>El plazo en uso; las pruebas lo acortan para ejercer el vencimiento.</summary>
    internal TimeSpan Plazo { get; set; } = TopeTrabajoPosterior;

    /// <summary>
    /// Lo que hace «asociar» después de confirmar la asociación: registra, confirma y despacha el correo. Nada de aquí lanza:
    /// un fallo del correo no deshace la asociación y se dice en el resultado. La asociación ya está confirmada: si el
    /// operador cierra la pestaña o la petición vence ahora, el correo (o su nota) debe salir igual, porque al reintentar el
    /// documento ya figura asociado y no se reenvía. Por eso este trabajo NO usa el token de la petición: corre con uno propio
    /// de <see cref="TopeTrabajoPosterior"/>.
    /// <para>
    /// Nada de aquí lanza, ni siquiera una <see cref="OperationCanceledException"/> o un <c>TaskCanceledException</c>: como el token es
    /// el propio y no el de la petición, una cancelación puede venir del plazo propio o de un tiempo de espera interno del
    /// transporte con el token aún vivo; en ambos casos la asociación ya está confirmada y la respuesta no puede ser un 500.
    /// Por eso los <c>catch</c> capturan todo <see cref="Exception"/> y degradan: «No se pudo preparar…» (Omitido, con nota)
    /// si falla el registro, o Pendiente si falla el despacho (el correo ya está registrado y el ciclo lo reintenta).
    /// </para>
    /// <para>
    /// OBSERVACIÓN (conocida, no corregida): si el plazo vence después de que el transporte aceptó el correo y antes del
    /// <c>SaveChanges</c> que lo marca Enviado, el ciclo de fondo del despachador lo vería Pendiente y podría reenviarlo. Ya
    /// ocurría con el token de la petición; ahora lo dispara un temporizador fijo.
    /// </para>
    /// </summary>
    /// <param name="despachar">
    /// false en «Asociar los N» (§9): el correo queda registrado y en cola (Pendiente) y lo envía el ciclo de fondo del
    /// despachador; 50 envíos en serie dentro de la petición pasarían el tiempo de espera del proxy.
    /// </param>
    public async Task<ResultadoEvidencia> EnviarAlAsociarAsync(Guid comparendoId, Guid tenantId, Guid archivoId, bool reemplazo, Actor actor, CancellationToken peticion,
        bool despachar = true)
    {
        if (peticion.IsCancellationRequested)
            LogPeticionCancelada(logger, comparendoId, tenantId);
        using var propio = new CancellationTokenSource(Plazo);
        var ct = propio.Token;
        Guid? comunicacion;
        try
        {
            DecisionEvidencia decision;
            (comunicacion, decision) = await RegistrarAsync(comparendoId, tenantId, archivoId, reemplazo, actor, ct).ConfigureAwait(false);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            if (comunicacion is null)
                return new ResultadoEvidencia(ResultadoEvidencia.Omitido, decision.Motivo);
        }
        catch (Exception ex)
        {
            // Sin datos personales en el log: solo ids y el tipo de error.
            LogNoPreparado(logger, ex.GetType().Name, comparendoId, tenantId);
            db.ChangeTracker.Clear();
            const string motivoFallo = "No se pudo preparar el correo al conductor; el documento quedó asociado";
            await DejarNotaAsync(comparendoId, tenantId, motivoFallo, reemplazo, actor).ConfigureAwait(false);
            return new ResultadoEvidencia(ResultadoEvidencia.Omitido, motivoFallo);
        }

        if (!despachar)
            return new ResultadoEvidencia(ResultadoEvidencia.Pendiente, null);

        try
        {
            var estado = await despachador.DespacharAsync(comunicacion.Value, ct).ConfigureAwait(false);
            return estado switch
            {
                EstadoComunicacion.Enviado or EstadoComunicacion.Entregado or EstadoComunicacion.Abierto => new ResultadoEvidencia(ResultadoEvidencia.Enviado, null),
                EstadoComunicacion.Pendiente => new ResultadoEvidencia(ResultadoEvidencia.Pendiente, null),
                _ => new ResultadoEvidencia(ResultadoEvidencia.Omitido, "El correo no llegó al conductor; revisa el Centro de mensajes"),
            };
        }
        catch (Exception ex)
        {
            LogDespachoPendiente(logger, ex.GetType().Name, comunicacion.Value);
            return new ResultadoEvidencia(ResultadoEvidencia.Pendiente, null);
        }
    }

    /// <summary>
    /// La fuente persistente del motivo: el aviso de la pantalla se cierra solo, la línea de tiempo no. Sin comunicación ni
    /// cambio de estado: es solo una nota del comparendo.
    /// </summary>
    private void NotaOmitida(Comparendo c, string motivo, bool reemplazo, Actor actor) =>
        timeline.Agregar(c, TipoEventoTimeline.EvidenciaOmitida, $"Documento asociado: no se envió correo al conductor. Motivo: {motivo}.", actor, new { motivo, reemplazo });

    /// <summary>GDC-EVID §10: la misma nota «no se envió correo» para el trabajador del lote (con su propio plazo; nunca lanza).</summary>
    public Task DejarNotaOmitidaAsync(Guid comparendoId, Guid tenantId, string motivo, bool reemplazo, Actor actor) =>
        DejarNotaAsync(comparendoId, tenantId, motivo, reemplazo, actor);

    /// <summary>
    /// Tras un fallo (contexto limpio): relee el comparendo y deja la nota; si tampoco se puede, solo se loguea. Usa su propio
    /// token corto e independiente: el plazo del trabajo ya pudo vencer (justo el caso en que la nota más hace falta, porque
    /// el operador ya vio el aviso de la pantalla y no quedaría rastro persistente) y el de la petición pudo cancelarse.
    /// </summary>
    private async Task DejarNotaAsync(Guid comparendoId, Guid tenantId, string motivo, bool reemplazo, Actor actor)
    {
        using var propio = new CancellationTokenSource(TopeNota);
        var ct = propio.Token;
        try
        {
            if (await db.Comparendos.FirstOrDefaultAsync(x => x.Id == comparendoId && x.TenantId == tenantId, ct).ConfigureAwait(false) is not { } c)
                return;
            NotaOmitida(c, motivo, reemplazo, actor);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogNoPreparado(logger, ex.GetType().Name, comparendoId, tenantId);
            db.ChangeTracker.Clear();
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "La petición de asociar el comparendo {Comparendo} de la empresa {Tenant} se canceló; el correo de evidencia sigue con su propio plazo.")]
    private static partial void LogPeticionCancelada(ILogger logger, Guid comparendo, Guid tenant);

    [LoggerMessage(Level = LogLevel.Warning, Message = "No se pudo preparar el correo de evidencia ({Tipo}) del comparendo {Comparendo} de la empresa {Tenant}.")]
    private static partial void LogNoPreparado(ILogger logger, string tipo, Guid comparendo, Guid tenant);

    [LoggerMessage(Level = LogLevel.Warning, Message = "El despacho del correo de evidencia {Comunicacion} quedó pendiente ({Tipo}); el ciclo lo reintenta.")]
    private static partial void LogDespachoPendiente(ILogger logger, string tipo, Guid comunicacion);

    private async Task<long?> TamanoAsync(Guid? archivoId, Guid tenantId, CancellationToken ct) =>
        archivoId is { } id ? await db.Archivos.AsNoTracking().Where(a => a.Id == id && a.TenantId == tenantId).Select(a => (long?)a.TamanoBytes).FirstOrDefaultAsync(ct).ConfigureAwait(false) : null;
}
