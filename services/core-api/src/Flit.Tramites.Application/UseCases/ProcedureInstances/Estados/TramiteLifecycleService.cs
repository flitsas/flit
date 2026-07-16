using System.Text.Json;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Enums;
using Flit.Tramites.Domain.Integration;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Enums;
using Flit.Tramites.Domain.Tramites.Estados;
using Flit.Tramites.Domain.Tramites.Services;

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
    ChecklistMatrixCompleteness? matrixCompleteness = null) : ITramiteLifecycleService
{
    // HU #10548 — si el OT destino deshabilita la validación de identidad, el gate no la exige.
    // Default permisivo (siempre exige) cuando no hay política cableada (tests).
    private readonly IIdentityValidationPolicy _identityPolicy =
        identityPolicy ?? NullIdentityValidationPolicy.Instance;

    // R10 (HU #10597) — repo de prenda para el gate de traspaso. Null en tests que no lo ejercitan
    // (el gate se omite de forma segura); en producción lo inyecta el contenedor.
    private readonly IProcedureInstancePrendaRepository? _prendaRepo = prendaRepo;

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

        // RF05 — anular/rechazar exigen motivo explícito para el historial.
        if (command.ToStatus is TramiteEstado.Anulado or TramiteEstado.Rechazado
            && string.IsNullOrWhiteSpace(command.Reason))
            return TramiteTransitionOutcome.Fail(
                TramiteEstadoErrores.MotivoRequerido,
                $"Debe indicar el motivo para pasar el trámite a '{command.ToStatus}'.");

        // RF03 — gate borrador→preparado: identidad aprobada/vigente + documentos obligatorios.
        if (from == TramiteEstado.Borrador && command.ToStatus == TramiteEstado.Preparado)
        {
            // Identidad PER-PERSONA (documento del actor), referenciada de su validación vigente
            // (HU #10350 rediseño #87): fila propia del trámite O identidad vigente de la persona
            // en otro trámite del tenant, sin clonar.
            var identidadAprobada = await IdentityApprovalResolver.ResolveApprovedPartiesAsync(
                repo, instance, DateTimeOffset.UtcNow, ct).ConfigureAwait(false);
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
            var gateErrors = SubmitGate.Evaluate(instance, identidadAprobada, docsCompletos);
            if (gateErrors.Count > 0)
                return TramiteTransitionOutcome.Fail(gateErrors[0], DetalleGatePreparacion(gateErrors[0]));

            // R10 (HU #10597) — gate de prenda del traspaso: con gravámenes en warn se exige una
            // decisión de prenda vigente (y su documento cuando la decisión lo requiere). "omitir" es
            // la vía "asumo el riesgo" (decisión válida sin documento). Solo con el repo cableado.
            var prendaError = await EvaluarPrendaGateAsync(instance, ct).ConfigureAwait(false);
            if (prendaError is var (prendaCode, prendaDetail) && prendaCode is not null)
                return TramiteTransitionOutcome.Fail(prendaCode, prendaDetail);
        }

        // Gates OT de entrega (heredados del submit HU #10217/#2).
        if (command.ToStatus == TramiteEstado.Entregado)
        {
            var entregaError = await EvaluarEntregaAsync(instance, ct).ConfigureAwait(false);
            if (entregaError is var (code, detail) && code is not null)
                return TramiteTransitionOutcome.Fail(code, detail);
        }

        var now = DateTimeOffset.UtcNow;
        instance.Status = command.ToStatus;
        instance.UpdatedAt = now;
        if (command.ToStatus == TramiteEstado.Entregado)
            instance.SubmittedAt = now;

        // Feature #10701 — un cambio de estado invalida el consolidado maestro persistido: el
        // expediente cambió, así que el próximo "Ver consolidado" debe regenerarlo antes de mostrarlo.
        instance.ConsolidadoMaestroVigente = false;

        var record = new TramiteTransitionRecord(
            command.TenantId,
            instance.Id,
            from,
            command.ToStatus,
            command.Reason,
            command.ChangedByUserId,
            now);

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

    /// <summary>Causa exacta del gate de preparación (RF03) para el mensaje al usuario.</summary>
    private static string DetalleGatePreparacion(string code) => code switch
    {
        TramiteEstadoErrores.IdentidadNoAprobada =>
            "La validación de identidad del comprador no está aprobada o no está vigente.",
        TramiteEstadoErrores.DocumentosIncompletos =>
            "Faltan documentos obligatorios del checklist para preparar el trámite.",
        _ => "El trámite no cumple los requisitos para prepararse.",
    };

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
    /// R10 (HU #10597) — gate de prenda del traspaso. Solo aplica a traspaso con el semáforo de
    /// gravámenes en <c>warn</c>: exige una decisión de prenda vigente y, si la decisión requiere
    /// documento, su adjunto. <c>(null, null)</c> = puede prepararse. Se omite si no hay repo cableado.
    /// </summary>
    private async Task<(string? Code, string? Detail)> EvaluarPrendaGateAsync(
        ProcedureInstance instance,
        CancellationToken ct)
    {
        if (_prendaRepo is null)
            return (null, null);

        var esTraspaso = TramiteModalidadEntradaCodes.FromCode(instance.ModalidadEntrada) == TramiteModalidadEntrada.Traspaso;
        if (!esTraspaso || !HasGravamenWarn(instance))
            return (null, null);

        var prenda = await _prendaRepo.GetVigenteAsync(instance.Id, instance.TenantId, ct).ConfigureAwait(false);
        var docTipos = instance.Attachments.Select(a => a.Tipo).ToList();

        return PrendaGate.Evaluate(esTraspaso: true, hasGravamenWarn: true, prenda, docTipos) switch
        {
            TramiteEstadoErrores.PrendaDecisionRequerida => (TramiteEstadoErrores.PrendaDecisionRequerida,
                "El vehículo tiene gravámenes: registra una decisión de prenda antes de preparar el trámite."),
            TramiteEstadoErrores.PrendaDocumentoRequerido => (TramiteEstadoErrores.PrendaDocumentoRequerido,
                "La decisión de prenda seleccionada requiere adjuntar su documento de soporte."),
            _ => (null, null),
        };
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
