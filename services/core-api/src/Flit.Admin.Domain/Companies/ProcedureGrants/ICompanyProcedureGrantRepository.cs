namespace Flit.Admin.Domain.Companies.ProcedureGrants;

/// <summary>
/// Repositorio de los tipos de trámite habilitados por compañía (FEATURE-08, grant model).
/// La implementación (Infrastructure) aplica el contexto RLS de SuperAdmin
/// (<c>set_config('app.current_tenant_id', ...)</c>) al tenant destino y persiste el grant + su
/// auditoría de forma atómica. Las lecturas van por owner-bypass + <c>WHERE tenant_id</c>.
/// </summary>
public interface ICompanyProcedureGrantRepository
{
    /// <summary>
    /// Habilita el tipo de trámite para la compañía. Idempotente: si ya existe devuelve <c>false</c>
    /// sin duplicar fila ni auditoría; si lo crea devuelve <c>true</c> y registra auditoría.
    /// </summary>
    Task<bool> AddGrantAsync(
        Guid tenantId,
        Guid procedureTypeId,
        Guid? createdBy,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deshabilita (elimina) el grant. Devuelve <c>false</c> si no existía (→ 404); si lo elimina
    /// devuelve <c>true</c> y registra auditoría.
    /// </summary>
    Task<bool> RemoveGrantAsync(
        Guid tenantId,
        Guid procedureTypeId,
        Guid? changedBy,
        CancellationToken cancellationToken = default);

    /// <summary>Ids de los tipos de trámite habilitados de la compañía.</summary>
    Task<IReadOnlyList<Guid>> ListEnabledProcedureTypeIdsAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default);
}
