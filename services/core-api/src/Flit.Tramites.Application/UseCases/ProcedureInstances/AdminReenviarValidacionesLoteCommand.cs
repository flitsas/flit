using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Estados;

namespace Flit.Tramites.Application.UseCases.ProcedureInstances;

// ── DTOs ─────────────────────────────────────────────────────────────────────

/// <summary>
/// Una persona del reenvío múltiple: su validación y, opcional, el correo corregido. HU #14627 —
/// <see cref="ValidationId"/> es OPCIONAL: cuando el actor nunca tuvo una fila propia (DTO
/// <c>no_iniciada</c> de <see cref="ListValidacionesReenviablesHandler"/>), el item identifica a la
/// persona por <see cref="PartyRole"/>/<see cref="Ordinal"/> en su lugar, y
/// <see cref="AdminReenviarValidacionIdentidadHandler"/> la envía por primera vez.
/// </summary>
public sealed record AdminReenviarValidacionLoteItem(Guid? ValidationId, string? PartyRole, int? Ordinal, string? Email);

public sealed record AdminReenviarValidacionesLoteCommand(
    Guid InstanceId,
    Guid TenantId,
    IReadOnlyList<AdminReenviarValidacionLoteItem> Items,
    Guid? ChangedByUserId);

/// <summary>
/// Resultado POR PERSONA. Con <see cref="Error"/> null el reenvío salió (o quedó en cola: <see cref="Queued"/>
/// de <see cref="Result"/>); si no, <see cref="Error"/> trae el mismo código que el reenvío individual y
/// <see cref="ErrorDetail"/> el detalle cuando lo hay. <see cref="ValidationId"/> es el id REAL: el existente
/// si el item lo traía, o el de la fila recién creada cuando era un envío por primera vez (HU #14627); puede
/// ser <c>null</c> si falló antes de crear la fila. <see cref="PartyRole"/>/<see cref="Ordinal"/> hacen eco
/// del item original para que el llamador empareje la respuesta incluso sin <see cref="ValidationId"/> en la
/// solicitud.
/// </summary>
public sealed record AdminReenviarValidacionLoteItemResult(
    Guid? ValidationId,
    string? PartyRole,
    int? Ordinal,
    string? Error,
    string? ErrorDetail,
    int? CooldownMinutos,
    AdminReenviarValidacionIdentidadResult? Result);

public sealed record AdminReenviarValidacionesLoteResult(IReadOnlyList<AdminReenviarValidacionLoteItemResult> Items);

/// <summary>
/// Reenvío de la validación de identidad a VARIAS personas de un trámite en un solo paso (HU #13821, Épica
/// #13817). Cada persona pasa por <see cref="AdminReenviarValidacionIdentidadHandler"/> —mismas reglas,
/// mismo historial, mismo tope/cooldown— y el fallo de una no frena a las demás.
///
/// <para>Única excepción: el estado del TRÁMITE (aprobado/anulado/revocado) es común a todas las personas,
/// así que ese rechazo corta la solicitud completa y se devuelve como error de la solicitud.</para>
///
/// <para>Entre persona y persona se limpia el rastreo de EF (<see cref="IProcedureInstanceRepository.ResetTracking"/>):
/// si el guardado de una choca con otra validación en vuelo (HU #11266), sus cambios quedan pendientes en el
/// contexto y el siguiente <c>SaveChanges</c> los reintentaría, arrastrando el mismo error a las demás.</para>
/// </summary>
public sealed class AdminReenviarValidacionesLoteHandler(
    AdminReenviarValidacionIdentidadHandler reenviar,
    IProcedureInstanceRepository repo)
{
    /// <summary>Tope de personas por solicitud: 4 compradores + 4 vendedores (ADR-0053).</summary>
    public const int MaxItems = 8;

    public const string SolicitudVacia = "solicitud_vacia";
    public const string DemasiadasPersonas = "demasiadas_personas";

    public async Task<(AdminReenviarValidacionesLoteResult? Result, string? Error, string? ErrorDetail)> HandleAsync(
        AdminReenviarValidacionesLoteCommand command,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        // HU #14627 — un item es válido si trae ValidationId (persona con fila propia) O PartyRole+Ordinal
        // (persona sin fila: "enviar por primera vez"). El dedupe usa ValidationId cuando existe; si no,
        // la clave compuesta (PartyRole, Ordinal) identifica a la persona igual de unívocamente.
        var items = (command.Items ?? [])
            .Where(i => (i.ValidationId is Guid vid && vid != Guid.Empty)
                || (!string.IsNullOrWhiteSpace(i.PartyRole) && i.Ordinal is not null))
            .DistinctBy(i => i.ValidationId is Guid vid && vid != Guid.Empty
                ? vid.ToString()
                : $"{i.PartyRole?.Trim().ToLowerInvariant()}|{i.Ordinal}")
            .ToList();
        if (items.Count == 0)
            return (null, SolicitudVacia, "Selecciona al menos una persona para reenviar.");
        if (items.Count > MaxItems)
            return (null, DemasiadasPersonas, $"Se puede reenviar a máximo {MaxItems} personas por solicitud.");

        // Trámite inexistente o de otro tenant: 404 de la solicitud, no un 200 con «no encontrado» por persona
        // (mismo contrato que las demás rutas de gestión avanzada, Bug #12554).
        if (await repo.GetByIdAsync(command.InstanceId, command.TenantId, ct).ConfigureAwait(false) is null)
            return (null, TramiteEstadoErrores.NoEncontrado, null);

        var resultados = new List<AdminReenviarValidacionLoteItemResult>(items.Count);
        foreach (var item in items)
        {
            if (resultados.Count > 0)
                repo.ResetTracking();

            var (result, error, errorDetail, cooldown) = await reenviar.HandleAsync(
                new AdminReenviarValidacionIdentidadCommand(
                    command.InstanceId, item.ValidationId, item.PartyRole, item.Ordinal,
                    command.TenantId, item.Email, command.ChangedByUserId),
                ct).ConfigureAwait(false);

            if (error == TramiteEstadoErrores.IdentidadReenvioNoDisponible)
                return (null, error, errorDetail);

            // HU #14627 — cuando el item llegó sin ValidationId (envío por primera vez), el id REAL es el
            // de la fila recién creada por el handler; sin resultado (falló antes de crearla) se deja null.
            var validationId = item.ValidationId ?? result?.Validation.Id;
            resultados.Add(new AdminReenviarValidacionLoteItemResult(validationId, item.PartyRole, item.Ordinal, error, errorDetail, cooldown, result));
        }

        return (new AdminReenviarValidacionesLoteResult(resultados), null, null);
    }
}
