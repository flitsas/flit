namespace Flit.Admin.Application.Companies.PersonalizedDocuments;

/// <summary>Fila de <c>admin.company_personalized_documents</c> expuesta a la Application.</summary>
public sealed record CompanyPersonalizedDocumentRecord(
    Guid Id,
    Guid TenantId,
    string DocumentType,
    int Version,
    string Status,
    bool IsActive,
    string Filename,
    string StoragePath,
    string StorageSha256,
    long SizeBytes,
    int? PageCount,
    string? Notes,
    DateTimeOffset CreatedAt,
    Guid? CreatedBy,
    DateTimeOffset? ActivatedAt,
    Guid? ActivatedBy,
    DateTimeOffset? DeactivatedAt,
    Guid? DeactivatedBy);

/// <summary>Desenlace de <see cref="ICompanyPersonalizedDocumentRepository.ReactivateAsync"/> (HU #11314).</summary>
public enum PersonalizedDocumentReactivationOutcome
{
    /// <summary>La versión quedó (o ya estaba) activa.</summary>
    Reactivated,

    /// <summary>No existe (o es de otro tenant — negativo de aislamiento).</summary>
    NotFound,

    /// <summary>La versión está en <c>pendiente</c> o <c>rechazado</c>: no se puede activar (409).</summary>
    InvalidStatus,
}

/// <summary>Resultado de reactivar una versión: el desenlace + la versión numérica (para la respuesta 200).</summary>
public sealed record ReactivateCompanyPersonalizedDocumentResult(PersonalizedDocumentReactivationOutcome Outcome, int? Version);

/// <summary>Datos para crear una versión nueva (nace en <c>pendiente</c>).</summary>
public sealed record SaveCompanyPersonalizedDocumentData(
    Guid TenantId,
    string DocumentType,
    int Version,
    string Filename,
    string StoragePath,
    string StorageSha256Declared,
    long SizeBytesDeclared,
    Guid? CreatedBy);

/// <summary>Datos verificados en servidor con los que se activa una versión al confirmar.</summary>
public sealed record ConfirmCompanyPersonalizedDocumentData(
    string StorageSha256,
    long SizeBytes,
    int PageCount,
    Guid? ActivatedBy);

/// <summary>
/// Repositorio de versiones de documento personalizado (HU #11313, ADR-0042). <b>Toda</b> consulta
/// filtra <c>tenant_id</c> de forma EXPLÍCITA: el RLS de este repositorio es decorativo (sin
/// <c>FORCE ROW LEVEL SECURITY</c> y con la aplicación como owner, las políticas no se evalúan), así
/// que el aislamiento multi-tenant tiene que estar escrito a mano en cada método.
/// </summary>
public interface ICompanyPersonalizedDocumentRepository
{
    /// <summary>Siguiente número de versión para <c>(tenantId, documentType)</c> — 1 si no existe ninguna.</summary>
    Task<int> GetNextVersionAsync(Guid tenantId, string documentType, CancellationToken cancellationToken = default);

    /// <summary>Crea la fila en <c>pendiente</c> y devuelve su id.</summary>
    Task<Guid> CreatePendingAsync(SaveCompanyPersonalizedDocumentData data, CancellationToken cancellationToken = default);

    /// <summary>Una fila por id, con <c>WHERE tenant_id</c> explícito. <c>null</c> si no existe o es de otro tenant.</summary>
    Task<CompanyPersonalizedDocumentRecord?> GetByIdAsync(Guid tenantId, Guid id, CancellationToken cancellationToken = default);

    /// <summary>Historial completo (todas las versiones, todos los tipos) del tenant, tenant-scoped.</summary>
    Task<IReadOnlyList<CompanyPersonalizedDocumentRecord>> ListByTenantAsync(Guid tenantId, CancellationToken cancellationToken = default);

    /// <summary>
    /// HU #11316 — la versión ACTIVA de <paramref name="documentType"/> para <paramref name="tenantId"/>,
    /// o <c>null</c> si no hay ninguna (comportamiento actual: se genera el documento del sistema). Es la
    /// consulta que consume el pipeline de generación (<c>IPersonalizedDocumentResolver</c>), llaveada por
    /// <c>instance.TenantId</c> — nunca por el tenant del usuario autenticado. <c>WHERE tenant_id</c>
    /// explícito, igual que el resto del repositorio.
    /// </summary>
    Task<CompanyPersonalizedDocumentRecord?> GetActiveAsync(
        Guid tenantId, string documentType, CancellationToken cancellationToken = default);

    /// <summary>
    /// Activa la versión <paramref name="id"/> (status → <c>activo</c>) y retira a <c>historico</c> la
    /// activa previa del mismo <c>document_type</c>, si existe. Nunca borra filas (restricción 9).
    /// </summary>
    Task ActivateAsync(Guid tenantId, Guid id, ConfirmCompanyPersonalizedDocumentData data, CancellationToken cancellationToken = default);

    /// <summary>Marca la versión como <c>rechazado</c> (validación de servidor fallida en el confirm).</summary>
    Task RejectAsync(Guid tenantId, Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reactiva una versión <c>historico</c> (HU #11314): pasa a <c>activo</c> y retira a
    /// <c>historico</c> la activa previa del mismo <c>document_type</c>, si existe y es distinta.
    /// Repetible en cualquier orden — reactivar la ya activa es un no-op idempotente. Nunca borra
    /// filas (restricción 9). <c>WHERE tenant_id</c> explícito: un id de otro tenant es
    /// <see cref="PersonalizedDocumentReactivationOutcome.NotFound"/>.
    /// </summary>
    Task<ReactivateCompanyPersonalizedDocumentResult> ReactivateAsync(
        Guid tenantId, Guid id, Guid? reactivatedBy, CancellationToken cancellationToken = default);

    /// <summary>
    /// Desactiva la versión activa de <paramref name="documentType"/> («volver al documento del
    /// sistema», HU #11314): ninguna versión queda <c>activo</c> y NINGUNA fila ni archivo se borra
    /// (restricción 9). Idempotente: <c>true</c> si retiró una fila activa, <c>false</c> si ya no
    /// había ninguna (no-op).
    /// </summary>
    Task<bool> DeactivateActiveAsync(
        Guid tenantId, string documentType, Guid? deactivatedBy, CancellationToken cancellationToken = default);
}
