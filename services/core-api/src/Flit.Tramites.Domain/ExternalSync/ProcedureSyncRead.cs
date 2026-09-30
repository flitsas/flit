namespace Flit.Tramites.Domain.ExternalSync;

/// <summary>
/// HU #13076 (Feature #13066, Épica #12737, ADR-0066) — posición en el feed de sincronización externa:
/// la transacción que selló el cambio y su versión. El feed se recorre en este orden, no solo por
/// versión: la versión se asigna al escribir y no al confirmar, así que ordenar solo por versión deja
/// atrás los cambios de una transacción larga que confirma después de otra con versión mayor.
/// </summary>
public readonly record struct ProcedureSyncPosition(ulong Transaction, long Version) : IComparable<ProcedureSyncPosition>
{
    public int CompareTo(ProcedureSyncPosition other)
    {
        var byTransaction = Transaction.CompareTo(other.Transaction);
        return byTransaction != 0 ? byTransaction : Version.CompareTo(other.Version);
    }

    public static bool operator <(ProcedureSyncPosition left, ProcedureSyncPosition right) => left.CompareTo(right) < 0;

    public static bool operator >(ProcedureSyncPosition left, ProcedureSyncPosition right) => left.CompareTo(right) > 0;

    public static bool operator <=(ProcedureSyncPosition left, ProcedureSyncPosition right) => left.CompareTo(right) <= 0;

    public static bool operator >=(ProcedureSyncPosition left, ProcedureSyncPosition right) => left.CompareTo(right) >= 0;
}

/// <summary>Un trámite cambiado: lo mínimo para armar el ítem (HU #13079) y avanzar el cursor (HU #13081).</summary>
public sealed record ProcedureSyncChange(
    Guid ProcedureInstanceId,
    Guid TenantId,
    ProcedureSyncPosition Position,
    DateTimeOffset ChangedAt,
    bool IsDeleted);

/// <summary>
/// Página pedida. <see cref="After"/> (cursor) y <see cref="Since"/> (arranque por fecha) son
/// excluyentes; sin ninguno se lee desde el principio.
/// </summary>
public sealed record ProcedureSyncPageRequest(
    ProcedureSyncPosition? After,
    DateTimeOffset? Since,
    int PageSize,
    TimeSpan StabilityLag)
{
    public static ProcedureSyncPageRequest FromCursor(ProcedureSyncPosition after, int pageSize, TimeSpan stabilityLag) =>
        new(after, null, pageSize, stabilityLag);

    public static ProcedureSyncPageRequest FromSince(DateTimeOffset since, int pageSize, TimeSpan stabilityLag) =>
        new(null, since, pageSize, stabilityLag);
}

/// <summary>
/// HU #13076 — lectura por versión entre compañías. Entrega solo trámites radicados al menos una vez
/// (y una vez dentro no salen), nunca los migrados desde FLIT 1, y solo cambios ya estables: de
/// transacciones anteriores a la más antigua en curso y con más de <c>StabilityLag</c> de antigüedad.
/// Es el ÚNICO componente autorizado a leer trámites de todas las compañías para el feed externo.
/// </summary>
public interface IProcedureSyncReadRepository
{
    Task<IReadOnlyList<ProcedureSyncChange>> ReadChangesAsync(
        ProcedureSyncPageRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// HU #13079 — la misma página con el ítem completo del contrato (v3.1 §4), leído en la misma
    /// instantánea que la página. Sin enmascarar datos personales.
    /// </summary>
    Task<IReadOnlyList<ProcedureSyncEntry>> ReadItemsAsync(
        ProcedureSyncPageRequest request, CancellationToken cancellationToken = default);
}
