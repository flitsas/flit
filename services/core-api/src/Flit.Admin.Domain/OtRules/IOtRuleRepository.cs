namespace Flit.Admin.Domain.OtRules;

/// <summary>Repositorio de reglas OT sobre <c>admin.ot_feature_flags</c> (HU #10221).</summary>
public interface IOtRuleRepository
{
    Task<OtRule> CreateAsync(
        Guid tenantId,
        string name,
        IReadOnlyList<OtRuleCondition> conditions,
        string logic,
        OtRuleAction action,
        Guid? createdBy,
        CancellationToken cancellationToken = default);

    Task<OtRule?> GetByIdAsync(
        Guid tenantId,
        Guid ruleId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OtRule>> ListByTenantAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OtRule>> ListEnabledByTenantAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default);

    Task<OtRule?> UpdateEnabledAsync(
        Guid tenantId,
        Guid ruleId,
        bool isEnabled,
        Guid? changedBy,
        CancellationToken cancellationToken = default);
}
