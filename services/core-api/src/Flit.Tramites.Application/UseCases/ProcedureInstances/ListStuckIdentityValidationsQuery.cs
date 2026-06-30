using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;

namespace Flit.Tramites.Application.UseCases.ProcedureInstances;

/// <summary>
/// Una validación de identidad ATASCADA (dead-letter): agotó los reintentos automáticos de su cola. Puede
/// venir de la cola de ENVÍO al proveedor (<c>Kind = "envio"</c>, validación en <c>error_envio</c>) o de la
/// outbox de COMPLETADO (<c>Kind = "encadenamiento"</c>, encadenamiento firma/FUR). Incluye el NOMBRE y el
/// DOCUMENTO de la persona validada (vista autenticada del gestor; la UI enmascara el documento a los
/// últimos 4) para saber de quién es. No expone el payload. <c>Nombre/TipoDoc/Documento</c> pueden ser null
/// si la validación ya no existe.
/// </summary>
public sealed record StuckIdentityValidationDto(
    Guid Id,
    Guid ValidationId,
    string EventType,
    int Attempts,
    DateTimeOffset OccurredAt,
    DateTimeOffset CreatedAt,
    string? Nombre,
    string? TipoDoc,
    string? Documento,
    string Kind);

/// <summary>Respuesta: los eventos atascados + el total devuelto (acotado a <see cref="ListStuckIdentityValidationsHandler.MaxRows"/>).</summary>
public sealed record StuckIdentityValidationsResponse(
    IReadOnlyList<StuckIdentityValidationDto> Stuck,
    int Total,
    int MaxDeliveryAttempts);

/// <summary>
/// Lista los eventos de validación de identidad ATASCADOS (dead-letter) del tenant para observabilidad
/// operativa (HU #10349 fase 2): pendientes que agotaron los reintentos del
/// <c>IdentityValidationOutboxProcessor</c> y por lo tanto NO se reintentan más. Sirve para que el
/// operador detecte trámites cuyo encadenamiento async (firma/FUR) quedó trabado y los reencole
/// manualmente (reset de <c>attempts</c>). Solo lectura, acotado a <see cref="MaxRows"/>.
/// </summary>
public sealed class ListStuckIdentityValidationsHandler(IIdentityValidationOutboxRepository repo)
{
    /// <summary>Cap de filas devueltas (vista de monitoreo, no exporta histórico completo).</summary>
    public const int MaxRows = 200;

    public async Task<StuckIdentityValidationsResponse> HandleAsync(Guid tenantId, CancellationToken ct = default)
    {
        var rows = await repo.ListStuckAsync(tenantId, MaxRows, ct);

        var dtos = rows
            .Select(o => new StuckIdentityValidationDto(
                o.Id, o.ValidationId, o.EventType, o.Attempts, o.OccurredAt, o.CreatedAt,
                o.Nombre, o.TipoDoc, o.Documento, o.Kind))
            .ToList();

        return new StuckIdentityValidationsResponse(dtos, dtos.Count, IdentityValidationOutbox.MaxDeliveryAttempts);
    }
}

/// <summary>
/// Reencola ("desatasca") un evento de identidad atascado (dead-letter): reinicia sus intentos para que
/// el worker lo vuelva a procesar (HU #10349 fase 2). Devuelve <c>"not_found"</c> si no hay un evento
/// atascado con ese id para el tenant (ya reencolado/despachado), o <c>null</c> si quedó reencolado.
/// </summary>
public sealed class RequeueStuckIdentityValidationHandler(IIdentityValidationOutboxRepository repo)
{
    public async Task<string?> HandleAsync(Guid tenantId, Guid id, CancellationToken ct = default)
    {
        var requeued = await repo.RequeueStuckAsync(tenantId, id, ct);
        return requeued ? null : "not_found";
    }
}

/// <summary>
/// Reencola TODOS los eventos de identidad atascados (dead-letter) del tenant de una vez (HU #10349
/// fase 2). Devuelve cuántos se reencolaron (0 si no había ninguno). Idempotente.
/// </summary>
public sealed class RequeueAllStuckIdentityValidationsHandler(IIdentityValidationOutboxRepository repo)
{
    public Task<int> HandleAsync(Guid tenantId, CancellationToken ct = default) =>
        repo.RequeueAllStuckAsync(tenantId, ct);
}
