namespace Flit.Infrastructure.Persistence.Entities.Admin;

/// <summary>
/// Escritura (PDF) de una compañía — <c>admin.company_deeds</c> (HU #10900, ADR-0033). Tenant-scoped
/// (RLS por <c>tenant_id</c>), con vigencia [<c>VigenciaDesde</c>, <c>VigenciaHasta</c>] y baja
/// lógica (<c>IsActive</c>). El PDF vive en storage (<c>StoragePath</c>/<c>StorageSha256</c>), nunca
/// en la fila.
/// </summary>
public sealed class CompanyDeedEntity
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    /// <summary>
    /// Representante que asoció la escritura (Feature #10929). <c>null</c> en escrituras legadas
    /// (asociadas a la empresa antes del ajuste): esas NO aparecen en el detalle de ningún representante.
    /// </summary>
    public Guid? RepresentativeId { get; set; }

    public string Description { get; set; } = string.Empty;

    public string StoragePath { get; set; } = string.Empty;

    public string StorageSha256 { get; set; } = string.Empty;

    public DateOnly VigenciaDesde { get; set; }

    public DateOnly VigenciaHasta { get; set; }

    public bool IsActive { get; set; } = true;

    public long RowVersion { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }
}
