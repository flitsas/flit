namespace Flit.Admin.Domain.CompanyDocumentParams;

/// <summary>Persistencia de los parámetros documentales por compañía gestora (HU #10521, RF31).</summary>
public interface ICompanyDocumentParamRepository
{
    /// <summary>Lista los parámetros de una gestora, ordenados por código de documento.</summary>
    Task<IReadOnlyList<CompanyDocumentParamItem>> ListByTenantAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Crea o actualiza (upsert por tenant + código) el estado de un tipo de documento.
    /// Devuelve el ítem resultante.
    /// </summary>
    Task<CompanyDocumentParamItem> UpsertAsync(
        Guid tenantId,
        string documentTypeCode,
        string state,
        Guid? userId,
        CancellationToken cancellationToken = default);
}
