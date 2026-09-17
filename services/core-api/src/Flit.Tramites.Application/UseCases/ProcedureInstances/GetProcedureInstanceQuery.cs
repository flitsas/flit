using System.Text.Json;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.RevocationRequests;
using Flit.Tramites.Domain.Tramites.Enums;
using Flit.Tramites.Domain.Tramites.Estados;
using Flit.Tramites.Domain.Tramites.ValueObjects;

namespace Flit.Tramites.Application.UseCases.ProcedureInstances;

public sealed record ProcedureInstanceFieldValueDto(
    Guid? FormFieldId,
    string FieldKey,
    string? ValueText,
    string? ValueJson,
    string Source);

public sealed record ProcedureInstanceStatusHistoryDto(
    string? FromStatus,
    string ToStatus,
    DateTimeOffset ChangedAt,
    string? Reason,
    // HU #10871 — checklist HÍBRIDO de la observación de subsanación (motivo + items), RECORTADO del
    // metadata jsonb que persiste OtClientProcedureRepository.BuildStatusHistoryMetadata (HU #10871/
    // #10872): NUNCA se expone fieldSnapshot ni ot_tenant_id/approver_tenant_id al cliente (plomería
    // interna / posible PII del snapshot de field_values). Serializado como JSON string
    // `{"motivo":...,"items":[{"campo":...,"detalle":...}]}` — mismo shape que
    // `frontend/lib/tramites/subsanacion.ts` (`parseSubsanacionObservation`) espera en `metadata`. Null
    // si la entrada no trae observación (transición sin checklist, p. ej. aprobar/rechazar).
    string? Metadata = null,
    // Bug #12526 — quién ejecutó la transición: nombre, correo y compañía (mismo criterio de resolución
    // que GetStatusHistoryHandler/HU #12184). Null cuando fue un proceso automático o el usuario ya no
    // existe/no tiene esos datos — la tarjeta cae al guion en vez de inventarlos.
    string? ChangedByName = null,
    string? ChangedByEmail = null,
    string? ChangedByCompania = null);

/// <summary>
/// Bug #12376, defectos 3/4 — evento administrativo del historial del trámite (bitácora
/// <c>ProcedureInstanceEvent</c>). Expone SOLO los tipos que el dashboard necesita mostrar en el
/// tracking (reenvío de validación, reasignación de gestor y, desde HU #12575, solicitud de
/// revocatoria): los demás tipos (<c>anular_admin</c>, <c>cambio_estado</c>, …) ya están cubiertos por
/// <see cref="ProcedureInstanceStatusHistoryDto"/> y se omiten aquí para no duplicar el timeline. Los
/// campos específicos de cada tipo quedan null en el otro (contrato "ancho", más simple para el
/// frontend que un payload JSON opaco).
/// </summary>
public sealed record ProcedureInstanceEventDto(
    string Tipo,
    DateTimeOffset CreatedAt,
    string? CreatedByName,
    // reasignar_gestor_admin (HU #12162)
    string? PreviousAssignedToName = null,
    string? NewAssignedToName = null,
    // reenvio_validacion_admin (HU #12161)
    string? PartyRole = null,
    bool? EmailActualizado = null,
    // Correo en claro (a pedido del producto) — el admin necesita ver la dirección exacta a la que se
    // reenvió, no una versión enmascarada.
    string? CorreoDestino = null,
    // Correo/compañía del gestor NUEVO — misma persona que ya nombra NewAssignedToName, para que la
    // tarjeta de "Reasignación de gestor" no deje Correo/Empresa en blanco (mismo hallazgo del Bug
    // #12526, aquí para el evento de reasignación en vez del historial de estados).
    string? NewAssignedToEmail = null,
    string? NewAssignedToCompania = null,
    // Compañía de quien EJECUTÓ el evento (ya se nombra en Rol: "Ejecutado por X") — completa el campo
    // Empresa en reenvio_validacion_admin, donde no hay un "gestor" propio del evento.
    string? CreatedByCompania = null,
    // revocatoria_solicitada (HU #12575, Feature #12565) — payload que ya emite RequestRevocationHandler
    // (HU #12572): número de intento y motivo escrito por el Administrador. `support_document_id` no se
    // expone aquí (el expediente ya lista sus adjuntos por su propio endpoint; no hace falta duplicarlo
    // en el timeline).
    int? RevocationAttemptNumber = null,
    string? RevocationReason = null,
    // Correo de quien EJECUTÓ el evento — a diferencia de reasignar_gestor_admin/reenvio_validacion_admin
    // (que hablan de un TERCERO), en revocatoria_solicitada el ejecutor ES el dato relevante de "Correo"
    // de la tarjeta (ya resuelto en el mismo batch de GetUserEmailsAsync que arma NewAssignedToEmail).
    string? CreatedByEmail = null,
    // revocatoria_aprobada / revocatoria_rechazada (HU #12576/#12577, Feature #12565) — motivo de la
    // DECISIÓN del OT, distinto de RevocationReason (que es el motivo original del gestor al pedirla).
    // Ambos tipos reutilizan RevocationAttemptNumber de arriba.
    string? RevocationDecisionReason = null);

/// <summary>
/// HU #12575 (Feature #12565, AC1) — sub-estado ACTIVO de revocatoria sobre un trámite
/// <see cref="TramiteEstado.Aprobado"/>, ORTOGONAL al <see cref="ProcedureInstanceDetailDto.Status"/>
/// (que permanece 'aprobado' durante todo el sub-flujo, ADR-0022): mismo precedente que
/// <see cref="ProcedureInstanceDetailDto.PlateFlowStatus"/> para la ruta de placa. Resuelto con
/// <see cref="IProcedureRevocationRequestRepository.FindActiveAsync"/> (HU #12571) en
/// vez de que el frontend infiera el sub-estado parseando el evento <c>revocatoria_solicitada</c> del
/// timeline — ese evento registra CADA intento (bitácora inmutable); este campo dice cuál, si alguno,
/// sigue ACTIVO ahora mismo. <c>null</c> = no hay solicitud activa (nunca se pidió, o la última ya se
/// decidió — decisión que resuelve HU #12576, todavía no implementada).
/// </summary>
public sealed record ProcedureInstanceActiveRevocationRequestDto(
    /// <summary><c>solicitada</c> | <c>en_revision</c> — los dos únicos valores que
    /// <see cref="ProcedureRevocationRequestStatus.EsActivo"/> considera
    /// ACTIVOS (HU #12571); una fila <c>aprobada</c>/<c>rechazada</c> ya no es "activa" y por tanto
    /// nunca llega aquí (ver XML doc de la clase).</summary>
    string Status,
    int AttemptNumber,
    DateTimeOffset RequestedAt);

/// <summary>
/// Feature #12565 — decisión del OT (HU #12576/#12577) sobre el intento MÁS RECIENTE de revocatoria,
/// para que el detalle del trámite (gestor y OT) muestre qué pasó incluso después de que el sub-flujo
/// ya se cerró. <c>null</c> cuando nunca se solicitó una revocatoria o cuando la solicitud sigue activa
/// (ese caso lo cubre <see cref="ProcedureInstanceActiveRevocationRequestDto"/>, no este).
/// </summary>
public sealed record ProcedureInstanceRevocationDecisionDto(
    /// <summary><c>aprobada</c> | <c>rechazada</c> — los únicos dos valores decididos.</summary>
    string Status,
    int AttemptNumber,
    DateTimeOffset RequestedAt,
    DateTimeOffset DecidedAt,
    /// <summary>Motivo con el que el gestor pidió la revocatoria (HU #12572, AC1).</summary>
    string? Reason,
    /// <summary>Motivo de la decisión del OT: obligatorio al rechazar, opcional al aprobar (HU #12577).</summary>
    string? DecisionReason);

/// <summary>
/// HU #12573 (Feature #12565) — gates de habilitación del botón "Solicitar revocatoria" (AC1-AC3) que el
/// frontend NO puede evaluar sin duplicar reglas de negocio ya resueltas por el dominio: fuente FLIT
/// (<see cref="TramiteFuente"/>, HU #11056) y ventana en días hábiles (<see cref="IBusinessDayCalculator"/>,
/// misma base que <see cref="RevocationRequestGate"/> de HU #12571/#12572). Solo se calcula sobre un
/// trámite <see cref="TramiteEstado.Aprobado"/> (único estado donde la acción aplica); en cualquier otro
/// caso este campo es <c>null</c> y el frontend NO ofrece la habilitación (fuera de alcance de esta HU:
/// el rol Administrador/Operario se resuelve en el cliente con el claim de rol del JWT, no aquí — ese dato
/// no pertenece al trámite).
/// </summary>
public sealed record ProcedureInstanceRevocationEligibilityDto(
    /// <summary>AC1/AC3 — el trámite fue creado en FLIT (fuente "dashboard"): ni integración ICT
    /// (<c>origin='ict'</c>) ni foto migrada de V1 (<c>is_migrated</c>).</summary>
    bool SourceSupported,
    /// <summary>Fecha límite de la ventana (AC1/AC3), ya calculada en días hábiles desde la aprobación
    /// ORIGINAL del trámite. <c>null</c> = sin ventana configurada para el OT = sin límite.</summary>
    DateTimeOffset? WindowExpiresAt,
    /// <summary>AC3 — <c>true</c> si hay ventana configurada y ya venció (motivo "Ventana de revocatoria
    /// vencida" en la UI).</summary>
    bool WindowExpired);

public sealed record ProcedureInstanceActorDto(
    string ActorType,
    string DocumentType,
    string DocumentNumber,
    string FullName,
    // HU #11014 — correo del actor para el expediente: cuando la identidad está apalancada o cubierta por
    // el baúl no hay validación propia de la que leerlo. PII (Ley 1581): solo en respuestas autenticadas.
    string? Email = null);

public sealed record ProcedureInstanceDetailDto(
    Guid Id,
    string ReferenceNumber,
    string Status,
    Guid ProcedureTypeId,
    Guid TenantId,
    DateTimeOffset CreatedAt,
    DateTimeOffset? SubmittedAt,
    DateTimeOffset? CompletedAt,
    IReadOnlyList<ProcedureInstanceFieldValueDto> FieldValues,
    IReadOnlyList<ProcedureInstanceStatusHistoryDto> StatusHistory,
    IReadOnlyList<ProcedureInstanceActorDto> Actors,
    // HU #10349/#10350 — marca de borrador finalizado (datos completos a la espera de la
    // validación de identidad async). Null mientras el borrador no se ha finalizado. El
    // frontend lo usa para el modo "readOnly parcial" del wizard (datos bloqueados, identidad
    // operable). Opcional (default null) para compat con consumidores que no lo lean.
    DateTimeOffset? DraftFinalizedAt = null,
    // HU #10879 — paso actual persistido del wizard (Key del paso). Prima como punto de retoma al
    // reabrir el borrador (AC2); null = el frontend cae al paso derivado de los gates. Opcional (default null).
    string? CurrentStep = null,
    /// <summary>Subsanación activa sobre rechazado (edición sin cambiar status).</summary>
    bool SubsanacionActiva = false,
    /// <summary>Veces que se activó la subsanación en este expediente.</summary>
    int SubsanacionCount = 0,
    // HU #10536 — marca de prioridad. Vive en una columna del expediente, no en FieldValues, así que
    // el detalle era la única lectura del wizard que NO la traía: el paso 1 pinta el interruptor de
    // "Trámite prioritario" y, al volver sobre un trámite ya creado, no tenía de dónde rehidratarlo.
    // Opcional (default false) para no romper a los consumidores que no la lean.
    bool Prioritario = false,
    // Bug #12376, defectos 3/4 — eventos administrativos (reenvío de validación, reasignación de
    // gestor) para el tracking del dashboard. Opcional (default vacío) para no romper consumidores
    // existentes del contrato.
    IReadOnlyList<ProcedureInstanceEventDto>? Events = null,
    // HU #12573 — gates del botón "Solicitar revocatoria" (ver XML doc de
    // ProcedureInstanceRevocationEligibilityDto). Null fuera de 'aprobado' o si el trámite se ve desde
    // la vista de red (NetworkGetProcedureInstanceHandler no la calcula a propósito: la revocatoria es
    // una acción del Administrador sobre SU PROPIO trámite, no de la cabeza de grupo en modo consulta).
    ProcedureInstanceRevocationEligibilityDto? RevocationEligibility = null,
    // HU #12575 — ver XML doc de ProcedureInstanceActiveRevocationRequestDto. MISMA excepción que
    // RevocationEligibility: null en la vista de red (NetworkGetProcedureInstanceHandler no la calcula).
    ProcedureInstanceActiveRevocationRequestDto? ActiveRevocationRequest = null,
    // Feature #12565 — ver XML doc de ProcedureInstanceRevocationDecisionDto. MISMA excepción que los
    // dos campos de arriba: null en la vista de red.
    ProcedureInstanceRevocationDecisionDto? LastRevocationDecision = null,
    /// <summary>ADR-0059 — estado desde el que el OT rechazó por última vez (entregado | preasignacion); null si no aplica.</summary>
    string? RejectedFrom = null);

public sealed class GetProcedureInstanceHandler(
    IProcedureInstanceRepository repo,
    // Opcionales (default null) para no romper la construcción manual `new GetProcedureInstanceHandler(repo)`
    // que ya usan varios tests existentes (Integration.Tests, Application.Tests): sin ellos, el handler
    // sigue funcionando igual, solo que RevocationEligibility queda null (degradación segura).
    IProcedureRevocationRequestRepository? revocationRepo = null,
    IBusinessDayCalculator? businessDayCalculator = null)
{
    /// <summary>Tipos de <see cref="ProcedureInstanceEvent"/> que el dashboard muestra en el tracking
    /// (ver XML doc de <see cref="ProcedureInstanceEventDto"/> sobre por qué solo estos tres).</summary>
    private static readonly HashSet<string> RelevantEventTypes = new(StringComparer.Ordinal)
    {
        "reenvio_validacion_admin",
        "reasignar_gestor_admin",
        // HU #12575 — evento que ya emite RequestRevocationHandler.EventoTipo (HU #12572); estaba
        // fuera del whitelist y se descartaba aquí antes de llegar al frontend.
        "revocatoria_solicitada",
        // Feature #12565 — decisión del OT (DecideRevocationRequestHandler, HU #12576/#12577): sin
        // esto, un rechazo no dejaba NINGÚN rastro en el tracking (el trámite seguía Aprobado como si
        // nunca se hubiera solicitado) y una aprobación solo se veía como la transición genérica
        // "Revocado desde Aprobado" del historial de estados, sin el motivo del OT.
        "revocatoria_aprobada",
        "revocatoria_rechazada",
    };

    public async Task<(ProcedureInstanceDetailDto? Result, string? Error)> HandleAsync(
        Guid id,
        Guid tenantId,
        CancellationToken ct = default)
    {
        var instance = await repo.GetByIdWithDetailsAsync(id, tenantId, ct);
        if (instance is null)
            return (null, "not_found");

        var revocationEligibility = await BuildRevocationEligibilityAsync(instance, tenantId, ct).ConfigureAwait(false);
        var activeRevocationRequest = await BuildActiveRevocationRequestAsync(instance, tenantId, ct).ConfigureAwait(false);
        var lastRevocationDecision = await BuildLastRevocationDecisionAsync(instance, tenantId, ct).ConfigureAwait(false);
        return (await BuildDetailAsync(
            repo, instance, ct, revocationEligibility, activeRevocationRequest, lastRevocationDecision).ConfigureAwait(false), null);
    }

    /// <summary>
    /// HU #12573 — ver XML doc de <see cref="ProcedureInstanceRevocationEligibilityDto"/>. PRIVADO de este
    /// handler (no de <see cref="BuildDetailAsync"/>) a propósito: <see cref="NetworkGetProcedureInstanceHandler"/>
    /// nunca lo invoca, así que la vista de red nunca trae este campo (ver comentario en el DTO).
    /// </summary>
    private async Task<ProcedureInstanceRevocationEligibilityDto?> BuildRevocationEligibilityAsync(
        ProcedureInstance instance, Guid tenantId, CancellationToken ct)
    {
        if (revocationRepo is null || businessDayCalculator is null)
            return null;
        if (!string.Equals(instance.Status, TramiteEstado.Aprobado, StringComparison.Ordinal))
            return null;

        // Misma derivación que TramiteFuente (HU #11056) y RevocationRequestGate (HU #12571) — AC1/AC3.
        var sourceSupported = string.Equals(
            TramiteFuente.Desde(instance.Origin, instance.IsMigrated),
            TramiteFuente.Dashboard,
            StringComparison.Ordinal);

        // Misma base fija que RequestRevocationHandler (AC2/AC5 de HU #12571/#12572): la aprobación
        // ORIGINAL, no la más reciente; un reintento tras un rechazo no reinicia la ventana.
        var approvedAt = await revocationRepo.GetFirstApprovedAtAsync(tenantId, instance.Id, ct).ConfigureAwait(false)
            ?? instance.UpdatedAt ?? instance.CreatedAt;
        var windowDays = instance.TransitOfficeId is Guid transitOfficeId
            ? await revocationRepo.GetRevocationWindowBusinessDaysAsync(transitOfficeId, ct).ConfigureAwait(false)
            : null;

        DateTimeOffset? windowExpiresAt = windowDays is { } dias
            ? businessDayCalculator.AddBusinessDays(approvedAt, dias)
            : null;
        var windowExpired = windowExpiresAt is { } vence && DateTimeOffset.UtcNow > vence;

        return new ProcedureInstanceRevocationEligibilityDto(sourceSupported, windowExpiresAt, windowExpired);
    }

    /// <summary>
    /// HU #12575 — ver XML doc de <see cref="ProcedureInstanceActiveRevocationRequestDto"/>. PRIVADO de
    /// este handler por la MISMA razón que <see cref="BuildRevocationEligibilityAsync"/>: la vista de red
    /// (<see cref="NetworkGetProcedureInstanceHandler"/>) nunca la invoca.
    /// </summary>
    private async Task<ProcedureInstanceActiveRevocationRequestDto?> BuildActiveRevocationRequestAsync(
        ProcedureInstance instance, Guid tenantId, CancellationToken ct)
    {
        if (revocationRepo is null)
            return null;
        // La solicitud de revocatoria solo existe sobre trámites Aprobado (ADR-0022: el sub-flujo entero
        // corre con el trámite en ese estado); fuera de él no tiene sentido ni consultarla.
        if (!string.Equals(instance.Status, TramiteEstado.Aprobado, StringComparison.Ordinal))
            return null;

        var active = await revocationRepo.FindActiveAsync(tenantId, instance.Id, ct).ConfigureAwait(false);
        if (active is null)
            return null;

        return new ProcedureInstanceActiveRevocationRequestDto(active.Status, active.AttemptNumber, active.RequestedAt);
    }

    /// <summary>
    /// Feature #12565 — ver XML doc de <see cref="ProcedureInstanceRevocationDecisionDto"/>. A diferencia
    /// de <see cref="BuildActiveRevocationRequestAsync"/>, SIN el gate por <c>instance.Status == 'aprobado'</c>:
    /// una revocatoria APROBADA deja el trámite en <c>'revocado'</c> (ya no 'aprobado'), así que ese gate
    /// escondería justo el caso que este campo existe para mostrar. Usa <c>FindLastAsync</c> (cualquier
    /// estado, la más reciente) y solo expone la fila si YA se decidió — si sigue activa, el campo de
    /// arriba es el que corresponde.
    /// </summary>
    private async Task<ProcedureInstanceRevocationDecisionDto?> BuildLastRevocationDecisionAsync(
        ProcedureInstance instance, Guid tenantId, CancellationToken ct)
    {
        if (revocationRepo is null)
            return null;

        var last = await revocationRepo.FindLastAsync(tenantId, instance.Id, ct).ConfigureAwait(false);
        if (last is null || last.DecidedAt is null || (
                last.Status != ProcedureRevocationRequestStatus.Aprobada
                && last.Status != ProcedureRevocationRequestStatus.Rechazada))
            return null;

        return new ProcedureInstanceRevocationDecisionDto(
            last.Status, last.AttemptNumber, last.RequestedAt, last.DecidedAt.Value, last.Reason, last.DecisionReason);
    }

    /// <summary>
    /// ÚNICO punto que arma el <see cref="ProcedureInstanceDetailDto"/> a partir del agregado cargado:
    /// lo usan el detalle propio (este handler) y el consolidado de la red
    /// (<c>NetworkGetProcedureInstanceHandler</c>, HU #12358), cuyo contrato es «los MISMOS campos que el
    /// detalle propio». Toda resolución adicional que enriquezca el detalle (eventos administrativos,
    /// autores del historial, …) va AQUÍ y nunca solo en <see cref="HandleAsync"/>: si se añade en el
    /// handler propio y no aquí, la vista de red deja de ser equivalente sin que compile nada distinto
    /// (fallo de CI del PR #370 al integrar el Bug #12526).
    /// <para>
    /// Excepción deliberada (HU #12573): <paramref name="revocationEligibility"/> NO se calcula aquí
    /// adentro, sino que lo resuelve el caller (solo <see cref="HandleAsync"/>) y se recibe ya armado —
    /// la revocatoria es una acción del Administrador sobre SU PROPIO trámite; en la vista de red
    /// (<c>NetworkGetProcedureInstanceHandler</c>) nadie la invoca, así que ese campo queda <c>null</c>
    /// sin necesidad de una query cross-tenant adicional que ahí no hace falta.
    /// </para>
    /// </summary>
    internal static async Task<ProcedureInstanceDetailDto> BuildDetailAsync(
        IProcedureInstanceRepository repo,
        ProcedureInstance instance,
        CancellationToken ct,
        ProcedureInstanceRevocationEligibilityDto? revocationEligibility = null,
        // HU #12575 — misma excepción deliberada que revocationEligibility (ver comentario de clase):
        // solo HandleAsync la calcula; la vista de red la recibe null.
        ProcedureInstanceActiveRevocationRequestDto? activeRevocationRequest = null,
        // Feature #12565 — misma excepción deliberada que los dos campos de arriba.
        ProcedureInstanceRevocationDecisionDto? lastRevocationDecision = null)
    {
        var events = await BuildEventsAsync(repo, instance.Events, ct).ConfigureAwait(false);
        var actorInfo = await BuildStatusHistoryActorInfoAsync(repo, instance.StatusHistory, ct).ConfigureAwait(false);
        return ToDetail(instance, events, actorInfo, revocationEligibility, activeRevocationRequest, lastRevocationDecision);
    }

    /// <summary>
    /// Bug #12526 — nombre/correo/compañía de quien ejecutó cada transición de la Línea de tiempo, en
    /// TRES consultas batch para toda la página (no una por fila): mismo criterio que
    /// <see cref="BuildEventsAsync"/> y que <c>GetStatusHistoryHandler</c> (HU #12184).
    /// </summary>
    private static async Task<StatusHistoryActorInfo> BuildStatusHistoryActorInfoAsync(
        IProcedureInstanceRepository repo,
        IEnumerable<ProcedureInstanceStatusHistory> history,
        CancellationToken ct)
    {
        var userIds = history
            .Where(h => h.ChangedBy is not null)
            .Select(h => h.ChangedBy!.Value)
            .Distinct()
            .ToList();
        if (userIds.Count == 0)
            return new StatusHistoryActorInfo(
                new Dictionary<Guid, string>(), new Dictionary<Guid, string>(), new Dictionary<Guid, string>());

        var names = await repo.GetUserDisplayNamesAsync(userIds, ct).ConfigureAwait(false);
        var emails = await repo.GetUserEmailsAsync(userIds, ct).ConfigureAwait(false);
        var companias = await repo.GetUserCompaniasAsync(userIds, ct).ConfigureAwait(false);
        return new StatusHistoryActorInfo(names, emails, companias);
    }

    internal sealed record StatusHistoryActorInfo(
        IReadOnlyDictionary<Guid, string> Names,
        IReadOnlyDictionary<Guid, string> Emails,
        IReadOnlyDictionary<Guid, string> Companias);

    /// <summary>
    /// Bug #12376, defectos 3/4 — resuelve los eventos administrativos relevantes a DTOs "anchos", con
    /// los nombres de usuario (ejecutor + gestor origen/destino) resueltos en UNA sola consulta batch
    /// (mismo criterio que la columna "Gestor" del listado, <see cref="IProcedureInstanceRepository
    /// .GetUserDisplayNamesAsync"/>) en vez de una consulta por evento.
    /// </summary>
    internal static async Task<IReadOnlyList<ProcedureInstanceEventDto>> BuildEventsAsync(
        IProcedureInstanceRepository repo, IEnumerable<ProcedureInstanceEvent> events, CancellationToken ct)
    {
        var relevant = events
            .Where(e => RelevantEventTypes.Contains(e.Tipo))
            .OrderBy(e => e.CreatedAt)
            .ToList();
        if (relevant.Count == 0)
            return [];

        var parsed = new List<(ProcedureInstanceEvent Event, JsonElement Payload)>(relevant.Count);
        var userIds = new HashSet<Guid>();
        foreach (var e in relevant)
        {
            if (e.CreatedBy is { } createdBy)
                userIds.Add(createdBy);

            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(e.Payload) ? "{}" : e.Payload);
            var root = doc.RootElement.Clone();
            if (e.Tipo == "reasignar_gestor_admin")
            {
                if (TryGetGuid(root, "previous_assigned_to_user_id", out var prev))
                    userIds.Add(prev);
                if (TryGetGuid(root, "new_assigned_to_user_id", out var next))
                    userIds.Add(next);
            }
            parsed.Add((e, root));
        }

        var names = await repo.GetUserDisplayNamesAsync(userIds, ct).ConfigureAwait(false);
        // Hallazgo posterior al Bug #12526: el mismo vacío de Correo/Empresa ocurría en las tarjetas
        // de evento administrativo (Reasignación de gestor, Reenvío de validación), que viven en esta
        // función en vez de en BuildStatusHistoryActorInfoAsync — se resuelve con los mismos métodos.
        var emails = await repo.GetUserEmailsAsync(userIds, ct).ConfigureAwait(false);
        var companias = await repo.GetUserCompaniasAsync(userIds, ct).ConfigureAwait(false);

        return parsed.Select(p =>
        {
            var (e, payload) = p;
            var createdByName = e.CreatedBy is { } createdBy && names.TryGetValue(createdBy, out var cn) ? cn : null;
            var createdByCompania = e.CreatedBy is { } createdByForCompania
                && companias.TryGetValue(createdByForCompania, out var ccia) ? ccia : null;

            if (e.Tipo == "reasignar_gestor_admin")
            {
                var previousName = TryGetGuid(payload, "previous_assigned_to_user_id", out var prev)
                    && names.TryGetValue(prev, out var pn) ? pn : null;
                var hasNew = TryGetGuid(payload, "new_assigned_to_user_id", out var next);
                var newName = hasNew && names.TryGetValue(next, out var nn) ? nn : null;
                var newEmail = hasNew && emails.TryGetValue(next, out var ne) ? ne : null;
                var newCompania = hasNew && companias.TryGetValue(next, out var ncia) ? ncia : null;
                return new ProcedureInstanceEventDto(
                    e.Tipo, e.CreatedAt, createdByName,
                    PreviousAssignedToName: previousName,
                    NewAssignedToName: newName,
                    NewAssignedToEmail: newEmail,
                    NewAssignedToCompania: newCompania);
            }

            // revocatoria_solicitada (HU #12575) — payload de RequestRevocationHandler.EventoTipo (HU
            // #12572): { revocation_request_id, attempt_number, reason, support_document_id }. El
            // ejecutor (CreatedBy) ES el Administrador que solicitó, así que su correo es el dato
            // relevante de "Correo" en la tarjeta (a diferencia de reasignar_gestor_admin, que habla de
            // un TERCERO).
            if (e.Tipo == "revocatoria_solicitada")
            {
                var attemptNumber = payload.TryGetProperty("attempt_number", out var an)
                    && an.ValueKind == JsonValueKind.Number && an.TryGetInt32(out var attemptValue)
                        ? attemptValue
                        : (int?)null;
                var reason = payload.TryGetProperty("reason", out var rs) && rs.ValueKind == JsonValueKind.String
                    ? rs.GetString()
                    : null;
                var createdByEmail = e.CreatedBy is { } createdByForEmail
                    && emails.TryGetValue(createdByForEmail, out var em) ? em : null;
                return new ProcedureInstanceEventDto(
                    e.Tipo, e.CreatedAt, createdByName,
                    RevocationAttemptNumber: attemptNumber,
                    RevocationReason: reason,
                    CreatedByEmail: createdByEmail,
                    CreatedByCompania: createdByCompania);
            }

            // revocatoria_aprobada / revocatoria_rechazada (Feature #12565) — payload de
            // DecideRevocationRequestHandler: { revocation_request_id, attempt_number,
            // decision_reason }. El ejecutor (CreatedBy) es el usuario del OT que decidió.
            if (e.Tipo == "revocatoria_aprobada" || e.Tipo == "revocatoria_rechazada")
            {
                var decisionAttemptNumber = payload.TryGetProperty("attempt_number", out var dan)
                    && dan.ValueKind == JsonValueKind.Number && dan.TryGetInt32(out var decisionAttemptValue)
                        ? decisionAttemptValue
                        : (int?)null;
                var decisionReason = payload.TryGetProperty("decision_reason", out var dr) && dr.ValueKind == JsonValueKind.String
                    ? dr.GetString()
                    : null;
                var decidedByEmail = e.CreatedBy is { } decidedBy
                    && emails.TryGetValue(decidedBy, out var dem) ? dem : null;
                return new ProcedureInstanceEventDto(
                    e.Tipo, e.CreatedAt, createdByName,
                    RevocationAttemptNumber: decisionAttemptNumber,
                    RevocationDecisionReason: decisionReason,
                    CreatedByEmail: decidedByEmail,
                    CreatedByCompania: createdByCompania);
            }

            // reenvio_validacion_admin
            var partyRole = payload.TryGetProperty("party_role", out var pr) && pr.ValueKind == JsonValueKind.String
                ? pr.GetString()
                : null;
            var emailActualizado = payload.TryGetProperty("email_actualizado", out var ea)
                && ea.ValueKind is JsonValueKind.True or JsonValueKind.False
                    ? ea.GetBoolean()
                    : (bool?)null;
            var correo = payload.TryGetProperty("correo_destino", out var cd) && cd.ValueKind == JsonValueKind.String
                ? cd.GetString()
                : null;
            return new ProcedureInstanceEventDto(
                e.Tipo, e.CreatedAt, createdByName,
                PartyRole: partyRole,
                EmailActualizado: emailActualizado,
                CorreoDestino: correo,
                CreatedByCompania: createdByCompania);
        }).ToList();
    }

    private static bool TryGetGuid(JsonElement obj, string property, out Guid value)
    {
        value = default;
        return obj.ValueKind == JsonValueKind.Object
            && obj.TryGetProperty(property, out var el)
            && el.ValueKind == JsonValueKind.String
            && Guid.TryParse(el.GetString(), out value);
    }

    internal static ProcedureInstanceDetailDto ToDetail(
        ProcedureInstance e,
        IReadOnlyList<ProcedureInstanceEventDto>? events = null,
        StatusHistoryActorInfo? actorInfo = null,
        ProcedureInstanceRevocationEligibilityDto? revocationEligibility = null,
        ProcedureInstanceActiveRevocationRequestDto? activeRevocationRequest = null,
        ProcedureInstanceRevocationDecisionDto? lastRevocationDecision = null)
    {
        var names = actorInfo?.Names ?? EmptyActorMap;
        var emails = actorInfo?.Emails ?? EmptyActorMap;
        var companias = actorInfo?.Companias ?? EmptyActorMap;

        return new ProcedureInstanceDetailDto(
            e.Id,
            e.ReferenceNumber,
            e.Status,
            e.ProcedureTypeId,
            e.TenantId,
            e.CreatedAt,
            e.SubmittedAt,
            e.CompletedAt,
            e.FieldValues
                .Select(f => new ProcedureInstanceFieldValueDto(f.FormFieldId, f.FieldKey, f.ValueText, f.ValueJson, f.Source))
                .ToList(),
            // Trazabilidad cronológica: EF materializa la colección sin orden garantizado — se
            // ordena por fecha/hora con desempate estable por Id (mismo criterio que el endpoint
            // de status-history) para que el Expediente pinte las transiciones en orden real.
            e.StatusHistory
                .OrderBy(h => h.ChangedAt)
                .ThenBy(h => h.Id)
                .Select(h => new ProcedureInstanceStatusHistoryDto(
                    h.FromStatus, h.ToStatus, h.ChangedAt, h.Reason, BuildObservationMetadata(h.Metadata),
                    ChangedByName: h.ChangedBy is { } cb1 && names.TryGetValue(cb1, out var n) ? n : null,
                    ChangedByEmail: h.ChangedBy is { } cb2 && emails.TryGetValue(cb2, out var em) ? em : null,
                    ChangedByCompania: h.ChangedBy is { } cb3 && companias.TryGetValue(cb3, out var co) ? co : null))
                .ToList(),
            e.Actors
                .Select(a => new ProcedureInstanceActorDto(a.ActorType, a.DocumentType, a.DocumentNumber, a.FullName, a.Email))
                .ToList(),
            e.DraftFinalizedAt,
            e.CurrentStep,
            e.SubsanacionActiva,
            e.SubsanacionCount,
            e.Prioritario,
            events ?? [],
            revocationEligibility,
            activeRevocationRequest,
            lastRevocationDecision,
            RejectedFrom: e.RejectedFrom);
    }

    private static readonly Dictionary<Guid, string> EmptyActorMap = [];

    /// <summary>
    /// HU #10871 — recorta el metadata jsonb persistido en <c>procedure_instance_status_history</c> al
    /// checklist HÍBRIDO (<c>motivo</c> + <c>items</c>) que el frontend necesita para pintar el panel de
    /// subsanación (<c>SubsanacionObservation</c>, HU #10871/#10872). Deliberadamente NO reenvía
    /// <c>FieldSnapshot</c> (baseline interno de re-radicación) ni <c>ot_tenant_id</c>/
    /// <c>approver_tenant_id</c> (plomería de auditoría cross-tenant) — esas claves quedan solo en la
    /// persistencia. Devuelve <c>null</c> cuando la entrada no trae observación (transiciones sin
    /// checklist, p. ej. aprobar/rechazar sin subsanación) para no ensuciar el contrato con JSON vacío.
    /// </summary>
    private static string? BuildObservationMetadata(string? metadataJson)
    {
        var observation = SubsanacionObservation.FromJson(metadataJson);
        if (observation is null || (observation.Motivo is null && observation.Items.Count == 0))
            return null;

        return JsonSerializer.Serialize(new
        {
            motivo = observation.Motivo,
            items = observation.Items.Select(i => new { campo = i.Campo, detalle = i.Detalle }),
        });
    }
}
