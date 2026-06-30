using Flit.Tramites.Domain.Entities;

namespace Flit.Tramites.Domain.Repositories;

/// <summary>
/// Tipo de atasco (dead-letter) de una validación de identidad. Distingue las DOS colas que pueden
/// quedar trabadas para que el gestor sepa qué etapa falló y la UI lo etiquete.
/// </summary>
public static class StuckIdentityValidationKinds
{
    /// <summary>Dead-letter de la cola de ENVÍO al proveedor (validación en <c>error_envio</c>): el envío a
    /// Kyverum/proveedor agotó los reintentos. Reencolar = volver a <c>pendiente_envio</c>.</summary>
    public const string Envio = "envio";

    /// <summary>Dead-letter de la outbox de COMPLETADO (encadenamiento firma/FUR): el evento
    /// <c>completed</c> agotó los reintentos. Reencolar = reiniciar <c>attempts</c>.</summary>
    public const string Encadenamiento = "encadenamiento";
}

/// <summary>
/// Proyección de lectura de un evento atascado + los datos de la PERSONA validada (resueltos desde la
/// validación biométrica referenciada por <c>validation_id</c>), para que el gestor sepa de quién es la
/// validación que se trabó. <c>Nombre/TipoDoc/Documento</c> pueden ser null si la validación ya no existe.
/// <c>Kind</c> distingue el atasco de la cola de envío (<c>envio</c>) del del encadenamiento async
/// (<c>encadenamiento</c>); ver <see cref="StuckIdentityValidationKinds"/>.
/// </summary>
public sealed record StuckIdentityValidationRow(
    Guid Id,
    Guid ValidationId,
    string EventType,
    int Attempts,
    DateTimeOffset OccurredAt,
    DateTimeOffset CreatedAt,
    string? Name,
    string? DocumentType,
    string? DocumentNumber,
    string Kind);

/// <summary>
/// Acceso de solo lectura + reencolado para observabilidad operativa de validación de identidad
/// (HU #10349 fase 2). Unifica las DOS colas que pueden quedar en dead-letter: (1) la outbox de COMPLETADO
/// (encadenamiento firma/FUR), y (2) la cola de ENVÍO al proveedor (validaciones en <c>error_envio</c>,
/// HU #10347/#10348). El despacho/reintento lo hacen los workers en Infraestructura; aquí se exponen los
/// atascados para que el gestor los vea y los pueda reencolar manualmente desde la UI.
/// </summary>
public interface IIdentityValidationOutboxRepository
{
    /// <summary>
    /// Lista las validaciones de identidad ATASCADAS del tenant, de AMBAS colas: outbox de completado
    /// (<c>published_at IS NULL AND attempts &gt;= <see cref="IdentityValidationOutbox.MaxDeliveryAttempts"/></c>)
    /// y cola de envío (<c>estado = error_envio</c>). Más antiguas primero. Solo lectura, acotado a
    /// <paramref name="limit"/> filas. Cada fila trae su <c>Kind</c> (ver <see cref="StuckIdentityValidationKinds"/>).
    /// </summary>
    Task<IReadOnlyList<StuckIdentityValidationRow>> ListStuckAsync(
        Guid tenantId, int limit, CancellationToken ct = default);

    /// <summary>
    /// Reencola ("desatasca") UNA validación atascada del tenant por id, sea de la cola de envío o de la
    /// outbox de completado: si es un evento de outbox dead-letter reinicia <c>attempts</c> a 0; si es una
    /// validación en <c>error_envio</c> la vuelve a <c>pendiente_envio</c> con <c>intentos = 0</c>. En ambos
    /// casos el worker correspondiente la retoma en el próximo ciclo. Devuelve <c>false</c> si no hay nada
    /// atascado con ese id (ya reencolado, despachado o de otro tenant). Idempotente.
    /// </summary>
    Task<bool> RequeueStuckAsync(Guid tenantId, Guid id, CancellationToken ct = default);

    /// <summary>
    /// Reencola TODO lo atascado del tenant de una vez, de ambas colas (outbox de completado + envío).
    /// Devuelve cuántos se reencolaron en total. Idempotente.
    /// </summary>
    Task<int> RequeueAllStuckAsync(Guid tenantId, CancellationToken ct = default);
}
