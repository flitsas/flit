using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Estados;
using Microsoft.Extensions.Logging;

namespace Flit.Tramites.Application.UseCases.ProcedureInstances;

/// <summary>HU #12795 — cómo terminó un trabajo de regeneración anticipada.</summary>
public enum ResultadoRegeneracionAnticipada
{
    /// <summary>El PDF se reconstruyó.</summary>
    Regenerado = 0,

    /// <summary>AC3 — la bandera ya estaba en true (el camino perezoso u otro trabajo lo reconstruyó).</summary>
    OmitidoVigente = 1,

    /// <summary>AC4 — trámite en estado final: la documentación es la que el organismo tuvo a la vista.</summary>
    OmitidoEstadoFinal = 2,

    /// <summary>AC4 — trámite migrado de V1: su expediente es histórico y no se pisa.</summary>
    OmitidoMigrado = 3,

    /// <summary>AC4 — el consolidado vigente lo cargó a mano el admin (<c>Source="user"</c>).</summary>
    OmitidoCargadoManual = 4,

    /// <summary>El trámite no existe en ese tenant (o fue borrado mientras el trabajo esperaba).</summary>
    NoEncontrado = 5,

    /// <summary>El handler de generación devolvió un código de error; se conserva el PDF anterior.</summary>
    Fallido = 6,

    /// <summary>
    /// HU #12787 (AC2) — motivo <c>maestro_radicado</c>: el maestro ya se radicó ante Quipux y es el
    /// documento de la secretaría; queda fijo (<see cref="MaestroRadicadoFijo"/>).
    /// </summary>
    OmitidoMaestroRadicado = 7,
}

/// <summary>
/// HU #12795 — ejecuta UN trabajo de la cola de regeneración anticipada
/// (<see cref="IConsolidadoRegeneracionQueue"/>).
///
/// <para>Decide si hay que regenerar y, si sí, delega en los handlers de siempre
/// (<see cref="GenerarConsolidadoHandler"/> sin <c>force</c> para el wizard,
/// <see cref="GenerarConsolidadoMaestroHandler"/> sin <c>force</c> para el maestro): aquí no hay
/// composición de PDF. Las excepciones se comprueban ANTES de invocarlos porque ninguno de los dos las
/// cubre todas: el del wizard no mira el estado final de un trámite no migrado (lo frena el gate del
/// gestor, que la regeneración interna no atraviesa) y el maestro no mira ni estado, ni migración, ni
/// <c>Source</c>. Una regeneración automática no puede reemplazar documentación definitiva.</para>
///
/// <para>El orden de precedencia del maestro sale del propio handler (configuración del OT vía
/// <c>IOtConfiguredDocumentOrderProvider</c>, con respaldo por modalidad); <c>matrizPrecedencia</c> va
/// en null, igual que en el canal de radicación Quipux, porque la matriz resuelta vive en Admin.</para>
///
/// <para>Tenant: todas las lecturas y el handler reciben el <c>tenantId</c> del trabajo; el repositorio
/// filtra por él explícitamente (el aislamiento no descansa en el RLS).</para>
/// </summary>
public sealed class RegenerarConsolidadoAnticipadoHandler(
    IProcedureInstanceRepository repo,
    GenerarConsolidadoHandler wizardHandler,
    GenerarConsolidadoMaestroHandler maestroHandler,
    ILogger<RegenerarConsolidadoAnticipadoHandler>? logger = null,
    ConsolidadoFalloBitacora? bitacora = null,
    IMaestroRadicadoLookup? maestroRadicado = null)
{
    internal const string TipoAdjuntoWizard = "consolidado";
    internal const string TipoAdjuntoMaestro = "consolidado_maestro";

    // HU #12798 — sin bitácora cableada (tests/composiciones antiguas) el fallo queda solo en el log.
    private readonly ConsolidadoFalloBitacora _bitacora = bitacora ?? new ConsolidadoFalloBitacora();

    // HU #12787 (AC2) — sin Quipux cableado nada está radicado.
    private readonly IMaestroRadicadoLookup _maestroRadicado = maestroRadicado ?? NullMaestroRadicadoLookup.Instance;

    public async Task<ResultadoRegeneracionAnticipada> HandleAsync(
        Guid tenantId,
        Guid procedureInstanceId,
        TipoConsolidado documento,
        CancellationToken ct = default)
    {
        var instance = await repo
            .GetByIdWithAttachmentsAsync(procedureInstanceId, tenantId, ct)
            .ConfigureAwait(false);
        if (instance is null)
            return Omitir(ResultadoRegeneracionAnticipada.NoEncontrado, tenantId, procedureInstanceId, documento);

        // HU #12787 (AC2) — un maestro con radicación VIGENTE (registrado/aprobado) queda fijo. Un rechazo
        // de Quipux ya no fija nada (re-review #12760, N1): el rechazo encola el maestro (HU #12796 AC2) y
        // aquí se regenera; la fila rechazada sigue protegida por RetirarFilas (AttachmentsProtegidosAsync).
        // Trámite en `rechazado`: no se consulta el fijo. El handler de Quipux transiciona el trámite
        // (commit + encolado) ANTES de marcar la submission `rechazado`; un worker rápido la leería aún
        // `registrado` y omitiría la regeneración que el rechazo pidió. Devuelto al gestor, la versión de
        // la secretaría deja de ser la vigente; el binario radicado se conserva igual (protegido).
        var maestroRadicado = documento == TipoConsolidado.Maestro
            && !string.Equals(instance.Status, TramiteEstado.Rechazado, StringComparison.Ordinal)
            && await _maestroRadicado.AttachmentRadicadoAsync(tenantId, procedureInstanceId, ct).ConfigureAwait(false) is not null;

        var omision = MotivoDeOmision(instance, documento, maestroRadicado);
        if (omision is { } motivo)
            return Omitir(motivo, tenantId, procedureInstanceId, documento);

        var tipoAdjunto = documento == TipoConsolidado.Wizard ? TipoAdjuntoWizard : TipoAdjuntoMaestro;
        var hayAnterior = instance.Attachments
            .Any(a => string.Equals(a.Tipo, tipoAdjunto, StringComparison.OrdinalIgnoreCase));
        GenerarConsolidadoResult? result;
        string? error;
        try
        {
            (result, error) = documento == TipoConsolidado.Wizard
                ? await wizardHandler
                    .HandleAsync(procedureInstanceId, tenantId, userId: null, force: false, ct)
                    .ConfigureAwait(false)
                : await maestroHandler
                    .HandleAsync(procedureInstanceId, tenantId, matrizPrecedencia: null, force: false, ct)
                    .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            // HU #12798 (AC3) — el trabajo corre en segundo plano, después de que el hito (p. ej. la
            // asignación de placa) ya confirmó: el fallo NO se propaga, queda en la bitácora del trámite
            // y el PDF anterior sigue en pie (reemplazo seguro, HU #12797).
            if (logger is not null)
            {
                RegeneracionAnticipadaLog.Fallida(
                    logger, documento, procedureInstanceId, tenantId, ConsolidadoFalloBitacora.CausaExcepcion);
            }

            await _bitacora
                .RegistrarAsync(
                    tenantId, procedureInstanceId, ConsolidadoFalloBitacora.Origenes.RegeneracionAnticipada,
                    tipoAdjunto, ConsolidadoFalloBitacora.CausaExcepcion, ex, conAnterior: hayAnterior, ct)
                .ConfigureAwait(false);
            return ResultadoRegeneracionAnticipada.Fallido;
        }

        if (error is not null || result is null)
        {
            if (logger is not null)
            {
                RegeneracionAnticipadaLog.Fallida(
                    logger, documento, procedureInstanceId, tenantId, error ?? "sin_resultado");
            }

            // HU #12798 (AC1/AC5) — solo los fallos reales van a la bitácora; not_found y
            // migrado_solo_lectura son omisiones, no fallos.
            if (ConsolidadoFalloBitacora.EsFallo(error))
            {
                await _bitacora
                    .RegistrarAsync(
                        tenantId, procedureInstanceId, ConsolidadoFalloBitacora.Origenes.RegeneracionAnticipada,
                        tipoAdjunto, error!, excepcion: null, conAnterior: hayAnterior, ct)
                    .ConfigureAwait(false);
            }

            return ResultadoRegeneracionAnticipada.Fallido;
        }

        // Carrera: entre la comprobación y el handler otro camino pudo subir la bandera; el handler
        // entonces reutiliza el PDF vigente y lo informa con Regenerado=false.
        if (!result.Regenerado)
            return Omitir(ResultadoRegeneracionAnticipada.OmitidoVigente, tenantId, procedureInstanceId, documento);

        if (logger is not null)
            RegeneracionAnticipadaLog.Regenerado(logger, documento, procedureInstanceId, tenantId);

        return ResultadoRegeneracionAnticipada.Regenerado;
    }

    /// <summary>
    /// AC3/AC4 — motivo por el que el trabajo NO debe regenerar, o <c>null</c> si procede. Orden: la
    /// documentación definitiva primero (estado final, migrado, maestro radicado ante Quipux), luego el
    /// PDF cargado a mano y por último la vigencia.
    /// </summary>
    internal static ResultadoRegeneracionAnticipada? MotivoDeOmision(
        ProcedureInstance instance, TipoConsolidado documento, bool maestroRadicado = false)
    {
        if (TramiteEstado.EsFinal(instance.Status))
            return ResultadoRegeneracionAnticipada.OmitidoEstadoFinal;

        // Migrado en cualquier estado: la anticipación es opcional y el camino perezoso sigue
        // disponible para el borrador migrado que el gestor retome en V2; lo que no puede pasar es que
        // un trabajo en segundo plano reemplace el expediente traído de V1 sin que nadie lo pida.
        if (instance.IsMigrated)
            return ResultadoRegeneracionAnticipada.OmitidoMigrado;

        // HU #12787 (AC2) — motivo `maestro_radicado`: el maestro radicado queda fijo.
        if (documento == TipoConsolidado.Maestro && maestroRadicado)
            return ResultadoRegeneracionAnticipada.OmitidoMaestroRadicado;

        var tipoAdjunto = documento == TipoConsolidado.Wizard ? TipoAdjuntoWizard : TipoAdjuntoMaestro;
        // El más reciente, igual que la entrega: tras conservar un maestro radicado puede haber dos filas
        // del tipo y un FirstOrDefault sin orden decidiría `Source`/vigencia al azar (re-review, N5).
        var vigente = ConsolidadoEntregaModos.Existente(instance, tipoAdjunto);

        if (vigente is not null && string.Equals(vigente.Source, "user", StringComparison.OrdinalIgnoreCase))
            return ResultadoRegeneracionAnticipada.OmitidoCargadoManual;

        var bandera = documento == TipoConsolidado.Wizard
            ? instance.ConsolidadoWizardVigente
            : instance.ConsolidadoMaestroVigente;

        // Mismo criterio que el atajo de caché de ambos handlers: bandera arriba Y adjunto presente.
        if (bandera && vigente is not null)
            return ResultadoRegeneracionAnticipada.OmitidoVigente;

        return null;
    }

    private ResultadoRegeneracionAnticipada Omitir(
        ResultadoRegeneracionAnticipada motivo, Guid tenantId, Guid procedureInstanceId, TipoConsolidado documento)
    {
        if (logger is not null)
            RegeneracionAnticipadaLog.Omitida(logger, documento, procedureInstanceId, tenantId, motivo);

        return motivo;
    }
}

/// <summary>Logging source-generated (CA1848) de la regeneración anticipada. Solo ids, sin PII.</summary>
internal static partial class RegeneracionAnticipadaLog
{
    [LoggerMessage(Level = LogLevel.Information,
        Message = "Regeneración anticipada del consolidado {Documento} omitida (instancia {InstanceId}, tenant {TenantId}): {Motivo}.")]
    public static partial void Omitida(
        ILogger logger, TipoConsolidado documento, Guid instanceId, Guid tenantId, ResultadoRegeneracionAnticipada motivo);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Regeneración anticipada del consolidado {Documento} fallida (instancia {InstanceId}, tenant {TenantId}): {Error}. Se conserva el PDF anterior.")]
    public static partial void Fallida(
        ILogger logger, TipoConsolidado documento, Guid instanceId, Guid tenantId, string error);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Consolidado {Documento} regenerado por anticipado (instancia {InstanceId}, tenant {TenantId}).")]
    public static partial void Regenerado(
        ILogger logger, TipoConsolidado documento, Guid instanceId, Guid tenantId);
}
