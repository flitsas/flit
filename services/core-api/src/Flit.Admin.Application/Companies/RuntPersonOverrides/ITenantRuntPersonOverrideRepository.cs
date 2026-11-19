namespace Flit.Admin.Application.Companies.RuntPersonOverrides;

/// <summary>
/// Repositorio de overrides manuales de persona RUNT por tenant (HU #14642).
/// Solo el SuperAdmin escribe; el decorador de consultas solo lee.
/// </summary>
public interface ITenantRuntPersonOverrideRepository
{
    /// <summary>Devuelve todos los overrides de un tenant, activos e inactivos.</summary>
    Task<IReadOnlyList<TenantRuntPersonOverrideView>> ListByTenantAsync(
        Guid tenantId,
        string? documentTypeFilter,
        CancellationToken ct);

    /// <summary>Obtiene un override por su ID (cualquier estado).</summary>
    Task<TenantRuntPersonOverrideView?> GetByIdAsync(Guid id, CancellationToken ct);

    /// <summary>Crea un nuevo override. Lanza <see cref="DuplicateRuntOverrideException"/> si ya existe uno activo.</summary>
    Task<TenantRuntPersonOverrideView> CreateAsync(CreateTenantRuntPersonOverrideRequest request, CancellationToken ct);

    /// <summary>Actualiza campos del override. Los campos null no se modifican.</summary>
    Task<TenantRuntPersonOverrideView?> UpdateAsync(Guid id, UpdateTenantRuntPersonOverrideRequest request, CancellationToken ct);
}

/// <summary>
/// Se intenta crear un override cuando ya existe uno activo para ese (tenant, tipo, número).
/// </summary>
public sealed class DuplicateRuntOverrideException(Guid tenantId, string documentType, string documentNumber)
    : Exception($"Ya existe un override activo para ({documentType}/{documentNumber}) en el tenant {tenantId}.")
{
    public Guid TenantId { get; } = tenantId;
    public string DocumentType { get; } = documentType;
    public string DocumentNumber { get; } = documentNumber;
}
