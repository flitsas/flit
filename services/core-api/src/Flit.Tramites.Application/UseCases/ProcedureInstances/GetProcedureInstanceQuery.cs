using System.Text.Json;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;
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
/// tracking (reenvío de validación y reasignación de gestor): los demás tipos (<c>anular_admin</c>,
/// <c>cambio_estado</c>, …) ya están cubiertos por <see cref="ProcedureInstanceStatusHistoryDto"/> y
/// se omiten aquí para no duplicar el timeline. Los campos específicos de cada tipo quedan null en el
/// otro (contrato "ancho", más simple para el frontend que un payload JSON opaco).
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
    string? CreatedByCompania = null);

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
    // LEGACY (ADR-0059): sub-estado de placa, se retira en HU #12603. La ruta de placa ya vive en Status.
    string? PlateFlowStatus = null,
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
    /// <summary>ADR-0059 — estado desde el que el OT rechazó por última vez (entregado | preasignacion); null si no aplica.</summary>
    string? RejectedFrom = null);

public sealed class GetProcedureInstanceHandler(IProcedureInstanceRepository repo)
{
    /// <summary>Tipos de <see cref="ProcedureInstanceEvent"/> que el dashboard muestra en el tracking
    /// (ver XML doc de <see cref="ProcedureInstanceEventDto"/> sobre por qué solo estos dos).</summary>
    private static readonly HashSet<string> RelevantEventTypes = new(StringComparer.Ordinal)
    {
        "reenvio_validacion_admin",
        "reasignar_gestor_admin",
    };

    public async Task<(ProcedureInstanceDetailDto? Result, string? Error)> HandleAsync(
        Guid id,
        Guid tenantId,
        CancellationToken ct = default)
    {
        var instance = await repo.GetByIdWithDetailsAsync(id, tenantId, ct);
        if (instance is null)
            return (null, "not_found");

        return (await BuildDetailAsync(repo, instance, ct).ConfigureAwait(false), null);
    }

    /// <summary>
    /// ÚNICO punto que arma el <see cref="ProcedureInstanceDetailDto"/> a partir del agregado cargado:
    /// lo usan el detalle propio (este handler) y el consolidado de la red
    /// (<c>NetworkGetProcedureInstanceHandler</c>, HU #12358), cuyo contrato es «los MISMOS campos que el
    /// detalle propio». Toda resolución adicional que enriquezca el detalle (eventos administrativos,
    /// autores del historial, …) va AQUÍ y nunca solo en <see cref="HandleAsync"/>: si se añade en el
    /// handler propio y no aquí, la vista de red deja de ser equivalente sin que compile nada distinto
    /// (fallo de CI del PR #370 al integrar el Bug #12526).
    /// </summary>
    internal static async Task<ProcedureInstanceDetailDto> BuildDetailAsync(
        IProcedureInstanceRepository repo, ProcedureInstance instance, CancellationToken ct)
    {
        var events = await BuildEventsAsync(repo, instance.Events, ct).ConfigureAwait(false);
        var actorInfo = await BuildStatusHistoryActorInfoAsync(repo, instance.StatusHistory, ct).ConfigureAwait(false);
        return ToDetail(instance, events, actorInfo);
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
        StatusHistoryActorInfo? actorInfo = null)
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
            e.PlateFlowStatus,
            e.CurrentStep,
            e.SubsanacionActiva,
            e.SubsanacionCount,
            e.Prioritario,
            events ?? [],
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
