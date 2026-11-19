using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Estados;

namespace Flit.Tramites.Application.UseCases.ProcedureInstances;

// ── DTOs ─────────────────────────────────────────────────────────────────────

/// <summary>Una persona del reenvío múltiple: su validación y, opcional, el correo corregido.</summary>
public sealed record AdminReenviarValidacionLoteItem(Guid ValidationId, string? Email);

public sealed record AdminReenviarValidacionesLoteCommand(
    Guid InstanceId,
    Guid TenantId,
    IReadOnlyList<AdminReenviarValidacionLoteItem> Items,
    Guid? ChangedByUserId);

/// <summary>
/// Resultado POR PERSONA. Con <see cref="Error"/> null el reenvío salió (o quedó en cola: <see cref="Queued"/>);
/// si no, <see cref="Error"/> trae el mismo código que el reenvío individual y <see cref="ErrorDetail"/> el
/// detalle cuando lo hay.
/// </summary>
public sealed record AdminReenviarValidacionLoteItemResult(
    Guid ValidationId,
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

        var items = (command.Items ?? [])
            .Where(i => i.ValidationId != Guid.Empty)
            .DistinctBy(i => i.ValidationId)
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
                    command.InstanceId, item.ValidationId, command.TenantId, item.Email, command.ChangedByUserId),
                ct).ConfigureAwait(false);

            if (error == TramiteEstadoErrores.IdentidadReenvioNoDisponible)
                return (null, error, errorDetail);

            resultados.Add(new AdminReenviarValidacionLoteItemResult(item.ValidationId, error, errorDetail, cooldown, result));
        }

        return (new AdminReenviarValidacionesLoteResult(resultados), null, null);
    }
}
