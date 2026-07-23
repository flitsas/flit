namespace Flit.Admin.Domain.Companies.LegalRepresentatives;

/// <summary>
/// Escrituras del directorio de representantes legales por compañía (HU #10900, ADR-0033). Tenant-
/// scoped: cada operación fija <c>app.current_tenant_id</c> (RLS), igual que el baúl de firmas. La
/// unicidad del par (compañía, representante) por tenant la garantiza en última instancia el índice
/// único de BD. <c>DocumentNumber</c> es PII: no loguear.
/// </summary>
public interface ILegalRepresentativeRepository
{
    /// <summary>
    /// Upsert de la compañía representada por <c>(tenant, NIT)</c>: crea si no existe, o actualiza sus
    /// datos mutables si ya existe. Devuelve el id de la dimensión.
    /// </summary>
    Task<Guid> UpsertRepresentedCompanyAsync(
        UpsertRepresentedCompanyData data,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Persiste un representante (alta o edición) junto con sus tipos de trámite (puente) y el vínculo
    /// de firma/identidad resuelto. Devuelve el id del representante.
    /// </summary>
    Task<Guid> SaveAsync(SaveLegalRepresentativeData data, CancellationToken cancellationToken = default);

    /// <summary>Baja lógica del representante. <c>false</c> si no existe o ya estaba inactivo.</summary>
    Task<bool> DeactivateAsync(
        Guid tenantId,
        Guid id,
        Guid? changedBy,
        CancellationToken cancellationToken = default);
}

/// <summary>Datos del upsert de compañía representada. <c>DocumentNumber</c> (NIT) es PII: no loguear.</summary>
public sealed record UpsertRepresentedCompanyData(
    Guid TenantId,
    string DocumentNumber,
    string Name,
    string? Email,
    string? Address,
    string? City,
    string? Phone,
    Guid? ActorBy);

/// <summary>
/// Datos de alta/edición de un representante. <c>Id</c> nulo = alta. <c>SignatureVaultId</c> y
/// <c>IdentityValidationRef</c> son el resultado del resolutor de firma/identidad (excluyentes).
/// <c>DocumentNumber</c> es PII (@pii:high): no loguear.
/// </summary>
public sealed record SaveLegalRepresentativeData(
    Guid TenantId,
    Guid? Id,
    Guid RepresentedCompanyId,
    string DocumentType,
    string DocumentNumber,
    string FirstLastName,
    string? SecondLastName,
    string Name,
    string? Email,
    string? Address,
    string? City,
    string? Phone,
    Guid? SignatureVaultId,
    Guid? IdentityValidationRef,
    IReadOnlyList<Guid> ProcedureTypeIds,
    Guid? ActorBy);
