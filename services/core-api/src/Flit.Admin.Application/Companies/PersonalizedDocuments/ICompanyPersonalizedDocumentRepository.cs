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
    DateTimeOffset? ActivatedAt,
    DateTimeOffset? DeactivatedAt);

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
    /// Activa la versión <paramref name="id"/> (status → <c>activo</c>) y retira a <c>historico</c> la
    /// activa previa del mismo <c>document_type</c>, si existe. Nunca borra filas (restricción 9).
    /// </summary>
    Task ActivateAsync(Guid tenantId, Guid id, ConfirmCompanyPersonalizedDocumentData data, CancellationToken cancellationToken = default);

    /// <summary>Marca la versión como <c>rechazado</c> (validación de servidor fallida en el confirm).</summary>
    Task RejectAsync(Guid tenantId, Guid id, CancellationToken cancellationToken = default);
}
