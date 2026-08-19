namespace Flit.Tramites.Domain.Integration;

/// <summary>
/// Bug #11612 — nombre de la COMPAÑÍA RADICADORA del trámite. La fuente autoritativa es el tenant
/// dueño del expediente (<c>procedure_instances.tenant_id</c> → <c>identity.tenants.legal_name</c>):
/// es la misma compañía que el organismo ve como "cliente" en su bandeja
/// (<c>OtClientProcedureRepository</c> proyecta <c>ClientTenantId = p.TenantId</c> y lo resuelve
/// contra <c>Tenants.LegalName</c>) y la misma fila que administra
/// <c>/api/v1/admin/companies/{tenantId}</c>.
///
/// <para>La resolución es SIEMPRE por el id del tenant del propio trámite: no admite búsqueda ni
/// listado, así que no puede cruzar tenants por construcción.</para>
/// </summary>
public interface ICompaniaRadicadoraDirectory
{
    /// <summary>Razón social del tenant, o <c>null</c> si no existe o no tiene nombre registrado.</summary>
    Task<string?> GetRazonSocialAsync(Guid tenantId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Implementación que NUNCA resuelve — default seguro para los tests de aplicación que no ejercitan
/// la portada y para cualquier composición que no cablee el directorio: el campo queda como estaba.
/// </summary>
public sealed class NullCompaniaRadicadoraDirectory : ICompaniaRadicadoraDirectory
{
    public static NullCompaniaRadicadoraDirectory Instance { get; } = new();

    public Task<string?> GetRazonSocialAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
        Task.FromResult<string?>(null);
}
