using System.Text.Json;
using System.Text.Json.Nodes;
using Flit.Tramites.Application.Documents;
using Flit.Tramites.Domain.Documents;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Enums;
using Flit.Tramites.Domain.Integration;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Enums;
using Flit.Tramites.Domain.Tramites.Estados;
using Flit.Tramites.Domain.Tramites.Services;
using Flit.Tramites.Domain.Tramites.ValueObjects;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Flit.Tramites.Application.UseCases.ProcedureInstances.Estados;

/// <summary>
/// Servicio ÚNICO de ciclo de vida del trámite (N 03, ADR-0022). Toda transición de
/// <c>procedure_instances.status</c> pasa por aquí: máquina (RF02), estados finales (RF04),
/// gate de preparación (RF03: identidad + documentos, <see cref="SubmitGate"/>), gates OT de
/// entrega (organismo habilitado + reglas OT, heredados del submit), motivo obligatorio para
/// anular/rechazar (RF05), historial (<see cref="ITramiteTransitionRecorder"/>) y publicación
/// (<see cref="ITramiteTransitionPublisher"/>) en la MISMA unidad de trabajo — un solo
/// SaveChanges con guarda de concurrencia por <c>row_version</c> (RNF01: conflicto → sin
/// efectos parciales).
/// </summary>
public sealed class TramiteLifecycleService(
    IProcedureInstanceRepository repo,
    IProcedureTypeRepository typeRepo,
    ITransitOfficeGrantGate transitOfficeGrantGate,
    IOtOperabilityGate otOperabilityGate,
    IOtRuleGate otRuleGate,
    ITramiteTransitionRecorder recorder,
    ITramiteTransitionPublisher publisher,
#pragma warning disable CS9113 // Bug #13194 (D2): se conserva la firma (DI y llamadores posicionales); ya no relaja la identidad.
    IIdentityValidationPolicy? identityPolicy = null,
#pragma warning restore CS9113
    IProcedureInstancePrendaRepository? prendaRepo = null,
    ChecklistMatrixCompleteness? matrixCompleteness = null,
    IDynamicProceduresPolicy? dynamicPolicy = null,
    IProcedureTypeSnapshotRepository? snapshotRepo = null,
    ISignatureVaultPolicy? vaultPolicy = null,
    IMandateRequirementPolicy? mandatePolicy = null,
    IMandateSignerDirectory? mandateDirectory = null,
    IPrendaDocumentRequirementPolicy? prendaDocumentRequirementPolicy = null,
    // HU #10970 — se añade AL FINAL, después de los parámetros que traía develop, para no desplazar
    // ninguna posición existente (varios call sites pasan estos opcionales por posición).
    TramiteValidationPolicy? validationPolicy = null,
    // HU #12796 (Épica #12760, D1) — cola de regeneración anticipada del consolidado. AL FINAL por la
    // misma razón que validationPolicy. Null en tests que no la ejercitan: sin cola, solo el perezoso.
    IConsolidadoRegeneracionQueue? regeneracionQueue = null,
    ILogger<TramiteLifecycleService>? logger = null,
    // HU #12775 AC3 — al final por la misma razón que el anterior. Null en tests que no lo ejercitan:
    // sin resolutor el gate de Cámara de Comercio se omite (comportamiento previo a la HU).
    CamaraComercioRequirementResolver? camaraComercioResolver = null,
    // HU #13144 (ADR-0066) — para excluir del gate el mandato personalizado de la compañía (ADR-0042). AL FINAL
    // por la misma razón que los anteriores. Null ⇒ nunca hay mandato personalizado.
    IPersonalizedDocumentResolver? personalizedDocumentResolver = null,
    // Bug #13194 (P4, D2) — al final por la misma razón. Null ⇒ el gate bloquea igual, sin notificar.
    IFirmaPendienteNotifier? firmaNotifier = null,
    // Bug #13194 (MAYOR-1) — accesor scoped para la extensión `partesSinFirma` del 409. Null en tests.
    UltimoBloqueoFirma? ultimoBloqueo = null) : ITramiteLifecycleService
{
    private readonly ILogger<TramiteLifecycleService> _logger =
        logger ?? NullLogger<TramiteLifecycleService>.Instance;

    // ADR-0036 (HU #10912/#10916) — config de mandato del OT (plantilla / exige a PN). Default seguro
    // (NUNCA resuelve ⇒ solo PJ, plantilla genérica) en tests que no lo ejercitan.
    private readonly IMandateRequirementPolicy _mandatePolicy = mandatePolicy ?? NullMandateRequirementPolicy.Instance;

    // ADR-0036 §D9 (HU #10916) — directorio de mandatarios del OT para resolver el firmante al aprobar.
    // Default seguro (NUNCA resuelve candidatos) en tests que no lo ejercitan.
    private readonly IMandateSignerDirectory _mandateDirectory = mandateDirectory ?? NullMandateSignerDirectory.Instance;

    // HU #13144 (ADR-0066) — evaluador ÚNICO del mandatario para el gate de radicación. Solo existe si el
    // directorio se cableó de verdad: con mandateDirectory nulo (tests que no lo ejercitan) el chequeo NO se
    // evalúa, igual que matrixCompleteness. No basta con NullMandateSignerDirectory: devolvería cero
    // candidatos y bloquearía todo.
    private readonly MandateSignerEvaluator? _mandateEvaluator = mandateDirectory is null
        ? null
        : new MandateSignerEvaluator(mandateDirectory, mandatePolicy, vaultPolicy, personalizedDocumentResolver);

    // HU #10970 — modo por ambiente de CF-03 en el gate de radicación. Sin inyectar ⇒ bloqueo duro
    // (comportamiento previo a esta historia).
    private readonly TramiteValidationPolicy _validationPolicy =
        validationPolicy ?? TramiteValidationPolicy.BlockAll;

    // Bug #13194 (P4, D2) — la política de identidad por OT (HU #10548) YA NO relaja ningún gate: «no se
    // permite enviar al OT trámites sin firmar», tampoco en un OT con la validación deshabilitada. El
    // parámetro `identityPolicy` se conserva sin uso para no romper la composición ni los llamadores.

    // FEATURE-08 / HU-BE-06 — flag F08_DynamicProcedures (default deshabilitado → SubmitGate estático).
    private readonly IDynamicProceduresPolicy _dynamicPolicy =
        dynamicPolicy ?? NullDynamicProceduresPolicy.Instance;

    // ADR-0025 §4 / HU #10645 — baúl de firmas: un actor NIT cubierto por una firma activa+vigente
    // cuenta como identidad aprobada en el gate de preparación (SubmitGate). Default seguro en tests.
    private readonly ISignatureVaultPolicy _vaultPolicy = vaultPolicy ?? NullSignatureVaultPolicy.Instance;

    // R10 (HU #10597) — repo de prenda para el gate de traspaso. Null en tests que no lo ejercitan
    // (el gate se omite de forma segura); en producción lo inyecta el contenedor.
    private readonly IProcedureInstancePrendaRepository? _prendaRepo = prendaRepo;

    // CF-06 (HU #10881) — override OT del documento de prenda, independiente del semáforo de
    // gravámenes. Default permisivo (nunca exige) cuando no hay política cableada (tests).
    private readonly IPrendaDocumentRequirementPolicy _prendaDocumentRequirementPolicy =
        prendaDocumentRequirementPolicy ?? NullPrendaDocumentRequirementPolicy.Instance;

    public async Task<TramiteTransitionOutcome> TransitionAsync(
        TramiteTransitionCommand command,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (!TramiteEstado.EsValido(command.ToStatus))
            return TramiteTransitionOutcome.Fail(
                TramiteEstadoErrores.EstadoDesconocido,
                $"'{command.ToStatus}' no es un estado de trámite conocido.");

        var instance = await repo.GetByIdWithWizardGraphAsync(command.InstanceId, command.TenantId, ct);
        if (instance is null)
            return TramiteTransitionOutcome.Fail(TramiteEstadoErrores.NoEncontrado);

        var from = instance.Status;

        // RF04 — aprobado/anulado son inmutables: ni transiciones ni edición de datos.
        if (TramiteEstado.EsFinal(from))
            return TramiteTransitionOutcome.Fail(
                TramiteEstadoErrores.EstadoFinal,
                $"El trámite está en estado final '{from}' y no admite más transiciones.");

        // ADR-0059 — la política decide con contexto (máquina + tipo pide placa + placa presente + actor
        // + subsanación activa). Aquí se resuelven también la re-radicación sin flag y las aristas de
        // la ruta de placa; el resto de gates de abajo asumen una arista ya válida.
        var veredicto = TramiteTransitionPolicy.Evaluate(
            from, command.ToStatus, TransitionContext.ForInstance(instance, command.Actor));
        if (!veredicto.Allowed)
            return TramiteTransitionOutcome.Fail(veredicto.ErrorCode!, veredicto.Detail);

        // Radicación: preparado → entregado|preasignacion, o re-radicación desde rechazado. Es el momento
        // en que el trámite LLEGA al organismo: corren los gates de entrega y se fija submitted_at.
        // «Enviar al OT» (asignado → entregado) no es una radicación: el trámite ya llegó al OT.
        var esRadicacion = TramiteEstado.EsRadicacion(from, command.ToStatus);

        // RF05 — anular/rechazar exigen motivo explícito para el historial.
        if (command.ToStatus is TramiteEstado.Anulado or TramiteEstado.Rechazado
            && string.IsNullOrWhiteSpace(command.Reason))
            return TramiteTransitionOutcome.Fail(
                TramiteEstadoErrores.MotivoRequerido,
                $"Debe indicar el motivo para pasar el trámite a '{command.ToStatus}'.");

        // Re-radicación selectiva: desde subsanación (flag sobre rechazado, o legado status
        // subsanacion) → entregado. Solo re-evalúa gates afectados por el diff del snapshot.
        var isSubsanacionReradicacion =
            TramiteEstado.EsReRadicacionSubsanacion(from, instance.SubsanacionActiva) && esRadicacion;

        var affectedGates = isSubsanacionReradicacion
            ? await ResolveSubsanacionAffectedGatesAsync(instance, command.TenantId, ct).ConfigureAwait(false)
            : SubsanacionGateMap.AllGates;

        // CF-03 (HU #10877) — precondición registral "vehículo ya matriculado", SEGUNDO momento
        // ("de nuevo al radicar", el estado pudo cambiar desde el preflight). SOLO Matrícula Inicial,
        // SOLO fuente FLIT (bloqueo duro por repo, sin IO externo al RUNT en este gate — la fuente RUNT
        // ya se validó de forma DURA en el preflight, AC1/AC3): si OTRO trámite del mismo VIN llegó a
        // 'aprobado' mientras este seguía en curso, esta relectura lo atrapa antes de preparar/entregar.
        // Al re-radicar desde subsanación, SOLO si el VIN fue uno de los campos corregidos (o no hay
        // snapshot base: fail-safe).
        if ((command.ToStatus == TramiteEstado.Preparado || esRadicacion)
            && affectedGates.Contains(SubsanacionGateMap.VehicleState))
        {
            var vehicleStateDetail = await EvaluarEstadoVehiculoRegistralAsync(instance, ct).ConfigureAwait(false);
            if (vehicleStateDetail is not null)
                return TramiteTransitionOutcome.Fail(VehicleStatePolicy.ErrorCode, vehicleStateDetail);
        }

        // RF03/R10 — gate de preparación: SIEMPRE en borrador→preparado; en re-radicación de
        // subsanación SOLO si el diff toca PreparationGate.
        var debeEvaluarGatePreparacion =
            (from == TramiteEstado.Borrador && command.ToStatus == TramiteEstado.Preparado)
            || (isSubsanacionReradicacion
                && affectedGates.Contains(SubsanacionGateMap.PreparationGate));

        if (debeEvaluarGatePreparacion)
        {
            var gatePreparacionError = await EvaluarGatePreparacionAsync(instance, command, ct).ConfigureAwait(false);
            if (gatePreparacionError is var (code, detail) && code is not null)
            {
                // Bug #13194 (P4, D2) — borrador→preparado bloqueado: si además faltan firmas, se dispara el
                // correo de validación de cada parte (idempotente). El código del gate no cambia.
                if (from == TramiteEstado.Borrador && FirmaGate.Aplica(command.ToStatus, command.Actor))
                {
                    var faltantes = await FirmaGate
                        .PartesSinFirmaAsync(repo, instance, _vaultPolicy, DateTimeOffset.UtcNow, ct)
                        .ConfigureAwait(false);
                    if (faltantes.Count > 0)
                    {
                        var notificadas = await NotificarPartesSinFirmaAsync(instance, faltantes, ct)
                            .ConfigureAwait(false);
                        RegistrarBloqueo(notificadas);
                        return TramiteTransitionOutcome.Fail(
                                code, $"{detail} Firma pendiente de: {FirmaGate.PartesConNotificacion(notificadas)}.")
                            with { PartesSinFirma = notificadas };
                    }
                }

                return TramiteTransitionOutcome.Fail(code, detail);
            }
        }

        // Bug #13194 (P4, D2) — gate de FIRMA único: «no se permite enviar al OT trámites sin firmar».
        // Corre en TODA llegada a preparado / preasignacion / entregado del gestor o el sistema (preparar,
        // radicar, re-radicar, «Enviar al OT», cambio de estado admin), sin relajación por OT. Va después
        // del gate de preparación para que borrador→preparado siga reportando su lista completa
        // (identidad_no_aprobada incluida) y antes de los gates de entrega, que promueven el OT.
        if (FirmaGate.Aplica(command.ToStatus, command.Actor))
        {
            var sinFirma = await FirmaGate
                .PartesSinFirmaAsync(repo, instance, _vaultPolicy, DateTimeOffset.UtcNow, ct)
                .ConfigureAwait(false);
            if (sinFirma.Count > 0)
            {
                // Cada parte sin firma (baúl o VID ausentes o vencidos) recibe el correo de validación. Un
                // fallo de la notificación no cambia el 409: queda como estado «fallida» de esa parte.
                var notificadas = await NotificarPartesSinFirmaAsync(instance, sinFirma, ct).ConfigureAwait(false);
                RegistrarBloqueo(notificadas);
                return TramiteTransitionOutcome.Fail(TramiteEstadoErrores.FirmaPendiente, FirmaGate.Detalle(notificadas))
                    with { PartesSinFirma = notificadas };
            }
        }

        // Gates OT de entrega (heredados del submit HU #10217/#2). HU #10872 (AC1) — este es el GATE
        // FINAL de radicación: corre SIEMPRE, sin importar el diff de campos corregidos.
        var metadataTransicion = command.Metadata;
        if (esRadicacion)
        {
            var entregaError = await EvaluarEntregaAsync(instance, ct).ConfigureAwait(false);
            if (entregaError is var (code, detail) && code is not null)
                return TramiteTransitionOutcome.Fail(code, detail);

            // HU #13144 (ADR-0066) — mandatario activo, vigente y con firma válida. DESPUÉS de grant,
            // operabilidad y reglas OT (no tapa sus mensajes), cuando el organismo ya está promovido. Cubre la
            // primera radicación y la re-radicación desde subsanación, y los dos destinos de /submit.
            var mandatarioGate = await EvaluarMandatarioAlRadicarAsync(instance, ct).ConfigureAwait(false);
            if (mandatarioGate.Code is not null)
                return TramiteTransitionOutcome.Fail(mandatarioGate.Code, mandatarioGate.Detail);
            if (mandatarioGate.Aviso is not null)
                metadataTransicion = MergeMetadata(metadataTransicion, mandatarioGate.Aviso);
        }

        // ADR-0036 §D9 (HU #10916) — al APROBAR, resolver el mandatario que firma el mandato: automático
        // si hay uno solo o el cotejo por usuario es único; explícito (mandateSignerId) si hay varios sin
        // match ⇒ 409 mandatario_requerido. Fija instance.MandateSignerId en la MISMA unidad de trabajo;
        // la regeneración del PDF del mandato con el firmante la dispara el handler tras el commit.
        if (command.ToStatus == TramiteEstado.Aprobado)
        {
            var (mandatoError, sinCandidatos) =
                await ResolverMandatarioAlAprobarAsync(instance, command, ct).ConfigureAwait(false);
            if (mandatoError is not null)
                return TramiteTransitionOutcome.Fail(mandatoError, DetalleMandatario(mandatoError, sinCandidatos));
        }

        var now = DateTimeOffset.UtcNow;
        instance.Status = command.ToStatus;
        instance.UpdatedAt = now;
        if (esRadicacion)
        {
            instance.SubmittedAt = now;

            // Cierra la ventana de edición de subsanación al re-radicar. El baseline ya se consumió
            // en el diff de gates de esta misma transición, así que se suelta con la ventana.
            if (instance.SubsanacionActiva)
            {
                instance.SubsanacionActiva = false;
                instance.SubsanacionBaseline = null;
            }
        }

        // Si se anula o se vuelve a borrador, apagar el flag de subsanación.
        if (command.ToStatus is TramiteEstado.Anulado or TramiteEstado.Borrador)
        {
            instance.SubsanacionActiva = false;
            instance.SubsanacionBaseline = null;
        }

        // ADR-0059 (HU #12597) — origen del rechazo: se fija al entrar a 'rechazado' (el gestor distingue
        // un rechazo desde preasignación) y se limpia al salir de él por cualquier arista.
        instance.RejectedFrom = command.ToStatus == TramiteEstado.Rechazado ? from : null;

        // Feature #10701 / HU #10860 — un cambio de estado invalida los consolidados persistidos
        // (maestro y wizard): el expediente cambió, así que la próxima generación debe regenerarlos
        // (el wizard además regenera en cascada el FUR con fecha vigente).
        instance.InvalidarConsolidados();

        var record = new TramiteTransitionRecord(
            command.TenantId,
            instance.Id,
            from,
            command.ToStatus,
            command.Reason,
            command.ChangedByUserId,
            now,
            metadataTransicion);

        // Historial (RF05) + publicación (RNF01) se ENCOLAN en la misma unidad de trabajo;
        // el commit único de abajo los persiste o descarta en bloque.
        await recorder.RecordAsync(record, ct).ConfigureAwait(false);
        await publisher.EnqueueAsync(record, ct).ConfigureAwait(false);

        var committed = await repo.SaveChangesWithConcurrencyGuardAsync(ct).ConfigureAwait(false);
        if (!committed)
            return TramiteTransitionOutcome.Fail(
                TramiteEstadoErrores.ConflictoConcurrencia,
                "El trámite fue modificado por otro proceso. Recargue el trámite e intente de nuevo.");

        // HU #12796 — hitos anticipados, SIEMPRE después del commit (una transición revertida no puede
        // dejar trabajo en cola). AC1: la radicación deja el trámite en el OT y lo primero que el organismo
        // abre es el consolidado maestro. AC2: un expediente entregado que el OT devuelve por esta vía
        // (decisión sincronizada desde Quipux) vuelve al gestor, así que se anticipan los dos. La decisión
        // desde la consola OT no pasa por aquí: la cubre OtClientProcedureRepository. Aprobar (AC4) y el
        // resto de aristas se quedan con la invalidación de arriba y el camino perezoso.
        // HU #12787 (AC2) — la radicación del canal Quipux (actor Quipux) NO encola el maestro: el que se
        // acaba de radicar es el documento de la secretaría y queda fijo. El worker también lo omite
        // (`maestro_radicado`), pero la submission se marca radicada DESPUÉS de esta transición: sin este
        // corte, un worker rápido podría ganar esa ventana.
        if (esRadicacion && command.Actor != TramiteActor.Quipux)
        {
            EncolarRegeneracionAnticipada(instance.TenantId, instance.Id, TipoConsolidado.Maestro);
        }
        else if (from == TramiteEstado.Entregado && command.ToStatus == TramiteEstado.Rechazado)
        {
            EncolarRegeneracionAnticipada(instance.TenantId, instance.Id, TipoConsolidado.Wizard);
            EncolarRegeneracionAnticipada(instance.TenantId, instance.Id, TipoConsolidado.Maestro);
        }

        return TramiteTransitionOutcome.Ok(instance);
    }

    /// <summary>Bug #13194 (MAYOR-1) — deja las partes del bloqueo en el accesor scoped de la petición.</summary>
    private void RegistrarBloqueo(IReadOnlyList<ParteSinFirma> partes)
    {
        if (ultimoBloqueo is not null)
            ultimoBloqueo.PartesSinFirma = partes;
    }

    /// <summary>
    /// Bug #13194 (P4, D2) — notifica (correo de validación) cada parte sin firma. Nunca lanza: una excepción
    /// del notificador se registra sin PII y la parte queda en <see cref="FirmaNotificacionEstados.Fallida"/>.
    /// </summary>
    private Task<IReadOnlyList<ParteSinFirma>> NotificarPartesSinFirmaAsync(
        ProcedureInstance instance, IReadOnlyList<string> partes, CancellationToken ct) =>
        FirmaGate.NotificarAsync(
            firmaNotifier, instance.Id, instance.TenantId, partes,
            (tipo, parte) => TramiteLifecycleLog.NotificacionFirmaFallida(_logger, tipo, instance.Id, parte),
            ct);

    /// <summary>
    /// HU #12796 — pide la regeneración anticipada sin afectar al hito: la cola no bloquea y un descarte
    /// (<c>false</c>) solo se registra, porque la bandera ya quedó abajo y el perezoso lo reconstruye.
    /// </summary>
    private void EncolarRegeneracionAnticipada(Guid tenantId, Guid instanceId, TipoConsolidado documento)
    {
        if (regeneracionQueue is null)
            return;

        if (!regeneracionQueue.Encolar(tenantId, instanceId, documento))
            TramiteLifecycleLog.RegeneracionAnticipadaDescartada(_logger, instanceId, tenantId, documento);
    }

    /// <summary>
    /// HU #13144 (ADR-0066) — gate de radicación del mandatario. <c>off</c> o evaluador no cableado: no se
    /// consulta el directorio ni se registra nada. <c>block</c>: rechaza con <c>mandatario_no_configurado</c> o
    /// <c>mandatario_firma_invalida</c>. <c>warn</c>: radica y devuelve el aviso (motivo, organismo y compañía,
    /// sin datos personales) para el log estructurado y la clave <c>mandatario_aviso</c> de los metadatos de la
    /// transición, que es la fuente persistente para medir el volumen.
    /// </summary>
    private async Task<(string? Code, string? Detail, JsonObject? Aviso)> EvaluarMandatarioAlRadicarAsync(
        ProcedureInstance instance, CancellationToken ct)
    {
        var modo = _validationPolicy.MandatarioRequerido;
        if (_mandateEvaluator is null || modo == TramiteValidationMode.Off)
            return (null, null, null);

        MandateSignerEvaluacion evaluacion;
        try
        {
            evaluacion = await _mandateEvaluator.EvaluateAsync(instance, null, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (modo == TramiteValidationMode.Warn && ex is not OperationCanceledException)
        {
            // En warn la medición nunca detiene una radicación.
            TramiteLifecycleLog.MandatarioEvaluacionFallida(_logger, instance.Id, instance.TenantId, ex);
            return (null, null, null);
        }

        if (evaluacion.CodigoDeError is not { } codigo)
            return (null, null, null);

        if (modo == TramiteValidationMode.Block)
            return (codigo, evaluacion.MensajeDeError, null);

        var motivo = evaluacion.Motivo ?? MandateSignerEstados.MotivoSinCandidatos;
        TramiteLifecycleLog.MandatarioAviso(
            _logger, instance.Id, codigo, motivo, evaluacion.TransitOfficeId, instance.TenantId);

        return (null, null, new JsonObject
        {
            ["modo"] = "warn",
            ["codigo"] = codigo,
            ["estado"] = MandateSignerEstados.ToCode(evaluacion.Estado),
            ["motivo"] = motivo,
            ["transitOfficeId"] = evaluacion.TransitOfficeId?.ToString(),
            ["companyTenantId"] = instance.TenantId.ToString(),
        });
    }

    /// <summary>Agrega <c>mandatario_aviso</c> a los metadatos de la transición sin perder lo que ya traían.</summary>
    private static string MergeMetadata(string? existing, JsonObject aviso)
    {
        JsonObject root;
        try
        {
            root = string.IsNullOrWhiteSpace(existing)
                ? []
                : JsonNode.Parse(existing) as JsonObject ?? new JsonObject { ["original"] = existing };
        }
        catch (JsonException)
        {
            root = new JsonObject { ["original"] = existing };
        }

        root["mandatario_aviso"] = aviso;
        return root.ToJsonString();
    }

    /// <summary>
    /// ADR-0036 §D9 (HU #10916) — resuelve el mandatario del mandato al aprobar. Devuelve el código de
    /// error (<c>mandatario_requerido</c>) si hay varios mandatarios y ninguno cotejó; <c>null</c> si no
    /// hay nada que resolver (el mandato no aplica, o el mandatario es institucional sin firmante persona)
    /// o si el firmante quedó fijado en <c>instance.MandateSignerId</c>.
    /// </summary>
    private async Task<(string? Code, bool SinCandidatos)> ResolverMandatarioAlAprobarAsync(
        ProcedureInstance instance, TramiteTransitionCommand command, CancellationToken ct)
    {
        // Producto: el mandato aplica siempre (PN y PJ); aquí solo resolvemos firmante / plantilla.
        var code = instance.FieldValues.FirstOrDefault(f =>
            string.Equals(f.FieldKey, "transit_office_code", StringComparison.OrdinalIgnoreCase))?.ValueText;
        var config = string.IsNullOrWhiteSpace(code)
            ? null
            : await _mandatePolicy.ResolveAsync(code, instance.TenantId, ct).ConfigureAwait(false);

        // Institucional u abierto (regla compañía×OT): no hay firmante persona que resolver.
        if (MandatoAssignmentModeCodes.SkipsPersonSigner(config?.AssignmentMode))
            return (null, false);

        // El OT debe estar promovido (se hizo en la entrega). Sin él no podemos consultar el directorio.
        if (instance.TransitOfficeId is not { } transitOfficeId)
            return (null, false);

        var (prelacion, _) = await MandateSignerPrelacionLoader
            .ResolveAsync(
                _mandateDirectory, _vaultPolicy, transitOfficeId, instance.TenantId,
                MandateSignerSelectionResolver.ResolveNitMandante(instance), config,
                command.MandateSignerId, instance.MandateSignerId, ct)
            .ConfigureAwait(false);

        var resolution = MandateSignerPrelacionLoader.Decidir(prelacion, command.ChangedByUserId);

        switch (resolution.Status)
        {
            case MandateSignerResolutionStatus.Resolved:
                instance.MandateSignerId = resolution.Signer!.Id;
                return (null, false);
            case MandateSignerResolutionStatus.RequiereSeleccion:
                // HU #13137 — con cero candidatos válidos el mensaje NO dice «hay varios»: no queda mandatario.
                return (TramiteEstadoErrores.MandatarioRequerido, prelacion.Validos.Count == 0);
            default:
                // NoConfigurado: el OT no tiene mandatarios; se aprueba sin firmante (el mandato queda con
                // placeholder hasta que el OT registre uno y se regenere). No bloquea la aprobación.
                return (null, false);
        }
    }

    /// <summary>Detalle del error de mandatario para el mensaje al usuario (ADR-0036 §D9).</summary>
    private static string DetalleMandatario(string code, bool sinCandidatos = false) => code switch
    {
        TramiteEstadoErrores.MandatarioRequerido when sinCandidatos =>
            MandateSignerEstados.MensajeSinMandatarioAlAprobar,
        TramiteEstadoErrores.MandatarioRequerido =>
            "El mandatario que firma el mandato debe elegirse entre los mandatarios vigentes de la compañía " +
            "en este organismo: ninguno quedó determinado (o el elegido ya no es válido). " +
            "Elija uno e intente aprobar de nuevo.",
        _ => "No se pudo resolver el mandatario del mandato.",
    };

    /// <summary>
    /// Causa(s) exacta(s) del gate de preparación (RF03) para el mensaje al usuario. Lista TODO lo que
    /// falta (no solo el primer bloqueo) con un texto legible por cada código de <see cref="SubmitGate"/>,
    /// para que el encabezado del wizard diga qué debe completar el gestor en vez de un genérico.
    /// </summary>
    private static string DetalleGatePreparacion(IReadOnlyList<string> codes)
    {
        var faltantes = codes.Select(FaltanteGatePreparacion).ToList();
        return faltantes.Count == 1
            ? $"No se puede preparar el trámite: falta {faltantes[0]}."
            : "No se puede preparar el trámite. Falta: " + string.Join("; ", faltantes) + ".";
    }

    /// <summary>Fragmento legible de lo que falta por cada código de gate de preparación (RF03).</summary>
    private static string FaltanteGatePreparacion(string code) => code switch
    {
        TramiteEstadoErrores.DocumentosIncompletos => "cargar los documentos obligatorios del checklist",
        TramiteEstadoErrores.CamaraComercioPendiente => "cargar el certificado de Cámara de Comercio de la parte persona jurídica",
        TramiteEstadoErrores.IdentidadNoAprobada => "aprobar la validación de identidad de las partes (comprador/vendedor)",
        SubmitGate.FurRequerido => "generar el FUR",
        SubmitGate.OrganismoRequerido => "seleccionar el organismo de tránsito",
        SubmitGate.ImprontaRequerida => "generar la impronta de motor y chasis",
        _ => $"resolver un requisito pendiente ({code})",
    };

    /// <summary>
    /// RF03/R10 — gate de preparación: identidad aprobada/vigente + documentos obligatorios + impronta
    /// + prenda del traspaso. Extraído para reusarse SIN duplicar lógica (HU #10872 AC1) desde dos
    /// disparadores: borrador→preparado (siempre) y subsanacion→entregado (solo si el diff de campos
    /// corregidos toca <see cref="SubsanacionGateMap.PreparationGate"/>). <c>(null, null)</c> = puede
    /// avanzar. La resolución de identidad reutiliza validaciones vigentes existentes — NUNCA solicita
    /// una nueva biométrica aquí (AC2: "no se vuelven a solicitar").
    /// </summary>
    private async Task<(string? Code, string? Detail)> EvaluarGatePreparacionAsync(
        ProcedureInstance instance,
        TramiteTransitionCommand command,
        CancellationToken ct)
    {
        // Identidad PER-PERSONA (documento del actor), referenciada de su validación vigente
        // (HU #10350 rediseño #87): fila propia del trámite O identidad vigente de la persona
        // en otro trámite del tenant, sin clonar. HU #10872 (AC2) — es la MISMA resolución de siempre:
        // no dispara ninguna solicitud nueva, solo consulta vigencia de lo ya validado.
        // Bug #13194 (P4, D2) — sin relajación por OT: un OT con la validación de identidad deshabilitada
        // (HU #10548) ya no da la identidad por satisfecha.
        var identidadAprobada = await IdentityApprovalResolver.ResolveApprovedPartiesAsync(
            repo, instance, DateTimeOffset.UtcNow, ct, _vaultPolicy).ConfigureAwait(false);

        // HU #10522 (RF17/RF22) — el gestor manda la completitud documental si tiene matriz.
        var docsCompletos = matrixCompleteness is null
            ? null
            : await matrixCompleteness.TryComputeCompletoAsync(instance, command.TenantId, ct).ConfigureAwait(false);

        // FEATURE-08 / HU-BE-06 (AC-06): para tipos dinámicos (flag F08_DynamicProcedures + snapshot)
        // el gate de preparación se delega en DynamicGateEvaluator.CanSubmitBlockers; en cualquier
        // otro caso se conserva SubmitGate estático (sin regresión).
        ProcedureTypeSnapshotRecord? snapshot = null;
        if (snapshotRepo is not null && await _dynamicPolicy.IsEnabledAsync(instance.TenantId, ct).ConfigureAwait(false))
            snapshot = await snapshotRepo.GetByInstanceIdAsync(instance.Id, command.TenantId, ct).ConfigureAwait(false);

        var gateErrors = snapshot is not null
            ? EvaluateDynamicSubmit(instance, snapshot, identidadAprobada, docsCompletos)
            : SubmitGate.Evaluate(instance, identidadAprobada, docsCompletos);
        if (gateErrors.Count > 0)
            return (gateErrors[0], DetalleGatePreparacion(gateErrors));

        // HU #12775 AC3 — parte jurídica sin firma precargada ni escritura vigente y sin certificado
        // de Cámara de Comercio. El paso del actor ya lo impide en el asistente; esto cierra la puerta
        // de atrás (borradores anteriores a la HU, llamadas directas a la API).
        if (camaraComercioResolver is not null)
        {
            var rolSinCertificado = await camaraComercioResolver
                .RolSinCertificadoAsync(instance.TenantId, instance, ct).ConfigureAwait(false);
            if (rolSinCertificado is not null)
            {
                return (TramiteEstadoErrores.CamaraComercioPendiente,
                    $"No se puede preparar el trámite: falta cargar el certificado de Cámara de Comercio del {rolSinCertificado}.");
            }
        }

        // Precondición del tipo, no un requisito documental: un cambio de carrocería necesita una
        // carrocería de partida. El preflight ya lo corta en el paso 1; esto cierra la puerta de atrás
        // de un borrador abierto antes de que la guarda existiera, que llegaría hasta aquí intacto.
        // Se mira el SNAPSHOT del RUNT y no el valor efectivo, que en este trámite lleva la carrocería
        // NUEVA. En modo warn/off no bloquea (mismo interruptor por ambiente que el preflight).
        if (_validationPolicy.VehicleBodyTypeRequired == TramiteValidationMode.Block
            && VehicleBodyTypePolicy.ExigeCarroceriaPrevia(instance.TypeCode)
            && VehicleBodyTypePolicy.SinCarroceria(FieldValue(instance, VehicleBodyTypePolicy.BodyTypeRuntFieldKey)))
        {
            return (VehicleBodyTypePolicy.ErrorCode,
                "No se puede preparar el trámite: el vehículo no tiene carrocería registrada en el RUNT, "
                + "así que no hay carrocería que cambiar. Vuelve a consultar el vehículo o radica el trámite que corresponda.");
        }

        // El traslado de cuenta declara a qué organismo va, y ese dato es el objeto del trámite: sin
        // él el FUR no puede decir a dónde se traslada. Se exige habilitado para la compañía —será
        // ella quien radique allí después— igual que el organismo del propio trámite.
        //
        // No se confunde con el radicado, que es el trámite espejo: allí el destino ES el organismo
        // del trámite y lo valida el gate de entrega, no este.
        if (ProcedureTypeGateProfile.FromJson(instance.ProcedureType?.GateProfile)
                .RequiresDestinationTransitOffice)
        {
            var destinoError = await ValidarOrganismoDestinoAsync(instance, ct).ConfigureAwait(false);
            if (destinoError is not null)
                return destinoError.Value;
        }

        // HU #12131/#12129 — el levantamiento de prenda sobre un vehículo sin gravamen reportado NO
        // bloquea la preparación (regla de negocio, sin interruptor por ambiente): el gestor captura
        // el acreedor/entidad manualmente en el paso de prenda del asistente. Antes de esta corrección
        // esta "puerta de atrás" replicaba el bloqueo duro que ya se quitó del preflight del paso 1.

        // R10 (HU #10597) — gate de prenda del traspaso: con gravámenes en warn se exige una
        // decisión de prenda vigente (y su documento cuando la decisión lo requiere). "omitir" es
        // la vía "asumo el riesgo" (decisión válida sin documento). Solo con el repo cableado.
        return await EvaluarPrendaGateAsync(instance, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// HU #10872 (AC1) — resuelve las categorías de gate afectadas por la re-radicación desde
    /// subsanación: trae el snapshot de field_values capturado al ENTRAR a subsanación (baseline) y lo
    /// compara contra el estado ACTUAL de <c>instance.FieldValues</c> (<see cref="FieldValueSnapshot.Diff"/>).
    /// Sin snapshot base (dato legado anterior a esta HU, o degradado) el fail-safe es
    /// <see cref="SubsanacionGateMap.NoBaselineFallback"/> — preserva el comportamiento previo a esta
    /// HU en vez de bloquear re-radicaciones legítimas que antes pasaban.
    /// </summary>
    private async Task<IReadOnlySet<string>> ResolveSubsanacionAffectedGatesAsync(
        ProcedureInstance instance, Guid tenantId, CancellationToken ct)
    {
        var baselineJson = await repo
            .GetLatestSubsanacionMetadataAsync(instance.Id, tenantId, ct)
            .ConfigureAwait(false);
        var baseline = SubsanacionObservation.FromJson(baselineJson)?.FieldSnapshot;
        if (baseline is null)
            return SubsanacionGateMap.NoBaselineFallback;

        var current = FieldValueSnapshot.Capture(instance.FieldValues);
        var changedKeys = FieldValueSnapshot.Diff(baseline, current);
        return SubsanacionGateMap.ResolveGates(changedKeys);
    }

    /// <summary>
    /// Gates de la entrega al OT: tipo publicado, organismo elegido HABILITADO para la empresa
    /// (promueve <c>TransitOfficeId</c> desde field_values) y reglas OT. (null, null) = puede entregar.
    /// </summary>
    private async Task<(string? Code, string? Detail)> EvaluarEntregaAsync(
        ProcedureInstance instance,
        CancellationToken ct)
    {
        var procedureType = await typeRepo.GetByIdAsync(instance.ProcedureTypeId, ct).ConfigureAwait(false);
        if (procedureType is null || procedureType.PublicationStatus != PublicationStatus.Published)
            return (TramiteEstadoErrores.TipoNoPublicado, "El tipo de trámite no está publicado.");

        // HU #10604 (R19) — RNMC NO es bloqueante: una medida correctiva pendiente NO veta el envío
        // al OT (antes exigía cargar el paz y salvo RNMC). La señal rnmc_medida_pendiente se conserva
        // como dato INFORMATIVO (visibilidad del OT), pero no gatea aquí.

        // #2 (R09) — el OT elegido en el FUR (transit_office_id en field_values) debe estar
        // HABILITADO para la empresa. Se promueve a la columna TransitOfficeId para que el motor de
        // reglas OT y la bandeja del OT operen sobre el id real; sin el grant el trámite entregado
        // NO aparecería en ninguna bandeja (el diagnóstico operativo lo da el endpoint /health).
        var selectedOfficeId = TransitOfficeIdFromFieldValues(instance);
        if (selectedOfficeId is { } officeId)
        {
            var enabled = await transitOfficeGrantGate
                .IsEnabledForTenantAsync(instance.TenantId, officeId, ct)
                .ConfigureAwait(false);
            if (!enabled)
                return (TramiteEstadoErrores.OrganismoNoHabilitado,
                    $"El organismo de tránsito seleccionado ({officeId}) no está habilitado para la " +
                    "compañía. Solicite el grant OT↔empresa: sin él, el trámite entregado no llegaría " +
                    "a la bandeja del organismo.");

            // HU #10518 — con grant, pero el OT debe estar OPERATIVO en la plataforma:
            // catálogo activo + tenant OT existente y activo. Desactivar el OT (is_active=false)
            // bloquea la radicación aunque el grant siga vigente (no se revoca automáticamente).
            var operable = await otOperabilityGate
                .IsOperableAsync(officeId, ct)
                .ConfigureAwait(false);
            if (!operable)
                return ("organismo_no_operable",
                    "El organismo de tránsito no está operativo en FLIT.");

            instance.TransitOfficeId = officeId;
        }

        var ruleResult = await otRuleGate.EvaluateSubmissionAsync(
            instance.TransitOfficeId,
            instance.ProcedureTypeId,
            procedureType.Code,
            ct).ConfigureAwait(false);

        if (ruleResult.IsBlocked)
            return (ruleResult.ErrorCode ?? TramiteEstadoErrores.ReglaOtBloquea,
                "El trámite está bloqueado por una regla OT activa.");

        return (null, null);
    }

    /// <summary>
    /// Id del organismo de tránsito elegido en el FUR, leído del field_value
    /// <c>transit_office_id</c> (lo persiste el wizard al seleccionar). <c>null</c> si no hay
    /// selección o no es un GUID válido (p. ej. instancias previas a la persistencia del id).
    /// </summary>
    private static Guid? TransitOfficeIdFromFieldValues(ProcedureInstance instance)
    {
        var raw = instance.FieldValues.FirstOrDefault(f =>
            string.Equals(f.FieldKey, "transit_office_id", StringComparison.OrdinalIgnoreCase))?.ValueText;

        return Guid.TryParse(raw, out var id) && id != Guid.Empty ? id : null;
    }

    /// <summary>
    /// Organismo de DESTINO declarado: presente y habilitado para la compañía. Devuelve <c>null</c>
    /// si puede avanzar, o el par (código, detalle) del bloqueo.
    /// </summary>
    private async Task<(string? Code, string? Detail)?> ValidarOrganismoDestinoAsync(
        ProcedureInstance instance,
        CancellationToken ct)
    {
        var destinoId = FieldValue(instance, TransitOfficeFieldKeys.DestinoId);
        if (!Guid.TryParse(destinoId, out var id) || id == Guid.Empty)
        {
            return (TramiteEstadoErrores.OrganismoDestinoRequerido,
                "Selecciona la secretaría de destino: es a dónde se traslada la cuenta y el FUR la declara.");
        }

        var habilitado = await transitOfficeGrantGate
            .IsEnabledForTenantAsync(instance.TenantId, id, ct)
            .ConfigureAwait(false);

        // El grant pudo revocarse entre la elección y la radicación: el borrador vive días.
        return habilitado
            ? null
            : (TramiteEstadoErrores.OrganismoDestinoRequerido,
                "La secretaría de destino ya no está habilitada para la compañía. Selecciona otra "
                + "antes de preparar el trámite.");
    }

    private static string? FieldValue(ProcedureInstance instance, string fieldKey) =>
        instance.FieldValues.FirstOrDefault(f =>
            string.Equals(f.FieldKey, fieldKey, StringComparison.OrdinalIgnoreCase))?.ValueText;

    /// <summary>
    /// FEATURE-08 / HU-BE-06 (AC-06) — gate de preparación para tipos dinámicos: computa los blockers
    /// del submit con <see cref="DynamicGateEvaluator.CanSubmitBlockers"/> desde el gate_profile del
    /// snapshot y las señales de la instancia. Reusa la completitud documental del gestor cuando existe.
    /// </summary>
    private static IReadOnlyList<string> EvaluateDynamicSubmit(
        ProcedureInstance instance,
        ProcedureTypeSnapshotRecord snapshot,
        IReadOnlySet<string> approvedParties,
        bool? docsCompletosOverride)
    {
        var root = JsonNode.Parse(snapshot.Snapshot) as JsonObject ?? [];
        var gateProfile = ProcedureTypeGateProfile.FromJson(root["gateProfile"]?.ToJsonString());

        var ctx = new DynamicWizardContext
        {
            DocumentosCompletos = docsCompletosOverride ?? DocsCompletos(instance),
            BiometricsApproved = MapPartiesToEntityCodes(approvedParties),
            FurGenerado = instance.Attachments.Any(a =>
                string.Equals(a.Tipo, "fur", StringComparison.OrdinalIgnoreCase)),
            PreflightProviderError = LatestPreflightHasProviderError(instance),
            PreflightVehiculoNoEncontrado = LatestPreflightHasVehiculoNoEncontrado(instance),
            UploadedDocumentCodes = new HashSet<string>(
                instance.Attachments.Select(a => a.Tipo), StringComparer.OrdinalIgnoreCase),
        };

        return DynamicGateEvaluator.CanSubmitBlockers(gateProfile, ctx);
    }

    private static bool DocsCompletos(ProcedureInstance instance)
    {
        var manual = ChecklistEstadoJson.Parse(instance.ChecklistEstado);
        var docTipos = instance.Attachments.Select(a => a.Tipo).ToList();
        var codigo = instance.TypeCode;
        var computed = ChecklistEngine.Compute(codigo, manual, docTipos);
        return computed?.Completo ?? true;
    }

    private static bool LatestPreflightHasProviderError(ProcedureInstance instance)
    {
        var latest = instance.PreflightSnapshots.OrderByDescending(s => s.CreatedAt).FirstOrDefault();
        if (latest is null)
            return false;
        var checks = GetPreflightHandler.DeserializeChecks(latest.Checks);
        return checks.Any(c => string.Equals(c.Status, "error", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>El RUNT respondió y el vehículo NO existe (check "vehiculo" en "fail"): bloqueo DURO,
    /// igual que el error de proveedor. Ver PreflightSnapshot.VehiculoNoEncontrado.</summary>
    private static bool LatestPreflightHasVehiculoNoEncontrado(ProcedureInstance instance)
    {
        var latest = instance.PreflightSnapshots.OrderByDescending(s => s.CreatedAt).FirstOrDefault();
        if (latest is null)
            return false;
        var checks = GetPreflightHandler.DeserializeChecks(latest.Checks);
        return checks.Any(c =>
            string.Equals(c.Key, "vehiculo", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(c.Status, "fail", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Mapea partes aprobadas (comprador/vendedor/locatario) a códigos de entidad (BUYER/OWNER/LESSEE).</summary>
    private static HashSet<string> MapPartiesToEntityCodes(IReadOnlySet<string> approvedParties)
    {
        var codes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (approvedParties.Contains(BiometricRules.ParteComprador)) codes.Add("BUYER");
        if (approvedParties.Contains(BiometricRules.ParteVendedor)) codes.Add("OWNER");
        if (approvedParties.Contains("locatario")) codes.Add("LESSEE");
        return codes;
    }

    /// <summary>
    /// CF-03 (HU #10877) — re-evalúa la fuente FLIT del bloqueo registral "vehículo ya matriculado" al
    /// preparar/entregar (segundo momento). SOLO Matrícula Inicial; sin VIN persistido (instancia sin
    /// consulta de vehículo aún) es no-op. Devuelve el mensaje de bloqueo, o <c>null</c> si el VIN sigue
    /// libre de una matrícula APROBADA de otro trámite.
    /// </summary>
    private async Task<string?> EvaluarEstadoVehiculoRegistralAsync(ProcedureInstance instance, CancellationToken ct)
    {
        // HU #10970 — fuera del modo block el gate no corta la transición. A diferencia del preflight,
        // aquí no hay semáforo donde dejar un warn: una transición se permite o no, así que warn y off
        // se comportan igual (no bloquear). La señal en amarillo la sigue dando el preflight.
        if (_validationPolicy.VehicleRegistrationState != TramiteValidationMode.Block)
            return null;

        if (instance.Family != ProcedureFamily.Matriculas)
            return null;

        var vin = instance.FieldValues.FirstOrDefault(f =>
            string.Equals(f.FieldKey, "vin", StringComparison.OrdinalIgnoreCase))?.ValueText;
        var vinNorm = VinNormalizer.Normalize(vin);
        if (vinNorm is null)
            return null;

        var existentes = await repo.FindTramitesByVinAsync(instance.TenantId, vinNorm, instance.Id, ct)
            .ConfigureAwait(false);
        var conflicto = VinPolicyEvaluator.EvaluarConflicto(existentes);
        if (conflicto?.Code != VinConflictCode.TramiteMatriculaCompletada)
            return null;

        return "El vehículo ya tiene una matrícula inicial APROBADA en FLIT para este VIN: no puede radicarse.";
    }

    /// <summary>
    /// Gate de prenda: (1) política compañía+OT del certificado — cualquier modalidad CON DIMENSIÓN DE
    /// PRENDA (CF-06); (2) R10 decisión de prenda — traspaso con gravámenes en warn, Y matrícula inicial
    /// de forma INCONDICIONAL (HU #11592, bloqueo duro que invierte deliberadamente la HU #10596: la
    /// prenda de matrícula dejó de ser una declaración meramente informativa).
    /// </summary>
    private async Task<(string? Code, string? Detail)> EvaluarPrendaGateAsync(
        ProcedureInstance instance,
        CancellationToken ct)
    {
        // Un trámite sin dimensión de prenda no tiene NADA que decidir sobre un gravamen: ni capa
        // complementaria (familia OTROS no acumula, ADR-0050) ni prenda propia del tipo. Se sale antes
        // que nada —incluido el override del OT, que hasta ahora se evaluaba primero y para toda
        // modalidad— porque ahí el bloqueo era INSATISFACIBLE: `RegistrarPrendaHandler` rechaza la
        // decisión de estos tipos con `prenda_no_admitida_en_tipo` y el asistente ni siquiera pinta el
        // paso, así que un duplicado de tarjeta o un cambio de color se quedaban sin poder prepararse.
        // Mismo predicado que usa ese handler para ACEPTAR: quien no puede registrar una prenda no
        // puede quedar bloqueado por no tenerla.
        var perfil = ProcedureTypeGateProfile.FromJson(instance.ProcedureType?.GateProfile);
        if (!perfil.AdmiteDimensionDePrenda(instance.ProcedureType?.Family, instance.ProcedureType?.Code))
            return (null, null);

        var docTipos = instance.Attachments.Select(a => a.Tipo).ToList();

        // La decisión vigente se carga ANTES del override: desde 2026-08-12 el override la necesita
        // para no exigir un documento que la UI no ofrece cargar (sin_prenda / omitir). Sin repo
        // cableado (tests) queda null, que el gate trata como "falta decidir" ⇒ prenda_decision_requerida.
        var prenda = _prendaRepo is null
            ? null
            : await _prendaRepo.GetVigenteAsync(instance.Id, instance.TenantId, ct).ConfigureAwait(false);

        // Compañía+OT: default exige certificado; opt-out al CreatedAt ⇒ opcional. Aplica a
        // matrícula, traspaso y cualquier otra modalidad con OT.
        var documentoExigido = await _prendaDocumentRequirementPolicy
            .IsRequiredAsync(instance.TenantId, instance.TransitOfficeId, instance.CreatedAt, ct)
            .ConfigureAwait(false);
        var otError = PrendaGate.EvaluateOtOverride(documentoExigido, prenda?.Decision, docTipos);
        if (otError is not null)
            return (otError, otError == TramiteEstadoErrores.PrendaDecisionRequerida
                ? "El organismo de tránsito exige el documento de prenda: registra la decisión de "
                  + "prenda del trámite antes de prepararlo."
                : "La compañía exige el documento de prenda para este organismo de tránsito.");

        if (_prendaRepo is null)
            return (null, null);

        var modalidad = instance.Family;

        // R10 (HU #10597) — gate del semáforo de gravámenes (decisión de prenda), solo traspaso.
        if (modalidad == ProcedureFamily.Traspaso && HasGravamenWarn(instance))
        {
            return MapPrendaGateResult(
                PrendaGate.Evaluate(esTraspaso: true, hasGravamenWarn: true, prenda, docTipos),
                prenda,
                documentoExigido,
                "El vehículo tiene gravámenes: registra una decisión de prenda antes de preparar el trámite.");
        }

        // R10 aplicado a matrícula inicial (HU #11592) — INCONDICIONAL: a diferencia del traspaso, no
        // depende de HasGravamenWarn (ese semáforo detecta gravámenes de un vehículo con historial; en
        // matrícula el vehículo es nuevo y el gravamen, si existe, se CONSTITUYE con el trámite —mismo
        // razonamiento que ya documenta EvaluateOtOverride para el override del OT). Sin decisión de
        // prenda vigente, no hay soporte del gravamen: no se puede preparar el trámite.
        if (modalidad == ProcedureFamily.Matriculas)
        {
            return MapPrendaGateResult(
                PrendaGate.EvaluateMatriculaInicial(prenda, docTipos),
                prenda,
                documentoExigido,
                "Registra la decisión de prenda antes de preparar el trámite.");
        }

        // El gravamen ES el trámite (inscribir / levantar prenda). Estos tipos caían en el `return`
        // final: el único trámite cuyo objeto es la prenda era el único SIN gate de prenda, así que
        // podía radicarse sin decisión, sin acreedor y sin certificado — y el FUR salía con la
        // casilla 11 o 12 marcada, el numeral 20 en blanco y sin bloque en el párrafo 23.
        //
        // `PrendaGate` ya tenía el núcleo preparado para esto; lo que faltaba era llamarlo.
        if (ProcedureTypeLayers.EsPrendaDeAccionUnica(instance.TypeCode))
        {
            // ADR-0055 (HU #12129) — con la acción complementaria activa puede haber hasta DOS hechos
            // vigentes (constitución + levantamiento); cada uno necesita su propio documento/acreedor
            // completos, así que el gate evalúa el CONJUNTO, no un solo `prendaVigente` (que además
            // con dos filas vigentes ya no identifica de forma determinística cuál es "la" decisión).
            var vigentes = _prendaRepo is null
                ? []
                : await _prendaRepo.GetVigentesAsync(instance.Id, instance.TenantId, ct).ConfigureAwait(false)
                    ?? [];

            var accionUnicaError = PrendaGate.EvaluateAccionUnica(vigentes, docTipos);

            // El detalle del mensaje (p. ej. "falta el acreedor") debe señalar CUÁL de los hasta dos
            // hechos lo dispara — se ubica reevaluando cada uno con la misma regla de un solo hecho.
            var prendaDelError = vigentes.FirstOrDefault(v =>
                PrendaGate.EvaluateAccionUnica(v, docTipos) == accionUnicaError);

            return MapPrendaGateResult(
                accionUnicaError,
                prendaDelError,
                // El certificado no es opcional aquí aunque el OT no lo exija por configuración: es
                // el soporte del acto que se está radicando, no un requisito añadido del organismo.
                documentoExigido: true,
                "Registra la información de la prenda antes de preparar el trámite.");
        }

        return (null, null);
    }

    /// <summary>
    /// Traduce el código de <see cref="PrendaGate"/> al par (código, detalle) del gate de preparación,
    /// compartido entre traspaso y matrícula inicial (HU #11592) para no duplicar el mapeo.
    /// </summary>
    private static (string? Code, string? Detail) MapPrendaGateResult(
        string? prendaGateCode,
        ProcedureInstancePrenda? prenda,
        bool documentoExigido,
        string mensajeDecisionRequerida) => prendaGateCode switch
        {
            TramiteEstadoErrores.PrendaDecisionRequerida =>
                (TramiteEstadoErrores.PrendaDecisionRequerida, mensajeDecisionRequerida),
            TramiteEstadoErrores.PrendaDocumentoRequerido when documentoExigido =>
                (TramiteEstadoErrores.PrendaDocumentoRequerido,
                    "La decisión de prenda seleccionada requiere adjuntar su documento de soporte."),
            TramiteEstadoErrores.PrendaAcreedorRequerido =>
                (TramiteEstadoErrores.PrendaAcreedorRequerido, DescribirAcreedorFaltante(prenda)),
            TramiteEstadoErrores.PrendaEntidadLevantamientoRequerida =>
                (TramiteEstadoErrores.PrendaEntidadLevantamientoRequerida,
                    "Indica ante qué entidad se levantó la prenda: es lo que el FUR declara en las observaciones."),
            _ => (null, null),
        };

    /// <summary>
    /// HU #11591 — arma el mensaje de <see cref="TramiteEstadoErrores.PrendaAcreedorRequerido"/>
    /// enumerando dinámicamente qué campo(s) del acreedor faltan (nombre, documento o ambos).
    /// </summary>
    private static string DescribirAcreedorFaltante(ProcedureInstancePrenda? prenda)
    {
        var faltantes = new List<string>();
        if (string.IsNullOrWhiteSpace(prenda?.AcreedorNombre))
            faltantes.Add("nombre del acreedor");
        if (string.IsNullOrWhiteSpace(prenda?.AcreedorDocumento))
            faltantes.Add("documento del acreedor");

        return "La decisión de prenda constituye un gravamen: falta diligenciar "
            + string.Join(" y ", faltantes) + ".";
    }

    /// <summary>
    /// ¿El último snapshot de preflight reporta el check <c>gravamenes</c> en <c>warn</c>/<c>fail</c>?
    /// El snapshot serializa la lista de checks (Key/Status). Parseo tolerante a Pascal/camelCase.
    /// </summary>
    private static bool HasGravamenWarn(ProcedureInstance instance)
    {
        var snapshot = instance.PreflightSnapshots
            .OrderByDescending(s => s.CreatedAt)
            .FirstOrDefault();
        if (snapshot is null || string.IsNullOrWhiteSpace(snapshot.Checks))
            return false;

        try
        {
            using var doc = JsonDocument.Parse(snapshot.Checks);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
                return false;

            foreach (var el in doc.RootElement.EnumerateArray())
            {
                if (JsonStringEquals(el, "key", "gravamenes")
                    && (JsonStringEquals(el, "status", "warn") || JsonStringEquals(el, "status", "fail")))
                    return true;
            }
        }
        catch (JsonException)
        {
            return false;
        }

        return false;
    }

    /// <summary>Compara (case-insensitive) una propiedad JSON con un valor, probando Pascal y camelCase.</summary>
    private static bool JsonStringEquals(JsonElement el, string prop, string expected)
    {
        foreach (var name in new[] { prop, char.ToUpperInvariant(prop[0]) + prop[1..] })
        {
            if (el.TryGetProperty(name, out var v)
                && v.ValueKind == JsonValueKind.String
                && string.Equals(v.GetString(), expected, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

}

/// <summary>Logging source-generated (CA1848) del ciclo de vida. Sin PII.</summary>
internal static partial class TramiteLifecycleLog
{
    [LoggerMessage(Level = LogLevel.Information,
        Message = "HU #12796 — la regeneración anticipada del consolidado {Documento} del trámite {InstanceId} (tenant {TenantId}) se descartó; lo cubre la regeneración perezosa.")]
    public static partial void RegeneracionAnticipadaDescartada(
        ILogger logger, Guid instanceId, Guid tenantId, TipoConsolidado documento);

    // HU #13144 (ADR-0066) — aviso del gate de mandatario en modo warn. SIN datos personales: solo ids y el
    // motivo del vocabulario estable (nunca documento, correo ni ruta de firma del mandatario).
    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Flit.TramiteValidations MandatarioRequerido (warn): el trámite {InstanceId} se radica sin mandatario válido. Codigo={Codigo}, Motivo={Motivo}, TransitOfficeId={TransitOfficeId}, CompanyTenantId={CompanyTenantId}.")]
    public static partial void MandatarioAviso(
        ILogger logger, Guid instanceId, string codigo, string motivo, Guid? transitOfficeId, Guid companyTenantId);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "HU #13144 — no se pudo evaluar el mandatario del trámite {InstanceId} (tenant {TenantId}); en modo warn la radicación continúa.")]
    public static partial void MandatarioEvaluacionFallida(
        ILogger logger, Guid instanceId, Guid tenantId, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Bug #13194 — no se pudo notificar la firma pendiente ({ExceptionType}) del trámite {InstanceId}, parte {Parte}; el bloqueo se mantiene.")]
    public static partial void NotificacionFirmaFallida(ILogger logger, string exceptionType, Guid instanceId, string parte);
}
