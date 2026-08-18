using System.Text.Json;
using System.Text.Json.Nodes;
using Flit.Tramites.Domain.Documents;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Enums;
using Flit.Tramites.Domain.Integration;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Enums;
using Flit.Tramites.Domain.Tramites.Estados;
using Flit.Tramites.Domain.Tramites.Services;
using Flit.Tramites.Domain.Tramites.ValueObjects;

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
    IIdentityValidationPolicy? identityPolicy = null,
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
    TramiteValidationPolicy? validationPolicy = null) : ITramiteLifecycleService
{
    // ADR-0036 (HU #10912/#10916) — config de mandato del OT (plantilla / exige a PN). Default seguro
    // (NUNCA resuelve ⇒ solo PJ, plantilla genérica) en tests que no lo ejercitan.
    private readonly IMandateRequirementPolicy _mandatePolicy = mandatePolicy ?? NullMandateRequirementPolicy.Instance;

    // ADR-0036 §D9 (HU #10916) — directorio de mandatarios del OT para resolver el firmante al aprobar.
    // Default seguro (NUNCA resuelve candidatos) en tests que no lo ejercitan.
    private readonly IMandateSignerDirectory _mandateDirectory = mandateDirectory ?? NullMandateSignerDirectory.Instance;

    // HU #10970 — modo por ambiente de CF-03 en el gate de radicación. Sin inyectar ⇒ bloqueo duro
    // (comportamiento previo a esta historia).
    private readonly TramiteValidationPolicy _validationPolicy =
        validationPolicy ?? TramiteValidationPolicy.BlockAll;

    // HU #10548 — si el OT destino deshabilita la validación de identidad, el gate no la exige.
    // Default permisivo (siempre exige) cuando no hay política cableada (tests).
    private readonly IIdentityValidationPolicy _identityPolicy =
        identityPolicy ?? NullIdentityValidationPolicy.Instance;

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

        if (!TramiteStateMachine.IsValidTransition(from, command.ToStatus))
            return TramiteTransitionOutcome.Fail(
                TramiteEstadoErrores.TransicionNoPermitida,
                $"La transición de '{from}' a '{command.ToStatus}' no está permitida.");

        // Rechazado → entregado solo con subsanación activa (flag). Sin flag no se re-radica.
        if (string.Equals(from, TramiteEstado.Rechazado, StringComparison.OrdinalIgnoreCase)
            && command.ToStatus == TramiteEstado.Entregado
            && !instance.SubsanacionActiva)
        {
            return TramiteTransitionOutcome.Fail(
                TramiteEstadoErrores.TransicionNoPermitida,
                "Solo se puede re-radicar a entregado cuando la subsanación está activa.");
        }

        // RF05 — anular/rechazar exigen motivo explícito para el historial.
        if (command.ToStatus is TramiteEstado.Anulado or TramiteEstado.Rechazado
            && string.IsNullOrWhiteSpace(command.Reason))
            return TramiteTransitionOutcome.Fail(
                TramiteEstadoErrores.MotivoRequerido,
                $"Debe indicar el motivo para pasar el trámite a '{command.ToStatus}'.");

        // Re-radicación selectiva: desde subsanación (flag sobre rechazado, o legado status
        // subsanacion) → entregado. Solo re-evalúa gates afectados por el diff del snapshot.
        var isSubsanacionReradicacion =
            TramiteEstado.EsReRadicacionSubsanacion(from, instance.SubsanacionActiva)
            && command.ToStatus == TramiteEstado.Entregado;

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
        if (command.ToStatus is TramiteEstado.Preparado or TramiteEstado.Entregado
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
                return TramiteTransitionOutcome.Fail(code, detail);
        }

        // Gates OT de entrega (heredados del submit HU #10217/#2). HU #10872 (AC1) — este es el GATE
        // FINAL de radicación: corre SIEMPRE, sin importar el diff de campos corregidos.
        if (command.ToStatus == TramiteEstado.Entregado)
        {
            var entregaError = await EvaluarEntregaAsync(instance, ct).ConfigureAwait(false);
            if (entregaError is var (code, detail) && code is not null)
                return TramiteTransitionOutcome.Fail(code, detail);
        }

        // ADR-0036 §D9 (HU #10916) — al APROBAR, resolver el mandatario que firma el mandato: automático
        // si hay uno solo o el cotejo por usuario es único; explícito (mandateSignerId) si hay varios sin
        // match ⇒ 409 mandatario_requerido. Fija instance.MandateSignerId en la MISMA unidad de trabajo;
        // la regeneración del PDF del mandato con el firmante la dispara el handler tras el commit.
        if (command.ToStatus == TramiteEstado.Aprobado)
        {
            var mandatoError = await ResolverMandatarioAlAprobarAsync(instance, command, ct).ConfigureAwait(false);
            if (mandatoError is not null)
                return TramiteTransitionOutcome.Fail(mandatoError, DetalleMandatario(mandatoError));
        }

        var now = DateTimeOffset.UtcNow;
        instance.Status = command.ToStatus;
        instance.UpdatedAt = now;
        if (command.ToStatus == TramiteEstado.Entregado)
        {
            instance.SubmittedAt = now;
            // Feature #10587 / HU #10785 — la ruta de placa NO cambia el status (queda 'entregado'):
            // fija el sub-estado interno de placa (preasignado Flujo B / asignado Flujo A / null estándar).
            // Los gates de entrega (EvaluarEntregaAsync) ya corrieron y promovieron el OT elegido.
            instance.PlateFlowStatus = command.PlateFlowStatus;

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
            command.Metadata);

        // Historial (RF05) + publicación (RNF01) se ENCOLAN en la misma unidad de trabajo;
        // el commit único de abajo los persiste o descarta en bloque.
        await recorder.RecordAsync(record, ct).ConfigureAwait(false);
        await publisher.EnqueueAsync(record, ct).ConfigureAwait(false);

        var committed = await repo.SaveChangesWithConcurrencyGuardAsync(ct).ConfigureAwait(false);
        if (!committed)
            return TramiteTransitionOutcome.Fail(
                TramiteEstadoErrores.ConflictoConcurrencia,
                "El trámite fue modificado por otro proceso. Recargue el trámite e intente de nuevo.");

        return TramiteTransitionOutcome.Ok(instance);
    }

    /// <summary>
    /// ADR-0036 §D9 (HU #10916) — resuelve el mandatario del mandato al aprobar. Devuelve el código de
    /// error (<c>mandatario_requerido</c>) si hay varios mandatarios y ninguno cotejó; <c>null</c> si no
    /// hay nada que resolver (el mandato no aplica, o el mandatario es institucional sin firmante persona)
    /// o si el firmante quedó fijado en <c>instance.MandateSignerId</c>.
    /// </summary>
    private async Task<string?> ResolverMandatarioAlAprobarAsync(
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
            return null;

        // El OT debe estar promovido (se hizo en la entrega). Sin él no podemos consultar el directorio.
        if (instance.TransitOfficeId is not { } transitOfficeId)
            return null;

        var candidates = await _mandateDirectory
            .GetCandidatesAsync(
                transitOfficeId, instance.TenantId,
                MandateSignerSelectionResolver.ResolveNitMandante(instance), ct)
            .ConfigureAwait(false);

        var resolution = MandateSignerSelector.Resolve(candidates, command.ChangedByUserId, command.MandateSignerId);

        switch (resolution.Status)
        {
            case MandateSignerResolutionStatus.Resolved:
                instance.MandateSignerId = resolution.Signer!.Id;
                return null;
            case MandateSignerResolutionStatus.RequiereSeleccion:
                return TramiteEstadoErrores.MandatarioRequerido;
            default:
                // NoConfigurado: el OT no tiene mandatarios; se aprueba sin firmante (el mandato queda con
                // placeholder hasta que el OT registre uno y se regenere). No bloquea la aprobación.
                return null;
        }
    }

    /// <summary>Detalle del error de mandatario para el mensaje al usuario (ADR-0036 §D9).</summary>
    private static string DetalleMandatario(string code) => code switch
    {
        TramiteEstadoErrores.MandatarioRequerido =>
            "Hay varios mandatarios para la compañía en este organismo y ninguno corresponde a su usuario. " +
            "Elija el mandatario que firma el mandato e intente aprobar de nuevo.",
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
        var identidadAprobada = await IdentityApprovalResolver.ResolveApprovedPartiesAsync(
            repo, instance, DateTimeOffset.UtcNow, ct, _vaultPolicy).ConfigureAwait(false);
        // HU #10548 — el OT destino puede tener la validación de identidad deshabilitada por
        // acuerdo: en ese caso se considera satisfecha para no bloquear la preparación.
        var identityRequired = await _identityPolicy.IsIdentityValidationRequiredAsync(
            instance.TenantId, TransitOfficeIdFromFieldValues(instance), ct).ConfigureAwait(false);
        if (!identityRequired)
            identidadAprobada = IdentitySatisfiedForAllParties(identidadAprobada);

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
    /// <summary>
    /// Marca la identidad de ambas partes (comprador y vendedor) como satisfecha, uniéndolas al set
    /// aprobado. Se usa cuando el OT destino deshabilita la validación de identidad (HU #10548): así
    /// el <see cref="SubmitGate"/> no exige identidad sin tocar su firma.
    /// </summary>
    private static HashSet<string> IdentitySatisfiedForAllParties(IReadOnlySet<string> approved) =>
        new(approved, StringComparer.OrdinalIgnoreCase)
        {
            BiometricRules.ParteComprador,
            BiometricRules.ParteVendedor,
        };

    private static Guid? TransitOfficeIdFromFieldValues(ProcedureInstance instance)
    {
        var raw = instance.FieldValues.FirstOrDefault(f =>
            string.Equals(f.FieldKey, "transit_office_id", StringComparison.OrdinalIgnoreCase))?.ValueText;

        return Guid.TryParse(raw, out var id) && id != Guid.Empty ? id : null;
    }

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
        var codigo = TipologiaResolver.ResolveCodigo(instance.TipologiaCodigo, instance.ModalidadEntrada);
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

        if (TramiteModalidadEntradaCodes.FromCode(instance.ModalidadEntrada) != TramiteModalidadEntrada.MatriculaInicial)
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
    /// Gate de prenda: (1) política compañía+OT del certificado — cualquier modalidad (CF-06);
    /// (2) R10 decisión de prenda — traspaso con gravámenes en warn, Y matrícula inicial de forma
    /// INCONDICIONAL (HU #11592, bloqueo duro que invierte deliberadamente la HU #10596: la prenda de
    /// matrícula dejó de ser una declaración meramente informativa).
    /// </summary>
    private async Task<(string? Code, string? Detail)> EvaluarPrendaGateAsync(
        ProcedureInstance instance,
        CancellationToken ct)
    {
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

        var modalidad = TramiteModalidadEntradaCodes.FromCode(instance.ModalidadEntrada);

        // R10 (HU #10597) — gate del semáforo de gravámenes (decisión de prenda), solo traspaso.
        if (modalidad == TramiteModalidadEntrada.Traspaso && HasGravamenWarn(instance))
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
        if (modalidad == TramiteModalidadEntrada.MatriculaInicial)
        {
            return MapPrendaGateResult(
                PrendaGate.EvaluateMatriculaInicial(prenda, docTipos),
                prenda,
                documentoExigido,
                "Registra la decisión de prenda antes de preparar el trámite.");
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
