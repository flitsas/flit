namespace Flit.Tramites.Domain.Repositories;

/// <summary>
/// HU #13296 (Feature #13282, Épica #13202) — lectura de SOLO LECTURA, cross-tenant, de las validaciones de
/// identidad manuales para el Super Admin. Puerto aparte de <see cref="IProcedureInstanceRepository"/>: es la única
/// lectura que cruza TODAS las compañías a propósito, y mantenerla en su propio sitio deja un único lugar que
/// revisar si alguna vez se sospecha una fuga. Quien la llama es responsable de haber verificado que el caller es
/// Super Admin (el repositorio no conoce al usuario).
/// </summary>
public interface IManualIdentityReviewReadRepository
{
    /// <summary>
    /// Página de validaciones con proveedor <c>manual</c> de todas las compañías, ya filtradas y ordenadas
    /// (pendientes de revisión primero, las más antiguas arriba).
    /// </summary>
    Task<(IReadOnlyList<ManualIdentityReviewRow> Items, int Total)> ListAsync(
        ManualIdentityReviewFilter filter,
        int skip,
        int take,
        CancellationToken ct = default);

    /// <summary>
    /// HU #13297 — detalle de una validación manual de CUALQUIER compañía, o <c>null</c> si no existe o no es del flujo manual.
    /// Las imágenes solo cuentan como disponibles si son las del ciclo ACTUAL (ver <see cref="ManualIdentityReviewDetailRow"/>).
    /// </summary>
    Task<ManualIdentityReviewDetailRow?> GetDetailAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// HU #13297 — referencia a una imagen del ciclo actual: <c>null</c> si la validación no existe o no es manual;
    /// con <see cref="ManualIdentityImageRef.StoragePath"/> en <c>null</c> si esa imagen no existe en el ciclo actual.
    /// </summary>
    Task<ManualIdentityImageRef?> GetImageRefAsync(Guid id, string kind, CancellationToken ct = default);
}

/// <summary>Tipos de imagen de una captura manual (segmento <c>{kind}</c> de la ruta y contrato <c>ManualDetail.images</c>).</summary>
public static class ManualImageKinds
{
    public const string Rostro = "rostro";
    public const string Anverso = "anverso";
    public const string Reverso = "reverso";
    public const string Firma = "firma";

    public static readonly IReadOnlyList<string> Todos = [Rostro, Anverso, Reverso, Firma];

    public static bool IsValid(string? kind) => kind is not null && Todos.Contains(kind, StringComparer.Ordinal);
}

/// <summary>
/// Detalle crudo de una validación manual (contrato <c>ManualDetail</c>). <c>HasRostro…HasFirma</c> son verdaderos solo con
/// captura del ciclo ACTUAL (estado pendiente de revisión o aprobada manualmente): tras un rechazo la fila vuelve a
/// <c>manual_activo</c> (reactivación del Super Admin) conservando las rutas anteriores, que no se muestran; una rechazada (HU #13299) sí las muestra hasta la captura nueva. <c>ConsentAt</c> solo viene si es del ciclo actual
/// (<c>consent_at &gt;= manual_activated_at</c>); <c>LinkExpiresAt</c> solo cuando se espera captura: <c>manual_activo</c> o <c>rechazado</c> con motivo.
/// </summary>
public sealed record ManualIdentityReviewDetailRow(
    Guid Id,
    Guid TenantId,
    Guid? ProcedureInstanceId,
    string? PartyRole,
    string FullName,
    string DocumentNumber,
    string TenantName,
    string Origin,
    string Status,
    DateTimeOffset? ActivatedAt,
    DateTimeOffset? ConsentAt,
    string? ConsentTextVersion,
    bool HasRostro,
    bool HasAnverso,
    bool HasReverso,
    bool HasFirma,
    DateTimeOffset? ReviewedAt,
    string? ReviewedBy,
    string? RejectionReasonCode,
    DateTimeOffset? LinkExpiresAt,
    DateTimeOffset? WaitingSince = null);

/// <summary>Referencia (interna, nunca sale por HTTP) a la imagen de una validación manual en el storage.</summary>
public sealed record ManualIdentityImageRef(
    Guid ValidationId, Guid TenantId, Guid? ProcedureInstanceId, string? PartyRole, string? StoragePath);

/// <summary>Filtros ya saneados del listado manual (todos opcionales).</summary>
public sealed record ManualIdentityReviewFilter(string? Status, string? Origin, string? Text);

/// <summary>
/// Fila cruda del listado manual; el origen ya viene calculado. <c>WaitingSince</c> (HU #13296): instante en que el cliente envió la
/// captura (evento de auditoría <c>manual_captura_recibida</c> más reciente; si no existe, <c>UpdatedAt</c> de la fila). Solo viene
/// con estado <c>pendiente_revision_manual</c>; en cualquier otro estado es <c>null</c> (no hay revisión en curso).
/// </summary>
public sealed record ManualIdentityReviewRow(
    Guid Id,
    string FullName,
    string DocumentNumber,
    string TenantName,
    string Origin,
    string Status,
    DateTimeOffset? ActivatedAt,
    DateTimeOffset? WaitingSince = null);

/// <summary>
/// Orígenes de una validación manual (contrato Épica #13202 §3). Hoy el modelo solo distingue
/// <see cref="Tramite"/> (hay <c>procedure_instance_id</c>), <see cref="Mandatario"/> (party_role = mandatario con
/// ficha) y <see cref="Prevalidacion"/> (standalone ligada a persona). <see cref="RepresentanteLegal"/> está en el
/// contrato pero ninguna fila lo identifica todavía: no se infiere.
/// </summary>
public static class ManualIdentityReviewOrigins
{
    public const string Tramite = "tramite";
    public const string Prevalidacion = "prevalidacion";
    public const string Mandatario = "mandatario";
    public const string RepresentanteLegal = "representante_legal";

    public static readonly IReadOnlyList<string> Todos = [Tramite, Prevalidacion, Mandatario, RepresentanteLegal];
}
