namespace Flit.Modules.Security.Domain.Permissions;

public interface IPermissionRepository
{
    Task<bool> SlugExistsAsync(string slug, CancellationToken ct);
    Task<bool> ModuleExistsAndIsActiveAsync(Guid moduleId, CancellationToken ct);
    Task<Guid> CreateAsync(CreatePermissionData data, CancellationToken ct);
    Task<bool> IsInUseByRolesAsync(Guid id, CancellationToken ct);
    Task DeactivateAsync(Guid id, CancellationToken ct);
    Task SoftDeleteAsync(Guid id, CancellationToken ct);
    Task<bool> ExistsAsync(Guid id, CancellationToken ct);
    Task<IReadOnlyList<PermissionSummary>> ListByModuleAsync(Guid? moduleId, CancellationToken ct);
}

public sealed record CreatePermissionData(
    Guid ModuleId,
    string Slug,
    string Name,
    string Action,
    string HttpMethod,
    string RoutePattern,
    string? Description);

public sealed record PermissionSummary(
    Guid Id,
    string Slug,
    string Name,
    string Action,
    Guid ModuleId,
    string ModuleName,
    string HttpMethod,
    string RoutePattern,
    bool IsActive);
