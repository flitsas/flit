namespace Flit.Modules.Security.Domain.Modules;

public interface ISecurityModuleRepository
{
    Task<bool> CodeExistsAsync(string code, Guid? excludeId, CancellationToken ct);
    Task<Guid> CreateAsync(SecurityModuleData data, CancellationToken ct);
    Task<SecurityModuleDetail?> GetByIdAsync(Guid id, CancellationToken ct);
    Task UpdateAsync(Guid id, UpdateModuleData data, CancellationToken ct);
    Task DeactivateAsync(Guid id, CancellationToken ct);
    Task<bool> HasActivePermissionsAsync(Guid id, CancellationToken ct);
    Task SoftDeleteAsync(Guid id, CancellationToken ct);
    Task<IReadOnlyList<SecurityModuleSummary>> ListAsync(CancellationToken ct);
}

public sealed record SecurityModuleData(string Code, string Name, string? Description, short SortOrder);
public sealed record UpdateModuleData(string Name, string? Description, short SortOrder);
public sealed record SecurityModuleDetail(Guid Id, string Code, string Name, string? Description, short SortOrder, bool IsActive, DateTimeOffset? DeletedAt);
public sealed record SecurityModuleSummary(Guid Id, string Code, string Name, string? Description, short SortOrder, bool IsActive, int PermissionCount);
